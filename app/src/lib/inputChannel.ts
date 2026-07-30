import type { ConnectionState, Device, MouseButton } from './types.ts'

/** Wartezeit vor dem nächsten Verbindungsversuch, wächst bis zum Maximum. */
const RECONNECT_BASE_MS = 500
const RECONNECT_MAX_MS = 8000

/**
 * Der Eingabe-WebSocket zum Agent.
 *
 * Bewegungs-Events werden bis zum nächsten Frame gesammelt: ein Finger auf dem
 * Display erzeugt bis zu 240 Events pro Sekunde, von denen nur die jeweils
 * letzte Position zählt. Ungedrosselt würde der Socket volllaufen und die
 * Eingabe sichtbar nachhängen.
 */
export class InputChannel {
  private socket: WebSocket | undefined
  private reconnectTimer: number | undefined
  private reconnectDelay = RECONNECT_BASE_MS
  private closedByUs = false

  private pendingMove: Record<string, unknown> | undefined
  private frameHandle: number | undefined

  private state: ConnectionState = 'disconnected'

  constructor(
    private readonly device: Device,
    private readonly onStateChange: (state: ConnectionState) => void,
    private readonly onError: (message: string) => void,
  ) {}

  connect(): void {
    this.closedByUs = false
    window.addEventListener('online', this.reconnectNow)
    this.open()
  }

  /**
   * Beim Wechsel WLAN↔Mobilfunk bleibt der alte Socket oft scheinbar offen
   * stehen: die Gegenstelle ist über die alte Route nicht mehr erreichbar, aber
   * der Browser merkt das erst nach dem TCP-Timeout. Deshalb wird hier
   * kompromisslos neu verbunden, statt auf den alten Socket zu hoffen.
   */
  private readonly reconnectNow = (): void => {
    if (this.closedByUs) {
      return
    }

    this.reconnectDelay = RECONNECT_BASE_MS
    this.socket?.close()
    this.open()
  }

  /**
   * Trennt die Verbindung. Der Agent löst dabei serverseitig alle gehaltenen
   * Tasten — deshalb ist ein sauberes Schließen wichtig.
   */
  disconnect(): void {
    this.closedByUs = true
    window.removeEventListener('online', this.reconnectNow)

    if (this.reconnectTimer !== undefined) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = undefined
    }

