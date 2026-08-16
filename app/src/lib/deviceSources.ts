import { storage } from './storage.ts'
import type { Device, DevicePlatform } from './types.ts'

/**
 * Woher die Geräteliste kommt.
 *
 * Bisher gab es genau eine Quelle: den Hub auf der NAS. Ab der Kopplung
 * (Phase 10) trägt das Handy seine Geräte selbst — der Hub ist dann eine
 * Quelle unter mehreren und irgendwann gar keine mehr. Beides nebeneinander
 * geht nur, wenn niemand mehr davon ausgeht, dass es *die* eine Liste gibt.
 */
export interface DeviceSource {
  /** Für Meldungen und zum Auseinanderhalten der Fehler. */
  readonly id: string
  list(): Promise<Device[]>
}

/** Was beim Einsammeln herauskam. */
export interface DeviceListResult {
  devices: Device[]
  /**
   * Fehler einzelner Quellen. Sie beenden das Einsammeln nicht: fällt der Hub
   * aus, bleiben die selbst gekoppelten Geräte trotzdem bedienbar.
   */
  failures: unknown[]
}

/** Die Geräte, die dieses Gerät selbst kennt. */
export function localDeviceSource(): DeviceSource {
  return {
    id: 'lokal',
    list: (): Promise<Device[]> => Promise.resolve(parseDevices(storage.getDevices())),
  }
}

/**
 * Fragt alle Quellen und legt die Antworten übereinander.
 *
 * Die erste Quelle gewinnt bei gleicher Kennung: ein selbst gekoppeltes Gerät
 * bringt eigene Zugangsdaten mit und soll nicht von einem alten Hub-Eintrag
 * überschrieben werden.
 */
export async function collectDevices(
  sources: readonly DeviceSource[],
): Promise<DeviceListResult> {
  const results = await Promise.all(
    sources.map(async (source) => {
      try {
        return { devices: await source.list(), failure: undefined }
      } catch (failure) {
        return { devices: [], failure }
      }
    }),
  )

  const devices: Device[] = []
  const seen = new Set<string>()

  for (const result of results) {
    for (const device of result.devices) {
      if (seen.has(device.id)) {
        continue
      }

      seen.add(device.id)
      devices.push(device)
    }
  }

  return {
    devices,
    failures: results.flatMap((result) => (result.failure === undefined ? [] : [result.failure])),
  }
}

/**
 * Liest die lokal hinterlegten Geräte.
 *
 * Jeder Eintrag wird geprüft, statt dem Speicher zu glauben: dort steht, was
 * eine frühere Fassung der App hinterlassen hat, und ein kaputter Eintrag darf
 * nicht die ganze Liste kosten.
 */
export function parseDevices(raw: string | undefined): Device[] {
  if (raw === undefined) {
    return []
  }

  try {
    const parsed: unknown = JSON.parse(raw)

    return Array.isArray(parsed) ? parsed.flatMap(toDevice) : []
  } catch {
    return []
  }
}

function toDevice(entry: unknown): Device[] {
  if (typeof entry !== 'object' || entry === null) {
    return []
  }

  const {
    id,
    name,
    alias,
    host,
    port,
    token,
    clientId,
    fingerprint,
    caFingerprint,
    canWake,
    mac,
    siteId,
    waker,
    platform,
    peerClientId,
    lastConnectedAt,
  } = entry as Record<string, unknown>

  if (typeof id !== 'string' || id.length === 0 || typeof host !== 'string' || host.length === 0) {
    return []
  }

  const paired = typeof clientId === 'string' && clientId.length > 0
  const shared = typeof token === 'string' && token.length > 0

  // Ohne einen der beiden Ausweise käme die App bis zur ersten Anfrage und
  // stünde dann vor einem 401, das wie ein Fehler des Agents aussieht.
  if (!paired && !shared) {
    return []
  }

  if (!Number.isInteger(port) || (port as number) <= 0) {
    return []
  }

  return [
    {
      id,
      name: typeof name === 'string' && name.length > 0 ? name : id,
      ...(typeof alias === 'string' && alias.trim().length > 0 ? { alias: alias.trim() } : {}),
      host,
      port: port as number,
      ...(shared ? { token: token as string } : {}),
      ...(paired ? { clientId: clientId as string } : {}),
      ...(typeof fingerprint === 'string' && fingerprint.length > 0 ? { fingerprint } : {}),
      // Bleibt erhalten, weil das Vertrauen zu einer Stelle nachgeholt werden
      // muss, wenn die Gegenstelle beim Eintragen noch nicht lief. Ohne ihn
      // gäbe es später nichts mehr, wogegen man vergleichen könnte — und das
      // Gerät bliebe für immer „nicht erreichbar".
      ...(typeof caFingerprint === 'string' && caFingerprint.length > 0
        ? { caFingerprint }
        : {}),
      canWake: canWake === true,
      // Standort und MAC merkt sich die App, solange der Rechner wach ist —
      // ohne sie kann ihn später niemand wecken (siehe `wake.ts`).
      ...(typeof mac === 'string' && mac.length > 0 ? { mac } : {}),
      ...(typeof siteId === 'string' && siteId.length > 0 ? { siteId } : {}),
      ...(waker === true ? { waker: true } : {}),
      // Ein unbekannter Wert zählt als keiner: dann steht in der Liste kein
      // Symbol, und das ist besser als ein falsches.
      ...(platform === 'windows' || platform === 'android' ? { platform } : {}),
      ...(typeof peerClientId === 'string' && peerClientId.length > 0
        ? { peerClientId }
        : {}),
      ...(typeof lastConnectedAt === 'number' && Number.isFinite(lastConnectedAt)
        ? { lastConnectedAt }
        : {}),
    },
  ]
}

