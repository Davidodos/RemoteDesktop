import { AgentClient } from './agentClient.ts'
import { signChallenge } from './clientKey.ts'
import { ensureClientKey } from './pairing.ts'
import { postJson } from '../transport/direct.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from './types.ts'

/**
 * Der Verbindungstest — **in beide Richtungen**.
 *
 * <p>
 * **Warum beide:** eine Kopplung besteht aus zwei Einträgen, einem hier und
 * einem drüben, und sie können einzeln fehlen. „Antwortet nicht" verschweigt,
 * welcher es war — ob es am Netz liegt, am Vertrauen oder an einer Freigabe,
 * die niemand erteilt hat. Genau diese Auskunft fehlte, und ohne sie sucht man
 * an der falschen Stelle.
 * </p>
 *
 * <p>
 * **Hin** heißt: dieses Gerät erreicht die Gegenseite, sie kennt es, und mit
 * diesen Rechten. **Her** heißt: die Gegenseite steht in der eigenen Liste der
 * zugelassenen Geräte, und mit jenen Rechten. Für „hin" wird ausdrücklich eine
 * Anmeldung durchgeführt und nicht nur eine Anfrage gestellt: nur die Anmeldung
 * nennt die Rechte, und nur sie prüft den Ausweis wirklich.
 * </p>
 */
export interface ConnectionReport {
  /** Ob die Gegenseite antwortet — auf der TLS- und der Protokollebene. */
  reachable: boolean
  /** Wie sie heißt, wenn sie antwortet. */
  hostname?: string
  /** Was sie kann. Leer heißt: sie sagt es nicht (älter als V4). */
  capabilities: string[]
  /** Welche Rechte dieses Gerät dort hat — `undefined`, wenn die Anmeldung scheiterte. */
  scopesThere?: string[]
  /** Was in der Gegenrichtung los ist — siehe {@link Reverse}. */
  reverse: Reverse
  /** Warum es nicht ging, in einem Satz. */
  failure?: string
}

/**
 * Die Gegenrichtung: darf die Gegenseite **dieses** Gerät steuern?
 *
 * <p>
 * **Vier Lagen und nicht eine.** Vorher stand für drei davon derselbe Satz da —
 * „Zurück steht nichts bereit — neu koppeln." —, und das war in zwei von drei
 * Fällen schlicht falsch. Es sah gleich aus, ob die Gegenseite wirklich nicht
 * eingetragen ist, ob nur die Kennung fehlt, unter der sie einzutragen wäre,
 * oder ob sich die eigene Liste gerade nicht lesen ließ. Wer daraufhin neu
 * koppelt, repariert im zweiten Fall etwas, das nicht kaputt war, und im
 * dritten gar nichts.
 * </p>
 */
export type Reverse =
  /**
   * Die Kennung der Gegenseite steht nicht im Gerät. Dann lässt sich hier
   * nichts nachsehen — die Gegenrichtung selbst kann trotzdem stehen. Das ist
   * der Fall bei einer Kopplung von vor dem Steckbrief-Austausch und bei einer
   * Gegenstelle, die ihren Ausweis nicht mitgeschickt hat.
   */
  | { kind: 'unknown' }
  /** Die eigene Liste ließ sich nicht lesen. Eine Störung, kein Befund. */
  | { kind: 'unreadable'; failure: string }
  /** Nachgesehen: die Gegenseite steht hier nicht. Das ist der Fall für „neu koppeln". */
  | { kind: 'missing' }
  /** Sie steht hier — mit diesen Rechten. Leer heißt: eingetragen und darf nichts. */
  | { kind: 'granted'; scopes: string[] }

export async function testConnection(device: Device): Promise<ConnectionReport> {
  const [there, reverse] = await Promise.all([outbound(device), inbound(device)])

  return { ...there, reverse }
}

/**
 * Ob dieses Gerät die Gegenseite erreicht — und was es dort darf.
 */
async function outbound(
  device: Device,
): Promise<Omit<ConnectionReport, 'reverse'>> {
  let capabilities: string[] = []
  let hostname: string | undefined

  try {
    const info = await new AgentClient(device).getInfo()

    hostname = info.hostname
    capabilities = info.capabilities ?? []
  } catch (failure) {
    return {
      reachable: false,
      capabilities: [],
      failure: failure instanceof Error ? failure.message : String(failure),
    }
  }

  return {
    reachable: true,
    ...(hostname === undefined ? {} : { hostname }),
    capabilities,
    ...(await scopesThere(device)),
  }
}

