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
}

export interface HostPairingCode {
  code: string
  expiresInSeconds: number
  /** Inhalt des QR-Codes — `undefined`, solange keine Adresse feststeht. */
  pairingUri?: string
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
  status: (): Promise<HostStatus> =>
    Promise.resolve({ running: false, deviceName: '', port: 0, addresses: [] }),
  start: () => unavailable(),
  stop: () => unavailable(),
  pairingCode: () => unavailable(),
  clients: (): Promise<HostClient[]> => Promise.resolve([]),
  revoke: () => unavailable(),
}

function unavailable(): Promise<never> {
  return Promise.reject(
    new Error('Dieses Gerät lässt sich hier nicht steuerbar machen — dafür braucht es die App.'),
  )
}
