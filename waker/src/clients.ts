import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'

/**
 * Ein gekoppelter Client — dasselbe Format wie in der `clients.json` des
 * Agents, damit man beide Dateien nebeneinander lesen kann.
 *
 * Es steht **niemals ein Token darin**, nur der öffentliche Schlüssel. Wer die
 * Datei liest, kann sich damit nicht anmelden.
 */
export interface PairedClient {
  id: string
  label: string
  /** Base64 des öffentlichen Schlüssels im SPKI-Format (ECDSA P-256). */
  publicKey: string
  createdAt: string
  lastSeenAt: string
}

/**
 * Die Liste der Geräte, die diesen Waker benutzen dürfen.
 *
 * Der Waker vertraut ausschließlich dieser Datei. Sie ist die einzige
 * Konfiguration, die er überhaupt hat — und sie füllt sich beim Koppeln von
 * selbst, statt von Hand gepflegt zu werden.
 */
export class ClientStore {
  private clients: PairedClient[]

  constructor(private readonly path: string) {
    this.clients = read(path)
  }

  list(): PairedClient[] {
    return [...this.clients]
  }

  find(id: string): PairedClient | undefined {
    return this.clients.find((client) => client.id === id)
  }

  /**
   * Nimmt einen Client auf. Koppelt dasselbe Gerät erneut — etwa nach einer
   * Neuinstallation der App —, ersetzt der neue Eintrag den alten, statt die
   * Liste mit Karteileichen zu füllen.
   */
  add(client: PairedClient): void {
    this.clients = [...this.clients.filter((entry) => entry.id !== client.id), client]
    this.write()
  }

  /** `false`, wenn es den Client gar nicht gab. */
  revoke(id: string): boolean {
    const remaining = this.clients.filter((client) => client.id !== id)

    if (remaining.length === this.clients.length) {
      return false
    }

    this.clients = remaining
    this.write()
    return true
  }

  touch(id: string, seenAt: string): void {
    this.clients = this.clients.map((client) =>
      client.id === id ? { ...client, lastSeenAt: seenAt } : client,
    )

    this.write()
  }

  private write(): void {
    const json = JSON.stringify(this.clients, null, 2)

    // Erst daneben schreiben, dann umbenennen: ein Absturz mitten im Schreiben
    // würde sonst die Liste aller zugelassenen Geräte zerstören.
    mkdirSync(dirname(this.path), { recursive: true })
    writeFileSync(`${this.path}.tmp`, json, 'utf8')
    renameSync(`${this.path}.tmp`, this.path)
  }
}

/**
 * Die Kennung eines Clients kommt aus seinem Schlüssel: die ersten 16
 * Hex-Stellen des SHA-256 über den öffentlichen Schlüssel. Genau wie im Agent —
 * dasselbe Handy hat damit an jedem Knoten dieselbe Kennung.
 */
export function fingerprintOf(publicKeyBase64: string): string {
  return createHash('sha256')
    .update(Buffer.from(publicKeyBase64, 'base64'))
    .digest('hex')
    .slice(0, 16)
}

/**
 * Eine fehlende Datei ist der Normalfall beim ersten Start. Eine kaputte Datei
 * ist es nicht — dann fliegt der Fehler, statt still mit einer leeren Liste
 * weiterzulaufen und alle gekoppelten Geräte auszusperren.
 */
function read(path: string): PairedClient[] {
  let content: string

  try {
    content = readFileSync(path, 'utf8')
  } catch {
    return []
  }

  if (content.trim().length === 0) {
    return []
  }

  const parsed: unknown = JSON.parse(content)

  if (!Array.isArray(parsed)) {
    throw new Error(`${path} enthält keine Client-Liste.`)
  }

  return parsed as PairedClient[]
}
