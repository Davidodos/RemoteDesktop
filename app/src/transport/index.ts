/**
 * Wie die App den Agent erreicht.
 *
 * Heute gibt es genau einen Weg — HTTPS und WSS direkt an `host:8443`
 * (`direct.ts`). Später kommt der Weg über WebRTC-Data-Channels dazu, bei dem
 * es keine Adressen und keine WebSockets mehr gibt. Damit das kein Umbau der
 * halben App wird, wissen `agentClient.ts`, `inputChannel.ts` und
 * `screenChannel.ts` schon jetzt nicht mehr, worüber sie eigentlich reden.
 */

/** Eine Anfrage mit Antwort — heute ein REST-Aufruf. */
export interface ControlRequest {
  path: string
  method?: 'GET' | 'POST' | 'DELETE'
  body?: unknown
}

/**
 * Ein laufender Kanal zum Agent.
 *
 * Bewusst schmal gehalten: senden, schließen, und die Auskunft, ob gerade
 * überhaupt etwas ankommt. Alles Weitere — Wiederverbinden, Zusammenfassen von
 * Bewegungen — bleibt eine Stufe darüber, weil es nichts mit dem Transport zu
 * tun hat.
 */
export interface Channel {
  /** Verwirft die Nachricht, solange der Kanal nicht offen ist. */
  send(payload: string): void
  close(): void
  readonly isOpen: boolean
}

/**
 * Alle Rückrufe sind freiwillig: der Eingabekanal bekommt nie Binärdaten, der
 * Bildkanal interessiert sich nicht für jede Textnachricht.
 */
export interface ChannelHandlers {
  onOpen?: () => void
  onClose?: () => void
  /** Details liefert der Browser aus Sicherheitsgründen nicht. */
  onError?: () => void
  onText?: (data: string) => void
  onBinary?: (data: ArrayBuffer) => void
}

export interface Transport {
  /** Einzelne Anfrage an den Agent; wirft {@link TransportError}. */
  control<T>(request: ControlRequest): Promise<T>

  /**
   * Adresse für Inhalte, die der Browser selbst lädt — ein `<img>` kann keine
   * eigenen Header mitschicken, deshalb muss die Berechtigung mit in die URL.
   */
  resourceUrl(path: string, query?: Record<string, string>): string

  /** Eingaben: geordnet und zuverlässig, sonst überholt ein Klick seine Bewegung. */
  inputChannel(handlers: ChannelHandlers): Channel

  /** Der Bildstrom des JPEG-Wegs. */
  screenStream(monitor: number, handlers: ChannelHandlers): Channel
}

/**
 * Etwas ist auf dem Weg zum Agent schiefgegangen.
 *
 * Die Klasse bleibt bewusst wortkarg: die verständliche Meldung baut der
 * Aufrufer, weil nur er den Gerätenamen und den Zusammenhang kennt.
 * `status === undefined` heißt, dass die Verbindung gar nicht zustande kam.
 */
export class TransportError extends Error {
  readonly status: number | undefined
  /** Die Klartextmeldung des Agents, falls er eine mitgeschickt hat. */
  readonly serverMessage: string | undefined

  constructor(
    message: string,
    options: { cause?: unknown; status?: number; serverMessage?: string } = {},
  ) {
    super(message, { cause: options.cause })
    this.name = 'TransportError'
    this.status = options.status
    this.serverMessage = options.serverMessage
  }
}
