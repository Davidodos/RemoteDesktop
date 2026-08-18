import { AgentClient } from './agentClient.ts'
import { LEGACY_CAPABILITIES, type Capability } from './capabilities.ts'
import { signChallenge } from './clientKey.ts'
import { deviceLabel } from './deviceNames.ts'
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
  /** Ihre Fassung, wenn sie eine nennt. */
  version?: string
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
   * Die Kennung der Gegenseite steht nicht im Gerät, und auch unter ihrem Namen
   * war nichts zu finden. Dann lässt sich hier nichts nachsehen — die
   * Gegenrichtung selbst kann trotzdem stehen.
   */
  | { kind: 'unknown' }
  /** Die eigene Liste ließ sich nicht lesen. Eine Störung, kein Befund. */
  | { kind: 'unreadable'; failure: string }
  /** Nachgesehen: die Gegenseite steht hier nicht. Das ist der Fall für „neu koppeln". */
  | { kind: 'missing' }
  /** Sie steht hier — mit diesen Rechten. Leer heißt: eingetragen und darf nichts. */
  | { kind: 'granted'; scopes: string[] }

/**
 * Welches Recht zu welcher Fähigkeit gehört.
 *
 * <p>
 * **Der Grund, warum es diese Zuordnung gibt:** der Test zählte vorher alle
 * Rechte auf, die dieses Gerät drüben hat. Damit stand bei einem Handy neben
 * „Bild, Eingabe" nichts weiter — und ob das nun vollständig ist oder ob vier
 * Rechte fehlen, war daraus nicht zu lesen. Ein Handy hat aber keine Medien,
 * keine Energieverwaltung und keine Aktionen; sie dort als fehlend zu führen
 * wäre eine Mängelliste über Dinge, die es nie gab.
 * </p>
 *
 * <p>
 * {@link Capability.keys}, {@link Capability.h264} und {@link Capability.files}
 * stehen bewusst nicht darin: sie sagen etwas über das *Wie*, nicht über eine
 * Erlaubnis. Für sie gibt es kein Recht, das jemand vergessen haben könnte.
 * </p>
 */
const SCOPE_OF: Partial<Record<Capability, string>> = {
  screen: 'screen',
  input: 'input',
  media: 'media',
  power: 'power',
  actions: 'actions',
  wake: 'wake',
}

/** Was in der Oberfläche steht — die englischen Namen sind Protokoll. */
const SCOPE_NAMES: Record<string, string> = {
  screen: 'Bild',
  input: 'Eingabe',
  media: 'Medien',
  power: 'Energie',
  actions: 'Aktionen',
  wake: 'Wecken',
}

export function scopeName(scope: string): string {
  return SCOPE_NAMES[scope] ?? scope
}

/**
 * Welche Rechte ein Gerät mit diesen Fähigkeiten überhaupt vergeben kann.
 *
 * Meldet es keine Fähigkeiten, ist es älter als V4 — dann gilt die Liste von
 * damals, und die deckt sich mit der eines Windows-Agents.
 */
export function expectedScopes(capabilities: readonly string[]): string[] {
  const abilities: readonly string[] =
    capabilities.length === 0 ? LEGACY_CAPABILITIES : capabilities

  return abilities
    .map((capability) => SCOPE_OF[capability as Capability])
    .filter((scope): scope is string => scope !== undefined)
}

