import type { Device, DeviceStatus } from './types.ts'

/**
 * Kurz genug, dass die Geräteliste flüssig lädt, lang genug für einen
 * Tailscale-Handshake über DERP.
 */
const PROBE_TIMEOUT_MS = 3000

/**
 * Ob ein Knoten gerade antwortet — vom Client aus gefragt, nicht vom Hub.
 *
 * Bis Phase 13 hat der Hub auf der NAS reihum alle Geräte angetippt und der App
 * eine fertige Liste geliefert. Mit ihm ist auch die Geräteliste verschwunden;
 * gefragt wird jetzt von dort, wo die Antwort gebraucht wird. Das ist ohnehin
 * die ehrlichere Auskunft: dass die NAS einen Rechner erreicht, heißt nicht,
 * dass das Handy es auch tut.
 *
 * Gefragt wird `/health` — der einzige Endpunkt, der ohne Ausweis auskommt.
 * Eine Anmeldung nur zum Nachsehen, ob jemand da ist, wäre verkehrt herum.
 */
export async function isReachable(device: Device): Promise<boolean> {
  try {
    const response = await fetch(`https://${device.host}:${device.port}/health`, {
      signal: AbortSignal.timeout(PROBE_TIMEOUT_MS),
      // Der Zwischenspeicher des Browsers würde einen längst schlafenden
      // Rechner noch minutenlang als wach ausweisen.
      cache: 'no-store',
    })

    return response.ok
  } catch {
    // Zeitüberschreitung, unbekannter Name, abgelehnte Verbindung — für die
    // Frage „antwortet er?" ist das alles dasselbe.
    return false
  }
}

/**
 * Fragt alle Knoten gleichzeitig. Ein schlafender Rechner darf die Antwort für
 * die anderen nicht aufhalten.
 */
export async function probeAll(devices: readonly Device[]): Promise<DeviceStatus[]> {
  return await Promise.all(
    devices.map(async (device) => ({
      id: device.id,
      online: await isReachable(device),
    })),
  )
}

/** Die Kennungen derer, die geantwortet haben — so, wie `wake.ts` sie erwartet. */
export function onlineIds(statuses: readonly DeviceStatus[]): Set<string> {
  return new Set(statuses.filter((status) => status.online).map((status) => status.id))
}
