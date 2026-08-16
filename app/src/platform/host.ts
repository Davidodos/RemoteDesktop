/**
 * Dieses Gerät steuerbar machen.
 *
 * Bis V4 war die App ausschließlich Fernbedienung. Seit Phase 28 kann ein
 * Handy auch die Gegenseite sein: es spricht dasselbe Protokoll wie der
 * Windows-Agent, auf demselben Port, mit derselben Kopplung. Was dahintersteckt
 * — ein Server, ein Vordergrunddienst, ein selbst ausgestelltes Zertifikat —
 * bleibt hinter dieser Schnittstelle.
 *
 * Im Browser und im Windows-Fenster gibt es das nicht: dort ist bereits ein
 * Agent zuständig, oder es fehlt schlicht die Möglichkeit. Deshalb `available`
 * — die Freigabeseite fragt zuerst und sagt sonst, warum es hier nichts zu
 * schalten gibt.
 */
export interface HostService {
  readonly available: boolean

  /**
   * Ob sich die Freigabe hier überhaupt schalten lässt.
   *
   * <p>
   * Am Handy ja: der Server lebt mit der App, und die Einstellung entscheidet
   * darüber. Am Rechner nein — dort heißt „dieses Gerät ist freigegeben"
   * schlicht „der Agent läuft", und das ist eine Auskunft. Wer ihn starten oder
   * beenden will, tut das im Fenster unter „Einstellungen"; ein Schalter in der
   * App meinte etwas, das ihr nicht gehört.
   * </p>
   */
  readonly toggleable: boolean

  /** Wie es gerade steht. Ohne Nebenwirkung — auch, wenn nichts läuft. */
  status(): Promise<HostStatus>

  start(): Promise<HostStatus>
  stop(): Promise<HostStatus>

  /**
   * Ein frischer Kopplungscode, fünf Minuten gültig, einmal verwendbar.
   * Schlägt fehl, solange der Host nicht läuft — einen Code anzuzeigen, den
   * niemand einlösen kann, wäre eine Einladung ins Leere.
   */
  pairingCode(): Promise<HostPairingCode>

  /**
   * Fragt die Bildschirmaufnahme an. Android zeigt dabei seinen eigenen
   * Dialog — die App kann ihn weder umgehen noch vorwegnehmen.
   *
   * Die Erlaubnis hält, solange der Dienst lebt, und ist nach einem Neustart
   * des Geräts weg. Das steht so auf der Seite: wer sein Handy weglegt, soll
   * nicht glauben, es bleibe für immer einsehbar.
   */
  enableScreen(): Promise<HostStatus>

  /** Beendet die Aufnahme. Der Host bleibt erreichbar, nur ohne Bild. */
  disableScreen(): Promise<HostStatus>

  /**
   * Öffnet die Systemeinstellungen, in denen die Fernsteuerung freigeschaltet
   * wird. Mehr kann die App nicht tun — einschalten muss es ein Mensch, und
   * das ist bei einem Recht dieser Größe richtig so.
   */
  openInputSettings(): Promise<void>

  /**
   * Die offenen Rückfragen „darf dieses Gerät jetzt verbinden?".
   *
   * <p>
   * **Warum jede Verbindung einzeln bestätigt wird.** Eine Kopplung ist eine
   * Erlaubnis auf Dauer; sie sagt, *wer* fragen darf. Sie sagt nicht, dass
   * jetzt gerade jemand zusehen darf. Ein Handy ist kein Rechner auf dem
   * Schreibtisch — wer es fernsteuern will, hat es ohnehin in der Hand.
   * </p>
   *
   * <p>
   * Ein Zuhörer und keine Abfrage: die Frage entsteht im Augenblick einer
   * eingehenden Anmeldung, und die Gegenseite wartet darauf. Der Rückgabewert
   * meldet den Zuhörer wieder ab.
   * </p>
   */
  onRequests(listener: (requests: ConnectionRequest[]) => void): () => void

  /**
   * Die Antwort. Kommt keine, läuft die Frage nach etwa dreißig Sekunden in
   * ihr Zeitlimit — und ein Zeitablauf ist ein Nein.
   */
  answer(id: string, allow: boolean): Promise<void>

  /** Wer dieses Gerät steuern darf. */
  clients(): Promise<HostClient[]>

  /**
   * Nimmt einem Gerät das Recht — sofort und rückwirkend auf alles, was schon
   * steht.
   */
  revoke(id: string): Promise<void>
}

export interface HostStatus {
  running: boolean
  /** Wie dieses Gerät sich nennt; steht in `/api/info` als `hostname`. */
  deviceName: string
  port: number
  /**
   * Alle Adressen, unter denen es gerade erreichbar ist. Mehrere heißt: WLAN
   * und VPN nebeneinander. Die erste steht im QR-Code.
   */
  addresses: string[]
  /** Fingerabdruck der eigenen Zertifizierungsstelle. */
  caFingerprint?: string
  /**
   * Ob die Bildschirmaufnahme bestätigt ist. Ohne sie ist das Gerät zwar
   * erreichbar und steuerbar, aber nicht zu sehen.
   */
  sharingScreen?: boolean
  /**
   * Ob die Bedienungshilfe läuft. Ohne sie ist das Gerät zu sehen, aber nicht
   * zu bedienen — und das ist der Zustand, den man aus der Ferne nicht von
   * einem hängenden Gerät unterscheiden kann.
   */
  acceptingInput?: boolean
}

export interface HostPairingCode {
  code: string
  expiresInSeconds: number
  /** Inhalt des QR-Codes — `undefined`, solange keine Adresse feststeht. */
  pairingUri?: string
}

/** Eine Verbindung, die gerade um Zustimmung bittet. */
export interface ConnectionRequest {
  id: string
  /** Wie das anfragende Gerät in „wer darf" heißt. */
  label: string
}

export interface HostClient {
  id: string
  label: string
  scopes: string[]
  /** Wann dieses Gerät zuletzt eine Sitzung geöffnet hat, in Millisekunden. */
  lastSeenAt: number
}

/** Für Umgebungen, die kein Ziel sein können. */
export const noHost: HostService = {
  available: false,
  toggleable: false,
  status: (): Promise<HostStatus> =>
    Promise.resolve({ running: false, deviceName: '', port: 0, addresses: [] }),
  start: () => unavailable(),
  stop: () => unavailable(),
  pairingCode: () => unavailable(),
  enableScreen: () => unavailable(),
  disableScreen: () => unavailable(),
  openInputSettings: () => unavailable(),
  onRequests: (): (() => void) => () => undefined,
  answer: () => unavailable(),
  clients: (): Promise<HostClient[]> => Promise.resolve([]),
  revoke: () => unavailable(),
}

function unavailable(): Promise<never> {
  return Promise.reject(
    new Error('Dieses Gerät lässt sich hier nicht steuerbar machen — dafür braucht es die App.'),
  )
}
