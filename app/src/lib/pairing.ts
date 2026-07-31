import { postJson } from '../transport/direct.ts'
import { TransportError } from '../transport/index.ts'
import { createClientKey, type ClientKeyPair } from './clientKey.ts'
import { storage } from './storage.ts'
import type { Device } from './types.ts'

/**
 * Die Kopplung: einmal pro Rechner den angezeigten Code eintippen, danach nie
 * wieder ein Geheimnis abtippen.
 *
 * Der Agent bekommt dabei nur den öffentlichen Schlüssel dieses Geräts. Wer
 * später eines der beiden Geräte übernimmt, hat damit noch keinen Zugang zum
 * anderen — das ist der ganze Grund, warum es kein geteiltes Token mehr gibt.
 */

/** Was der Agent nach erfolgreicher Kopplung zurückmeldet. */
interface PairResponse {
  clientId: string
  scopes: string[]
  hostname: string
  agentFingerprint: string
}

export interface PairTarget {
  /** MagicDNS-Name des Rechners. */
  host: string
  port: number
  code: string
  /** Wie dieses Gerät in der Liste am Rechner heißen soll. */
  label: string
}

/**
 * Das eigene Schlüsselpaar, beim ersten Bedarf erzeugt.
 *
 * Ein Paar für alle Rechner: die Identität dieses Handys ist überall dieselbe,
 * freigeschaltet wird sie bei jedem Agent einzeln.
 */
export async function ensureClientKey(): Promise<ClientKeyPair> {
  const existing = parseClientKey(storage.getClientKey())

  if (existing !== undefined) {
    return existing
  }

  const created = await createClientKey()
  storage.setClientKey(JSON.stringify(created))

  return created
}

/**
 * Koppelt mit einem Agent und liefert das fertige Gerät.
 *
 * Gespeichert wird hier noch nichts — das entscheidet der Aufrufer, der auch
 * weiß, was schon in der Liste steht.
 */
export async function pairWithAgent(target: PairTarget): Promise<Device> {
  const key = await ensureClientKey()
  const base = `https://${target.host}:${target.port}`

  let response: PairResponse

  try {
    response = await postJson<PairResponse>(`${base}/api/pair`, {
      code: target.code.trim(),
      label: target.label.trim(),
      publicKey: key.publicKey,
    })
  } catch (cause) {
    throw new PairingError(describeFailure(cause, target.host), { cause })
  }

  return {
    // Der Fingerabdruck des Agent-Schlüssels als Kennung: er bleibt gleich,
    // auch wenn der Rechner umbenannt wird oder eine andere Adresse bekommt.
    id: response.agentFingerprint,
    name: response.hostname,
    host: target.host,
    port: target.port,
    clientId: response.clientId,
    fingerprint: response.agentFingerprint,
    // Wecken kann nur, wer die MAC kennt — das ist bis Phase 14 der Hub.
    canWake: false,
  }
}

export class PairingError extends Error {
  constructor(message: string, options: { cause?: unknown } = {}) {
    super(message, options)
    this.name = 'PairingError'
  }
}

/** Aus dem wortkargen Transportfehler wird hier ein Satz, der weiterhilft. */
function describeFailure(cause: unknown, host: string): string {
  if (!(cause instanceof TransportError)) {
    return cause instanceof Error ? cause.message : String(cause)
  }

  if (cause.status === undefined) {
    return `${host} antwortet nicht. Läuft der Rechner und ist der Agent gestartet?`
  }

  return cause.serverMessage ?? `${host} antwortete mit HTTP ${cause.status}.`
}

function parseClientKey(raw: string | undefined): ClientKeyPair | undefined {
  if (raw === undefined) {
    return undefined
  }

  try {
    const { publicKey, privateKey } = JSON.parse(raw) as Record<string, unknown>

    // Ein halb geschriebener Eintrag wäre schlimmer als keiner: die Kopplung
    // liefe durch und die Anmeldung scheiterte danach bei jedem Versuch.
    if (typeof publicKey !== 'string' || typeof privateKey !== 'string') {
      return undefined
    }

    return publicKey.length > 0 && privateKey.length > 0 ? { publicKey, privateKey } : undefined
  } catch {
    return undefined
  }
}