/**
 * Die Anmeldung, ausdrücklich am Transport vorbei.
 *
 * <p>
 * Der Transport merkt sich ein Token und wirft weg, was bei der Anmeldung sonst
 * noch gesagt wurde — unter anderem die Rechte. Genau die sind hier die Frage.
 * Ein zweiter Anmeldeweg ist das nicht: es sind dieselben zwei Aufrufe, nur
 * ohne den Speicher davor.
 * </p>
 */
async function scopesThere(device: Device): Promise<{ scopesThere?: string[] }> {
  if (device.clientId === undefined) {
    // Ein Gerät mit Sammel-Token kennt keine Rechte je Client. Dann gibt es
    // hier nichts zu berichten, und das ist keine Störung.
    return {}
  }

  const base = `https://${device.host}:${device.port}`

  try {
    const { nonce } = await postJson<{ nonce: string }>(`${base}/api/session/challenge`, {
      clientId: device.clientId,
    })

    const key = await ensureClientKey()

    const { scopes } = await postJson<{ scopes?: string[] }>(`${base}/api/session`, {
      clientId: device.clientId,
      nonce,
      signature: await signChallenge(key.privateKey, nonce),
    })

    return { scopesThere: scopes ?? [] }
  } catch {
    // Erreichbar, aber nicht angemeldet: die Gegenseite kennt dieses Gerät
    // nicht mehr. Das ist ein Ergebnis und kein Fehlschlag des Tests.
    return {}
  }
}

/**
 * Ob die Gegenseite hier steht — und mit welchen Rechten.
 *
 * Vier Ausgänge, und jeder sagt etwas anderes. Siehe {@link Reverse}.
 */
async function inbound(device: Device): Promise<Reverse> {
  if (device.peerClientId === undefined) {
    return { kind: 'unknown' }
  }

  let clients

  try {
    clients = await getPlatform().host.clients()
  } catch (failure) {
    return {
      kind: 'unreadable',
      failure: failure instanceof Error ? failure.message : String(failure),
    }
  }

  const entry = clients.find((client) => client.id === device.peerClientId)

  return entry === undefined ? { kind: 'missing' } : { kind: 'granted', scopes: entry.scopes }
}

/**
 * Aus dem Bericht wird ein Satz, der weiterhilft.
 *
 * „Antwortet nicht" verschweigt, ob es am Netz, am Vertrauen oder an einer
 * fehlenden Freigabe liegt — und genau danach sucht man dann an der falschen
 * Stelle. Deshalb steht hier zu jeder Richtung genau eine Aussage, und zu jeder
 * Aussage der nächste Schritt.
 */
export function describeReport(device: Device, report: ConnectionReport): string {
  const name = report.hostname ?? device.name

  const hin = !report.reachable
    ? `Nicht erreichbar: ${report.failure ?? 'kein Grund genannt'}`
    : report.scopesThere === undefined
      ? `${name} antwortet, kennt dieses Gerät aber nicht mehr. Neu koppeln.`
      : `${name}: ${report.scopesThere.join(', ') || 'nichts erlaubt'}.`

  return `${hin} ${describeReverse(name, report.reverse)}`
}

function describeReverse(name: string, reverse: Reverse): string {
  switch (reverse.kind) {
    case 'granted':
      return `Zurück: ${reverse.scopes.join(', ') || 'nichts erlaubt'}.`

    case 'missing':
      return `Zurück: ${name} steht hier nicht in der Liste — neu koppeln.`

    case 'unreadable':
      return `Zurück: nicht nachsehbar (${reverse.failure}).`

    // Kein „neu koppeln": hier ist nichts kaputt, hier fehlt nur die Kennung,
    // unter der nachzusehen wäre. Wer diese Richtung wirklich prüfen will,
    // koppelt neu — aber wenn sie funktioniert, funktioniert sie.
    case 'unknown':
      return (
        `Zurück: nicht nachsehbar — dieses Gerät hat sich beim Koppeln nicht gemerkt, `
        + `unter welcher Kennung ${name} hier steht. Ob es geht, sagt ein Versuch von dort aus.`
      )
  }
}