/** Welche der erwarteten Rechte fehlen. */
export function missingScopes(
  capabilities: readonly string[],
  granted: readonly string[],
): string[] {
  return expectedScopes(capabilities).filter((scope) => !granted.includes(scope))
}

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
  let version: string | undefined

  try {
    const info = await new AgentClient(device).getInfo()

    hostname = info.hostname
    version = info.version
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
    ...(version === undefined ? {} : { version }),
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
 * <p>
 * **Gesucht wird zweimal.** Zuerst über die Kennung aus der Kopplung, dann über
 * den Namen. Der zweite Weg ist kein Notbehelf, sondern der Grund, warum diese
 * Auskunft überhaupt etwas taugt: eine Kopplung von vor dem Steckbrief-Austausch
 * hat die Kennung nie mitbekommen, und der Test sagte daraufhin „nicht
 * nachsehbar" über eine Gegenrichtung, die tadellos eingetragen dastand. Der
 * Name ist der, den die Gegenseite beim Koppeln selbst angegeben hat; er steht
 * genau deshalb in der eigenen Liste.
 * </p>
 */
async function inbound(device: Device): Promise<Reverse> {
  let clients

  try {
    clients = await getPlatform().host.clients()
  } catch (failure) {
    return {
      kind: 'unreadable',
      failure: failure instanceof Error ? failure.message : String(failure),
    }
  }

  const entry =
    (device.peerClientId === undefined
      ? undefined
      : clients.find((client) => client.id === device.peerClientId)) ?? byName(clients, device)

  if (entry !== undefined) {
    return { kind: 'granted', scopes: entry.scopes }
  }

  // Ohne Kennung *und* ohne Namenstreffer lässt sich nichts behaupten: der
  // Eintrag könnte unter einem dritten Namen dastehen. Mit Kennung dagegen ist
  // nachgesehen und nichts gefunden — das ist ein Befund.
  return device.peerClientId === undefined ? { kind: 'unknown' } : { kind: 'missing' }
}

/**
 * Der Eintrag zum Namen dieses Geräts.
 *
 * Verglichen wird ohne Rücksicht auf Groß- und Kleinschreibung und Leerraum:
 * der Name kommt aus einem Feld, in das ein Mensch getippt hat.
 */
function byName(
  clients: readonly { id: string; label: string; scopes: string[] }[],
  device: Device,
): { id: string; label: string; scopes: string[] } | undefined {
  const wanted = new Set(
    [device.name, deviceLabel(device)].map((name) => name.trim().toLowerCase()),
  )

  return clients.find((client) => wanted.has(client.label.trim().toLowerCase()))
}

/**
 * Aus dem Bericht wird ein Satz, der weiterhilft.
 *
 * <p>
 * **Es steht nur noch da, was fehlt.** Vorher zählte der Test alle Rechte auf,
 * die dieses Gerät drüben hat — eine Liste, die man mit einer zweiten im Kopf
 * vergleichen musste, um zu wissen, ob sie vollständig ist. Bei einem Handy war
 * sie es immer, und trotzdem las sie sich wie ein Auszug. Jetzt steht dort
 * entweder „alle Rechte" oder genau das, was fehlt — und zwar nur solche
 * Rechte, die diese Art von Gerät überhaupt vergeben kann.
 * </p>
 */
export function describeReport(device: Device, report: ConnectionReport): string {
  const name = report.hostname ?? device.name

  const hin = !report.reachable
    ? `Nicht erreichbar: ${report.failure ?? 'kein Grund genannt'}`
    : report.scopesThere === undefined
      ? `${name} antwortet, kennt dieses Gerät aber nicht mehr. Neu koppeln.`
      : describeScopes(name, report)

  return `${hin} ${describeReverse(name, report.reverse)}`
}

function describeScopes(name: string, report: ConnectionReport): string {
  const fehlend = missingScopes(report.capabilities, report.scopesThere ?? [])

  if (fehlend.length === 0) {
    return `${name}: alle Rechte verfügbar.`
  }

  return `${name}: es fehlt ${fehlend.map(scopeName).join(', ')}.`
}

function describeReverse(name: string, reverse: Reverse): string {
  switch (reverse.kind) {
    case 'granted':
      return reverse.scopes.length === 0
        ? `Zurück: ${name} ist eingetragen, darf aber nichts.`
        : `Zurück: eingetragen (${reverse.scopes.map(scopeName).join(', ')}).`

    case 'missing':
      return `Zurück: ${name} steht hier nicht in der Liste — neu koppeln.`

    case 'unreadable':
      return `Zurück: nicht nachsehbar (${reverse.failure}).`

    // Kein „neu koppeln": hier ist nichts kaputt, hier fehlt nur die Kennung,
    // unter der nachzusehen wäre — und unter dem Namen stand ebenfalls nichts.
    // Wer diese Richtung wirklich prüfen will, koppelt neu; aber wenn sie
    // funktioniert, funktioniert sie.
    case 'unknown':
      return `Zurück: nicht nachsehbar — hier steht kein Eintrag auf den Namen ${name}.`
  }
}
