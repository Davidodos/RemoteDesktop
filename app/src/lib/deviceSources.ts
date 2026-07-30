import type { HubClient } from './hubClient.ts'
import { storage } from './storage.ts'
import type { Device } from './types.ts'

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

export function hubDeviceSource(hub: HubClient): DeviceSource {
  return {
    id: 'hub',
    list: (): Promise<Device[]> => hub.getDevices(),
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

  const { id, name, host, port, token, canWake } = entry as Record<string, unknown>

  if (typeof id !== 'string' || id.length === 0 || typeof host !== 'string' || host.length === 0) {
    return []
  }

  // Ohne Token käme die App bis zur ersten Anfrage und stünde dann vor einem
  // 401, das wie ein Fehler des Agents aussieht.
  if (typeof token !== 'string' || token.length === 0) {
    return []
  }

  if (!Number.isInteger(port) || (port as number) <= 0) {
    return []
  }

  return [
    {
      id,
      name: typeof name === 'string' && name.length > 0 ? name : id,
      host,
      port: port as number,
      token,
      canWake: canWake === true,
    },
  ]
}
