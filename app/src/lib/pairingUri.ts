/**
 * Was im QR-Code der Kopplung steht.
 *
 * Erzeugt wird er am Rechner, gelesen am Handy — zwei Programme, die getrennt
 * aktualisiert werden. Deshalb stehen Erzeugen und Lesen hier in einer Datei
 * und werden gemeinsam geprüft: eine stillschweigende Änderung auf einer Seite
 * fällt sonst erst am Gerät auf.
 *
 * Der Code selbst bleibt der aus Phase 10 — sechs Ziffern, fünf Minuten gültig,
 * einmal verwendbar. Der QR spart nur das Abtippen, er ersetzt kein Geheimnis.
 */

/** Vorgabe, die praktisch nie ein anderer ist (siehe agent/README.md). */
export const DEFAULT_AGENT_PORT = 8443

const SCHEME = 'remotedesktop:'
const ACTION = 'pair'
const CODE_PATTERN = /^\d{6}$/

export interface PairingTarget {
  /** MagicDNS-Name des Rechners. */
  host: string
  port: number
  code: string
}

export function buildPairingUri(target: PairingTarget): string {
  const query = new URLSearchParams({
    host: target.host,
    port: String(target.port),
    code: target.code,
  })

  return `${SCHEME}//${ACTION}?${query.toString()}`
}

/**
 * Liest einen gescannten Text. Wirft mit einem Satz, der am Handy angezeigt
 * werden kann — ein Scanner bekommt viel zu sehen, was nicht hierher gehört.
 */
export function parsePairingUri(scanned: string): PairingTarget {
  const url = toUrl(scanned.trim())

  if (url === undefined || url.protocol.toLowerCase() !== SCHEME) {
    throw new PairingUriError('Das ist kein QR-Code von RemoteDesktop.')
  }

  // Bei `remotedesktop://pair?…` landet `pair` im Hostteil, nicht im Pfad.
  if (url.hostname.toLowerCase() !== ACTION) {
    throw new PairingUriError('Dieser QR-Code ist nicht für die Kopplung gedacht.')
  }

  const host = url.searchParams.get('host')?.trim() ?? ''

  if (host.length === 0) {
    throw new PairingUriError('Im QR-Code fehlt der Rechnername.')
  }

  const code = url.searchParams.get('code')?.trim() ?? ''

  if (!CODE_PATTERN.test(code)) {
    throw new PairingUriError('Der Kopplungscode im QR-Code besteht nicht aus sechs Ziffern.')
  }

  return { host, port: readPort(url.searchParams.get('port')), code }
}

export class PairingUriError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'PairingUriError'
  }
}

function readPort(raw: string | null): number {
  if (raw === null || raw.trim().length === 0) {
    return DEFAULT_AGENT_PORT
  }

  const port = Number(raw)

  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new PairingUriError(`Der Port im QR-Code ergibt keinen Sinn: ${raw}`)
  }

  return port
}

function toUrl(scanned: string): URL | undefined {
  try {
    return new URL(scanned)
  } catch {
    // Ein WLAN-Code (`WIFI:S=…`) etwa ist gar keine Adresse.
    return undefined
  }
}
