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
  /** Welche Rechte die Gegenseite hier hat — `undefined`, wenn sie hier nicht steht. */
  scopesHere?: string[]
  /** Warum es nicht ging, in einem Satz. */
  failure?: string
}

export async function testConnection(device: Device): Promise<ConnectionReport> {
  const [there, here] = await Promise.all([outbound(device), inbound(device)])

  return { ...there, ...(here === undefined ? {} : { scopesHere: here }) }
}

/**
 * Ob dieses Gerät die Gegenseite erreicht — und was es dort darf.
 */
async function outbound(
  device: Device,
): Promise<Omit<ConnectionReport, 'scopesHere'>> {
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
 * `undefined` heißt „steht nicht in der Liste"; ein leeres Array hieße „steht
 * drin und darf nichts", und das ist etwas anderes.
 */
async function inbound(device: Device): Promise<string[] | undefined> {
  if (device.peerClientId === undefined) {
    return undefined
  }

  try {
    const clients = await getPlatform().host.clients()

    return clients.find((client) => client.id === device.peerClientId)?.scopes
  } catch {
    return undefined
  }
}
