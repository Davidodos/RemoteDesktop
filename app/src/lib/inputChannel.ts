import { directTransport } from '../transport/direct.ts'
import type { Channel, Transport } from '../transport/index.ts'
import type { ConnectionState, Device, MouseButton } from './types.ts'

/** Wartezeit vor dem nächsten Verbindungsversuch, wächst bis zum Maximum. */
const RECONNECT_BASE_MS = 500
const RECONNECT_MAX_MS = 8000

/**
 * So oft wird nachgefasst, bevor aufgegeben wird.
 *
 * Mit der Verdopplung oben deckt das gut eine Minute ab — genug für den
 * Neustart nach einem Selbst-Update, das ist der Fall, für den das Ganze da
 * ist. Endlos weiterzuversuchen wäre schlimmer als aufzugeben: die App stünde
 * dann für immer auf „verbinde…", während der Rechner in Wahrheit
 * heruntergefahren ist, und am Handy kostet das nebenbei den Akku.
 */
const MAX_RECONNECT_ATTEMPTS = 10

/**
 * So viele erfolglose Versuche, bevor eine Störung gemeldet wird.
 *
 * <p>
 * **Der Befund dahinter (18.08.2026):** ein WebSocket meldet `error`, sobald ein
 * Verbindungsversuch scheitert — und das tut der erste regelmäßig, ohne dass
 * irgendetwas kaputt wäre. Der Rechner startet den Agent gerade neu, das Handy
 * hat die Anfrage noch nicht bestätigt, das WLAN hat eine Sekunde gebraucht. Der
 * nächste Versuch kommt durch, und alles läuft. Trotzdem stand am Bildschirm der
 * denkbar ungünstigste Satz: das Sicherheitszertifikat sei abgelaufen, man möge
 * es im Fenster neu holen. Das schickte jeden, der ihn las, an eine Stelle, an
 * der nichts zu reparieren war — während die Verbindung nebenher tadellos stand.
 * </p>
 *
 * <p>
 * Drei Versuche decken mit der Verdopplung oben knapp zwei Sekunden ab. Wer die
 * überschreitet, hat es wirklich nicht mit einem Wackler zu tun.
 * </p>
 */
const ATTEMPTS_BEFORE_COMPLAINING = 3

/**
 * Der Eingabe-WebSocket zum Agent.
 *
 * Bewegungs-Events werden bis zum nächsten Frame gesammelt: ein Finger auf dem
 * Display erzeugt bis zu 240 Events pro Sekunde, von denen nur die jeweils
 * letzte Position zählt. Ungedrosselt würde der Socket volllaufen und die
 * Eingabe sichtbar nachhängen.
 */
export class InputChannel {
  private channel: Channel | undefined
  private reconnectTimer: number | undefined
  private reconnectDelay = RECONNECT_BASE_MS
  private closedByUs = false

  /** Wie oft seit der letzten stehenden Verbindung erfolglos versucht wurde. */
  private attempts = 0
  /** Ob der laufende Versuch je zustande kam. Siehe {@link open}. */
  private opened = false
  /**
   * Ob diese Verbindung überhaupt schon einmal stand.
   *
   * Trennt den Wackler vom Grundproblem: nach einer stehenden Verbindung ist ein
   * Abriss etwas, das wieder zusammenwächst — davor ist er womöglich das, was
   * eine Meldung wert wäre. Siehe {@link ATTEMPTS_BEFORE_COMPLAINING}.
   */
  private everConnected = false

  private pendingMove: Record<string, unknown> | undefined
  private frameHandle: number | undefined

  private state: ConnectionState = 'disconnected'

  constructor(
    private readonly device: Device,
    private readonly onStateChange: (state: ConnectionState) => void,
    private readonly onError: (message: string) => void,
    private readonly transport: Transport = directTransport(device),
  ) {}

