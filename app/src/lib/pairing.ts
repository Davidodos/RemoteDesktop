import { postJson } from '../transport/direct.ts'
import { TransportError } from '../transport/index.ts'
import { certificateFingerprint } from './certificateTrust.ts'
import { createClientKey, type ClientKeyPair } from './clientKey.ts'
import type { BackPairing } from '../platform/index.ts'
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

/**
 * Was die Gegenseite nach erfolgreicher Kopplung zurückmeldet.
 *
 * Zwei Arten von Gegenstelle antworten hier: ein Agent nennt seinen Rechnernamen
 * und den Fingerabdruck seines Schlüssels, ein Waker nur seine Rolle und den
 * Standort. Deshalb ist außer `clientId` alles freiwillig.
 */
interface PairResponse {
  clientId: string
  scopes?: string[]
  hostname?: string
  agentFingerprint?: string
  /**
   * Fingerabdruck der Zertifizierungsstelle, mit der sich der Agent ausweist.
   *
   * Bei einem Agent mit Zertifikat von Tailscale kommt hier ausdrücklich
   * `null` — es gibt dann nichts zu bestätigen. Deshalb steht `| null` im Typ
   * und nicht nur das Fragezeichen: genau diese Unterscheidung ging schief,
   * siehe {@link fingerprintOf}.
   */
  caFingerprint?: string | null
  /** `waker` bei einem Knoten, der nur wecken kann. */
  role?: string
  siteId?: string
  canWake?: boolean
}

export interface PairTarget {
  /** MagicDNS-Name des Rechners. */
  host: string
  port: number
  code: string
  /** Wie dieses Gerät in der Liste am Rechner heißen soll. */
  label: string
  /**
   * Das eigene Angebot für die Gegenrichtung. Geht mit, wenn dieses Gerät
   * selbst steuerbar ist — siehe `platform/localNode.ts`. Die Gegenseite hebt
   * es auf, bis ihre Oberfläche danach fragt.
   */
  back?: BackPairing
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

  const waker = response.role === 'waker'

  // Geprüft wird der Wert und nicht seine Abwesenheit — siehe
  // `certificateFingerprint`. Ein `null` ist kein Fingerabdruck.
  const caFingerprint = certificateFingerprint(response.caFingerprint)

  return {
    // Der Fingerabdruck des Agent-Schlüssels als Kennung: er bleibt gleich,
    // auch wenn der Rechner umbenannt wird oder eine andere Adresse bekommt.
    // Ein Waker hat keinen — dort tut es der Name, unter dem er erreichbar ist.
    id: response.agentFingerprint ?? target.host,
    // Ein Waker nennt keinen Rechnernamen; sein MagicDNS-Name ist das, was der
    // Nutzer eingetippt hat und wiedererkennt.
    name: response.hostname ?? target.host,
    host: target.host,
    port: target.port,
    clientId: response.clientId,
    ...(response.agentFingerprint === undefined
      ? {}
      : { fingerprint: response.agentFingerprint }),
    ...(caFingerprint === undefined ? {} : { caFingerprint }),
    ...(waker ? { waker: true } : {}),
    ...(response.siteId === undefined ? {} : { siteId: response.siteId }),
    // Jeder Agent kann seit Phase 14 Nachbarn wecken, ein Waker kann sonst
    // nichts. Ältere Agents melden das Feld nicht — dann eben nicht.
    canWake: response.canWake ?? false,
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
    // Drei Ursachen sehen an dieser Stelle gleich aus: kein Rechner, kein Name,
    // kein Vertrauen. Die dritte ist die unangenehmste, weil der Rechner dabei
    // antwortet — deshalb steht sie mit dabei und nicht in einer Fußnote.
    return (
      `${host} antwortet nicht. Läuft der Agent auf diesem Rechner, und sind ` +
      'beide Geräte im selben Netz? Die Adresse muss genau so lauten wie im ' +
      'Fenster am Rechner. Läuft der Rechner ohne Tailscale, koppelt nur der ' +
      'QR-Code — er bringt das Zertifikat mit, das dieses Gerät vorher ' +
      'bestätigen muss.'
    )
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
