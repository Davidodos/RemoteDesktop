import { directTransport } from '../transport/direct.ts'
import type { Channel, Transport } from '../transport/index.ts'
import type { ConnectionState, Device, QualityMode, ScreenMeta, ScreenStats } from './types.ts'

/** Wartezeit vor dem nächsten Verbindungsversuch, wächst bis zum Maximum. */
const RECONNECT_BASE_MS = 500
const RECONNECT_MAX_MS = 8000

/** Länge des Kopfs jeder Binärnachricht: x, y, Breite, Höhe als je 16 Bit. */
const HEADER_BYTES = 8

/**
 * So viele noch nicht gezeichnete Ausschnitte hält die App aus. Darüber ist das
 * Handy zu langsam für den Stream — dann ist es besser, den Rückstand zu
 * verwerfen und ein frisches Vollbild anzufordern, als minutenlang veraltete
 * Bildteile nachzuzeichnen.
 */
const MAX_PENDING_REGIONS = 24

/** Kommt so lange gar nichts mehr, gilt die Verbindung als tot. */
const STALL_TIMEOUT_MS = 6000
const STALL_CHECK_MS = 2000

/**
 * So viele erfolglose Versuche, bevor eine Störung gemeldet wird.
 *
 * Wie beim Eingabekanal: der erste Versuch scheitert regelmäßig, ohne dass etwas
 * kaputt ist — der Agent startet gerade neu, oder am Handy hat noch niemand die
 * Aufnahme bestätigt. Der nächste kommt durch. Eine Meldung beim ersten Mal
 * beschreibt deshalb einen Zustand, den es beim Lesen schon nicht mehr gibt.
 */
const ATTEMPTS_BEFORE_COMPLAINING = 3

interface ScreenCallbacks {
  onMeta: (meta: ScreenMeta) => void
  onStats: (stats: ScreenStats) => void
  onState: (state: ConnectionState) => void
  onError: (message: string) => void
  /** Sperrbildschirm, UAC-Dialog oder Vollbildspiel — gerade kommt kein Bild. */
  onAvailability: (available: boolean, reason?: string) => void
}

/**
 * Der Bild-WebSocket zum Agent.
 *
 * Der Agent schickt nicht ständig Vollbilder, sondern nur die Ausschnitte, die
 * sich geändert haben — jeder mit acht Byte Kopf davor. Die Klasse zeichnet sie
 * direkt in ein Canvas in Originalauflösung; Zoom und Seitenverhältnis regelt
 * darüber CSS.
 */
export class ScreenChannel {
  private channel: Channel | undefined
  private canvas: HTMLCanvasElement | undefined
  private context: CanvasRenderingContext2D | undefined

  private reconnectTimer: number | undefined
  private reconnectDelay = RECONNECT_BASE_MS
  private closedByUs = false

  /** Hält die Zeichenreihenfolge trotz asynchroner JPEG-Dekodierung ein. */
  private drawQueue: Promise<void> = Promise.resolve()
  private pendingRegions = 0

  private state: ConnectionState = 'disconnected'

  private stallTimer: number | undefined
  private lastMessageAt = 0

  /** Wie oft seit der letzten stehenden Verbindung erfolglos versucht wurde. */
  private attempts = 0
  /** Ob diese Verbindung überhaupt schon einmal stand. */
  private everConnected = false

  constructor(
    private readonly device: Device,
    private readonly monitor: number,
    private readonly callbacks: ScreenCallbacks,
    private readonly transport: Transport = directTransport(device),
  ) {}

  connect(): void {
    this.closedByUs = false
    this.attempts = 0
    this.everConnected = false
    window.addEventListener('online', this.reconnectNow)
    this.open()
  }

  disconnect(): void {
    this.closedByUs = true
    window.removeEventListener('online', this.reconnectNow)

    if (this.stallTimer !== undefined) {
      clearInterval(this.stallTimer)
      this.stallTimer = undefined
    }

    if (this.reconnectTimer !== undefined) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = undefined
    }