  connect(): void {
    this.closedByUs = false
    this.attempts = 0
    this.everConnected = false
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
    this.attempts = 0
    this.channel?.close()
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
    this.channel?.close()
    this.channel = undefined
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

  /**
   * Zwei Finger auseinander oder zusammen — der Zoom eines Berührungsgeräts.
   *
   * <p>
   * Mittelpunkt in Anteilen von 0 bis 1, `scale` als Faktor: über 1 heißt
   * heranholen. Am Rechner gibt es dafür keine Geste, deshalb entsteht sie dort
   * aus gezogenem Rechtsklick — und deshalb geht dieser Befehl auch nur an
   * Geräte, die Berührungen erwarten (siehe `lib/capabilities.ts`,
   * `isTouchTarget`). Ein Windows-Agent lehnt ihn ab, und das ist richtig so:
   * er hätte nichts, was er damit tun könnte.
   * </p>
   */
  pinch(x: number, y: number, scale: number): void {
    this.send({ t: 'pinch', x, y, scale })
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
    this.opened = false

    this.channel = this.transport.inputChannel({
      onOpen: () => {
        this.reconnectDelay = RECONNECT_BASE_MS
        this.attempts = 0
        this.opened = true
        this.everConnected = true
        this.setState('connected')
      },

      onText: (data) => this.handleMessage(data),

      onClose: () => {
        this.setState('disconnected')

        if (this.closedByUs) {
          return
        }

        // Ein Kanal, der nie zustande kam, deutet auf einen Ausweis, den die
        // Gegenseite nicht mehr kennt: nach einem Neustart des Agents — und ein
        // Selbst-Update ist genau das — sind alle Sitzungen weg, weil sie nur
        // in seinem Arbeitsspeicher lagen. Ein WebSocket bekommt dafür keinen
        // Statuscode, er wird einfach geschlossen. Also melden wir uns beim
        // nächsten Versuch neu an, statt mit dem alten Token weiterzureden.
        if (!this.opened) {
          this.transport.reauthenticate?.()
        }

        this.scheduleReconnect()
      },

      onError: () => {
        // **Nicht beim ersten Mal.** Details liefert der Browser aus
        // Sicherheitsgründen nicht, und ein einzelner gescheiterter Versuch sagt
        // für sich genommen nichts: er kommt bei jedem Neustart des Agents vor,
        // bei jeder Verbindung, die drüben noch bestätigt wird, und nach jedem
        // Netzwechsel. Gemeldet wird erst, wenn es dabei bleibt — und nur, wenn
        // diese Verbindung noch nie stand. Stand sie schon einmal, kümmert sich
        // der Wiederaufbau darum, und dafür gibt es die Statusanzeige.
        if (this.everConnected || this.attempts < ATTEMPTS_BEFORE_COMPLAINING) {
          return
        }

        this.onError(
          `Die Verbindung zu ${this.device.name} kommt nicht zustande. Meist ist ` +
          'das Sicherheitszertifikat des Rechners abgelaufen — im Fenster dort ' +
          'unter „Einrichtung“ neu holen.',
        )
      },
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

    this.attempts += 1

    if (this.attempts > MAX_RECONNECT_ATTEMPTS) {
      // Aufgeben heißt hier nicht „für immer": ein Netzwechsel setzt den Zähler
      // zurück, und wer das Gerät erneut auswählt, fängt von vorn an.
      this.onError(
        `${this.device.name} meldet sich nicht mehr. ` +
          'Läuft der Rechner noch? Gerät erneut auswählen, um es noch einmal zu versuchen.',
      )

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

    // Eingaben nicht puffern: ein Klick, der zehn Sekunden später verspätet
    // ankommt, trifft auf einen völlig anderen Bildschirminhalt. Genau das tut
    // der Kanal, wenn er gerade nicht offen ist.
    this.channel?.send(JSON.stringify(payload))
  }

  private setState(state: ConnectionState): void {
    if (this.state === state) {
      return
    }

    this.state = state
    this.onStateChange(state)
  }
}