    this.cancelPendingMove()
    this.socket?.close()
    this.socket = undefined
    this.setState('disconnected')
  }

  // ---- Befehle ------------------------------------------------------

  /** Absolute Position auf einem Monitor, jeweils 0..1. */
  moveTo(monitor: number, x: number, y: number): void {
    this.queueMove({ t: 'move', monitor, x, y })
  }

  /** Relative Bewegung vom Trackpad. */
  moveBy(dx: number, dy: number): void {
    // Relative Bewegungen dürfen nicht verworfen, nur zusammengefasst werden —
    // sonst geht ein Teil der Strecke verloren.
    const pending = this.pendingMove

    if (pending?.['t'] === 'moverel') {
      this.queueMove({
        t: 'moverel',
        dx: (pending['dx'] as number) + dx,
        dy: (pending['dy'] as number) + dy,
      })
      return
    }

    this.queueMove({ t: 'moverel', dx, dy })
  }

  click(button: MouseButton = 'left'): void {
    this.send({ t: 'click', button })
  }

  buttonDown(button: MouseButton = 'left'): void {
    this.send({ t: 'down', button })
  }

  buttonUp(button: MouseButton = 'left'): void {
    this.send({ t: 'up', button })
  }

  scroll(vertical: number, horizontal = 0): void {
    this.send({ t: 'scroll', dy: vertical, dx: horizontal })
  }

  keyDown(key: string): void {
    this.send({ t: 'keydown', key })
  }

  keyUp(key: string): void {
    this.send({ t: 'keyup', key })
  }

  /** Tastenkombination, z.B. `combo('escape', ['ctrl', 'shift'])`. */
  combo(key: string, modifiers: string[] = []): void {
    this.send({ t: 'key', key, mods: modifiers })
  }

  /**
   * Frei zusammengestellte Kombination aus beliebig vielen Tasten.
   *
   * Alle der Reihe nach drücken und in umgekehrter Reihenfolge wieder lösen —
   * genau wie an einer echten Tastatur. Über {@link combo} ginge das nicht:
   * dort ist eine Taste die eigentliche und der Rest Modifier, hier darf jede
   * Taste an jeder Stelle stehen.
   */
  chord(keys: string[]): void {
    // Doppelte Tasten würden zweimal gedrückt, aber nur einmal gelöst — und
    // blieben damit auf dem Rechner hängen.
    const pressed = [...new Set(keys)]

    for (const key of pressed) {
      this.keyDown(key)
    }

    for (const key of [...pressed].reverse()) {
      this.keyUp(key)
    }
  }

  typeText(text: string): void {
    this.send({ t: 'text', text })
  }

  // ---- Interna ------------------------------------------------------

  private open(): void {
    this.setState('connecting')

    // Browser können bei WebSockets keine eigenen Header setzen — deshalb das
    // Token im Query-String. Die Verbindung ist TLS-verschlüsselt.
    const url =
      `wss://${this.device.host}:${this.device.port}/ws/input` +
      `?token=${encodeURIComponent(this.device.token)}`

    const socket = new WebSocket(url)
    this.socket = socket

    socket.addEventListener('open', () => {
      this.reconnectDelay = RECONNECT_BASE_MS
      this.setState('connected')
    })

    socket.addEventListener('message', (event) => {
      this.handleMessage(event.data as string)
    })

    socket.addEventListener('close', () => {
      this.setState('disconnected')

      if (!this.closedByUs) {
        this.scheduleReconnect()
      }
    })

    socket.addEventListener('error', () => {
      // Details liefert der Browser aus Sicherheitsgründen nicht. Die
      // häufigste Ursache ist ein abgelaufenes Tailscale-Zertifikat.
      this.onError(`Eingabe-Verbindung zu ${this.device.name} gestört.`)
    })
  }

  private handleMessage(raw: string): void {
    try {
      const message = JSON.parse(raw) as { t?: string; message?: string }

      if (message.t === 'error' && typeof message.message === 'string') {
        this.onError(message.message)
      }
    } catch {
      // Unverständliche Antwort ignorieren statt die Verbindung zu opfern.
    }
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer !== undefined) {
      return
    }

    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = undefined

      if (!this.closedByUs) {
        this.open()
      }
    }, this.reconnectDelay)

    // Verdoppeln bis zum Maximum: nach einem Netzwechsel WLAN→Mobilfunk soll
    // die App schnell zurückkommen, ohne bei totem Rechner zu hämmern.
    this.reconnectDelay = Math.min(this.reconnectDelay * 2, RECONNECT_MAX_MS)
  }

  private queueMove(payload: Record<string, unknown>): void {
    this.pendingMove = payload

    if (this.frameHandle !== undefined) {
      return
    }

    this.frameHandle = requestAnimationFrame(() => {
      this.frameHandle = undefined
      this.flushPendingMove()
    })
  }

  private cancelPendingMove(): void {
    if (this.frameHandle !== undefined) {
      cancelAnimationFrame(this.frameHandle)
      this.frameHandle = undefined
    }

    this.pendingMove = undefined
  }

  /**
   * Schickt eine noch wartende Bewegung sofort los.
   *
   * Ohne das käme ein Klick vor der Bewegung an, die ihn positioniert — beim
   * Tippen auf das Bildschirmbild landet der Klick dann dort, wo der Zeiger
   * vorher stand.
   */
  private flushPendingMove(): void {
    const pending = this.pendingMove

    if (pending === undefined) {
      return
    }

    this.cancelPendingMove()
    this.send(pending)
  }

  private send(payload: Record<string, unknown>): void {
    this.flushPendingMove()

    if (this.socket?.readyState !== WebSocket.OPEN) {
      // Eingaben nicht puffern: ein Klick, der zehn Sekunden später verspätet
      // ankommt, trifft auf einen völlig anderen Bildschirminhalt.
      return
    }

    this.socket.send(JSON.stringify(payload))
  }

  private setState(state: ConnectionState): void {
    if (this.state === state) {
      return
    }

    this.state = state
    this.onStateChange(state)
  }
}