    this.channel?.close()
    this.channel = undefined
    this.setState('disconnected')
  }

  /** Muss vor dem ersten Bild passieren, sonst geht es ins Leere. */
  attachCanvas(canvas: HTMLCanvasElement): void {
    this.canvas = canvas

    // alpha: false spart dem Browser das Alpha-Blending pro Bild — der
    // Bildschirminhalt ist ohnehin deckend.
    this.context = canvas.getContext('2d', { alpha: false }) ?? undefined
  }

  // ---- Befehle ------------------------------------------------------

  /** Nächstes Bild vollständig anfordern, z.B. nach einem Anzeigefehler. */
  refresh(): void {
    this.send({ t: 'refresh' })
  }

  /** Stream anhalten, solange die App im Hintergrund ist. */
  pause(): void {
    this.send({ t: 'pause' })
  }

  resume(): void {
    this.send({ t: 'resume' })
  }

  setQuality(mode: QualityMode): void {
    this.send({ t: 'quality', value: mode })
  }

  // ---- Interna ------------------------------------------------------

  private open(): void {
    this.setState('connecting')

    this.channel = this.transport.screenStream(this.monitor, {
      onOpen: () => {
        this.reconnectDelay = RECONNECT_BASE_MS
        this.attempts = 0
        this.everConnected = true
        this.setState('connected')
        this.watchForStall()
      },

      onText: (data) => {
        this.lastMessageAt = Date.now()
        this.handleText(data)
      },

      onBinary: (data) => {
        this.lastMessageAt = Date.now()
        this.handleFrame(data)
      },

      onClose: () => {
        this.setState('disconnected')

        if (!this.closedByUs) {
          this.scheduleReconnect()
        }
      },

      onError: () => {
        // Nicht beim ersten Mal — siehe {@link ATTEMPTS_BEFORE_COMPLAINING}.
        // Stand die Bildverbindung schon einmal, kümmert sich der Wiederaufbau
        // darum, und die Statusanzeige sagt es ohnehin.
        if (this.everConnected || this.attempts < ATTEMPTS_BEFORE_COMPLAINING) {
          return
        }

        this.callbacks.onError(`Bildverbindung zu ${this.device.name} kommt nicht zustande.`)
      },
    })
  }

  private handleText(raw: string): void {
    try {
      const message = JSON.parse(raw) as Record<string, unknown>

      switch (message['t']) {
        case 'meta':
          this.applyMeta(message as unknown as ScreenMeta)
          break

        case 'stats':
          this.callbacks.onStats(message as unknown as ScreenStats)
          break

        case 'unavailable':
          this.callbacks.onAvailability(false, message['message'] as string | undefined)
          break

        case 'available':
          this.callbacks.onAvailability(true)
          break

        case 'error':
          this.callbacks.onError(String(message['message'] ?? 'Unbekannter Fehler.'))
          break
      }
    } catch {
      // Unverständliche Antwort ignorieren statt die Verbindung zu opfern.
    }
  }

  private applyMeta(meta: ScreenMeta): void {
    const canvas = this.canvas

    if (canvas !== undefined) {
      // Canvas immer in echter Monitorauflösung — die Ausschnitte kommen in
      // Monitorkoordinaten, und so bleibt die Umrechnung für Klicks trivial.
      canvas.width = meta.width
      canvas.height = meta.height
    }

    this.callbacks.onMeta(meta)
  }

  private handleFrame(data: ArrayBuffer): void {
    if (data.byteLength <= HEADER_BYTES || this.context === undefined) {
      return
    }

    if (this.pendingRegions >= MAX_PENDING_REGIONS) {
      // Rückstand wegwerfen und sauber neu anfangen.
      this.pendingRegions = 0
      this.drawQueue = Promise.resolve()
      this.refresh()
      return
    }

    const header = new DataView(data, 0, HEADER_BYTES)
    const x = header.getUint16(0, true)
    const y = header.getUint16(2, true)
    const width = header.getUint16(4, true)
    const height = header.getUint16(6, true)

    const blob = new Blob([data.slice(HEADER_BYTES)], { type: 'image/jpeg' })

    this.pendingRegions++

    // Die Dekodierung läuft parallel, das Zeichnen muss aber der Reihe nach
    // passieren: sonst überschreibt ein älterer Ausschnitt einen neueren.
    this.drawQueue = this.drawQueue
      .then(async () => {
        const bitmap = await createImageBitmap(blob)

        try {
          this.context?.drawImage(bitmap, x, y, width, height)
        } finally {
          bitmap.close()
        }
      })
      .catch(() => {
        // Ein kaputtes Einzelbild ist kein Grund, den Stream aufzugeben.
      })
      .finally(() => {
        this.pendingRegions--
      })
  }

  private readonly reconnectNow = (): void => {
    if (this.closedByUs) {
      return
    }

    this.reconnectDelay = RECONNECT_BASE_MS
    this.channel?.close()
    this.open()
  }

  /**
   * Der Agent schickt jede Sekunde Statistik — bleibt die aus, ist die
   * Verbindung tot, auch wenn der Browser sie noch für offen hält. Das passiert
   * regelmäßig beim Wechsel zwischen WLAN und Mobilfunk.
   */
  private watchForStall(): void {
    if (this.stallTimer !== undefined) {
      clearInterval(this.stallTimer)
    }

    this.lastMessageAt = Date.now()

    this.stallTimer = window.setInterval(() => {
      if (this.closedByUs || this.channel?.isOpen !== true) {
        return
      }

      if (Date.now() - this.lastMessageAt > STALL_TIMEOUT_MS) {
        this.reconnectNow()
      }
    }, STALL_CHECK_MS)
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer !== undefined) {
      return
    }

    this.attempts += 1

    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = undefined

      if (!this.closedByUs) {
        this.open()
      }
    }, this.reconnectDelay)

    this.reconnectDelay = Math.min(this.reconnectDelay * 2, RECONNECT_MAX_MS)
  }

  private send(payload: Record<string, unknown>): void {
    this.channel?.send(JSON.stringify(payload))
  }

  private setState(state: ConnectionState): void {
    if (this.state === state) {
      return
    }

    this.state = state
    this.callbacks.onState(state)
  }
}
