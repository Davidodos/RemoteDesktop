import { Socket } from 'node:net'

/**
 * Kurz genug, dass die Geräteliste flüssig lädt, lang genug für einen
 * Tailscale-Handshake über DERP.
 */
const PROBE_TIMEOUT_MS = 2000

/**
 * Warum ein Gerät nicht erreichbar ist.
 *
 * Die Unterscheidung ist keine Kosmetik: ein unbekannter Name heißt, dass die
 * Namensauflösung des Hubs nicht stimmt — dann sind alle Geräte offline, egal
 * ob sie laufen. Das sieht von außen genauso aus wie ein ausgeschalteter PC,
 * ist aber ein Fehler auf der NAS.
 */
export type ProbeFailure = 'dns' | 'unreachable'

export interface ProbeResult {
  online: boolean
  reason?: ProbeFailure
}

/**
 * Prüft, ob der Agent auf einem Gerät erreichbar ist.
 *
 * Bewusst nur ein TCP-Connect statt eines HTTPS-Requests auf `/health`:
 * Es geht allein um „läuft der Rechner und lauscht der Agent" — ein voller
 * TLS-Handshake pro Gerät würde die Geräteliste unnötig verzögern.
 */
export async function probe(host: string, port: number): Promise<ProbeResult> {
  return new Promise((resolve) => {
    const socket = new Socket()
    let settled = false

    const finish = (result: ProbeResult): void => {
      if (settled) {
        return
      }

      settled = true
      socket.destroy()
      resolve(result)
    }

    socket.setTimeout(PROBE_TIMEOUT_MS)
    socket.once('connect', () => finish({ online: true }))
    socket.once('timeout', () => finish({ online: false, reason: 'unreachable' }))
    socket.once('error', (error: NodeJS.ErrnoException) =>
      finish({
        online: false,
        reason: error.code === 'ENOTFOUND' || error.code === 'EAI_AGAIN'
          ? 'dns'
          : 'unreachable',
      }))

    socket.connect(port, host)
  })
}

/** Kurzform für Aufrufer, die nur das Ja/Nein brauchen. */
export async function isReachable(host: string, port: number): Promise<boolean> {
  return (await probe(host, port)).online
}