/**
 * Nimmt ein frisch gekoppeltes Gerät in die lokale Liste auf.
 *
 * Ein zweiter Eintrag für denselben Rechner wird ersetzt, nicht angehängt: die
 * Kennung ist der Fingerabdruck des Agents, und nach einer erneuten Kopplung
 * gelten nur noch die neuen Zugangsdaten.
 */
export function saveLocalDevice(device: Device): Device[] {
  const existing = parseDevices(storage.getDevices())
  const previous = existing.find((entry) => entry.id === device.id)

  // Was diesem Gerät gehört und nicht der Kopplung, überlebt sie: der selbst
  // vergebene Name und der Zeitpunkt der letzten Verbindung. Wer denselben
  // Rechner erneut koppelt, soll beides nicht verlieren.
  const entry: Device = {
    ...(previous?.alias === undefined ? {} : { alias: previous.alias }),
    ...(previous?.lastConnectedAt === undefined
      ? {}
      : { lastConnectedAt: previous.lastConnectedAt }),
    ...device,
  }

  const devices = [...existing.filter((item) => item.id !== device.id), entry]

  storage.setDevices(JSON.stringify(devices))

  return devices
}

/**
 * Gibt einem Gerät einen eigenen Namen — oder nimmt ihn wieder weg.
 *
 * Der Name gilt nur auf diesem Gerät und lässt sich jederzeit ändern; am
 * Rechner ändert sich davon nichts. Ein leerer Name ist deshalb kein Fehler,
 * sondern die Rückkehr zum Namen, den der Rechner selbst meldet.
 */
export function renameLocalDevice(id: string, alias: string): Device[] {
  const trimmed = alias.trim()

  const devices = parseDevices(storage.getDevices()).map((entry) => {
    if (entry.id !== id) {
      return entry
    }

    const { alias: _previous, ...rest } = entry

    return trimmed.length > 0 ? { ...rest, alias: trimmed } : rest
  })

  storage.setDevices(JSON.stringify(devices))

  return devices
}

/**
 * Hält fest, dass dieses Gerät gerade erreichbar war — und was es ist.
 *
 * <p>
 * Beides kommt aus derselben Antwort: wer `/api/info` beantwortet, war
 * erreichbar und sagt dabei, ob er ein Rechner oder ein Handy ist. Die Angabe
 * wird übernommen, weil ein Gerät, das vor Phase 31g gekoppelt wurde, sie noch
 * nicht mitbekommen hat.
 * </p>
 *
 * <p>
 * „Zuletzt verbunden" ist rein lokal: die Gegenseite weiß nicht, wann *dieses*
 * Gerät sie zuletzt gesehen hat, und ihre eigene Uhr hilft hier niemandem.
 * Geschrieben wird höchstens einmal je Minute — sonst schriebe die App im
 * Sekundentakt in den Speicher, und die Angabe wäre trotzdem keine andere.
 * </p>
 *
 * @returns Die neue Liste, oder `undefined`, wenn es nichts zu schreiben gab.
 */
export function rememberContact(
  id: string,
  platform?: DevicePlatform,
  now = Date.now(),
): Device[] | undefined {
  const devices = parseDevices(storage.getDevices())
  const previous = devices.find((entry) => entry.id === id)

  if (previous === undefined) {
    return undefined
  }

  // Geprüft und nicht geglaubt: die Angabe kommt aus einer JSON-Antwort, und
  // was dort steht, hat niemand vorher angesehen.
  const gemeldet =
    platform === 'windows' || platform === 'android' ? platform : undefined

  const neueZeit =
    previous.lastConnectedAt === undefined ||
    now - previous.lastConnectedAt >= TOUCH_INTERVAL_MS
  const neuePlattform = gemeldet !== undefined && previous.platform !== gemeldet

  if (!neueZeit && !neuePlattform) {
    return undefined
  }

  const updated = devices.map((entry) =>
    entry.id === id
      ? {
          ...entry,
          lastConnectedAt: neueZeit ? now : entry.lastConnectedAt,
          ...(gemeldet === undefined ? {} : { platform: gemeldet }),
        }
      : entry,
  )

  storage.setDevices(JSON.stringify(updated))

  return updated
}

/**
 * Wie grob „zuletzt verbunden" mitgeschrieben wird. Eine Minute ist genauer,
 * als es je jemanden interessiert — und selten genug, dass es nicht auffällt.
 */
const TOUCH_INTERVAL_MS = 60_000

/** Entfernt ein selbst gekoppeltes Gerät aus der lokalen Liste. */
export function forgetLocalDevice(id: string): Device[] {
  const devices = parseDevices(storage.getDevices()).filter((entry) => entry.id !== id)

  storage.setDevices(JSON.stringify(devices))

  return devices
}
