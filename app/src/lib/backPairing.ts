import { pairWithAgent } from './pairing.ts'
import { saveLocalDevice } from './deviceSources.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from './types.ts'

/**
 * Die Gegenrichtung einlösen.
 *
 * <p>
 * **Warum das nicht der Agent selbst tut:** koppeln heißt, einen privaten
 * Geräteschlüssel zu benutzen und ein Gerät in eine Liste einzutragen. Beides
 * liegt in der App und nicht im Agent. Der hebt das Angebot nur auf; hier wird
 * es abgeholt und ausgeführt.
 * </p>
 *
 * <p>
 * **Warum ohne Rückfrage:** das Angebot kam über eine Verbindung, die gerade
 * beglaubigt wurde — jemand hat am anderen Gerät einen Kopplungscode
 * eingetippt oder einen QR-Code gescannt. Noch einmal zu fragen hieße, dieselbe
 * Entscheidung zweimal zu verlangen. Der Fingerabdruck der Gegenstelle kommt
 * auf demselben Weg mit und muss deshalb auch nicht mehr abgelesen werden.
 * </p>
 */
export async function completeBackPairing(): Promise<Device[] | undefined> {
  const platform = getPlatform()
  const offer = await platform.node.take()

  if (offer === undefined) {
    return undefined
  }

  // Erst vertrauen, dann koppeln — dieselbe Reihenfolge wie beim gewöhnlichen
  // Weg. Ohne das scheitert schon der erste Aufruf am Zertifikat, und die
  // Meldung darüber sieht aus wie ein Gerät, das nicht antwortet.
  if (offer.caFingerprint !== undefined && platform.trust.available) {
    const certificate = await platform.trust.fetchAuthority?.(offer.host, TRUST_PORT)

    if (certificate !== undefined && certificate.fingerprint === offer.caFingerprint) {
      await platform.trust.install(certificate.base64, certificate.fingerprint)
    }
  }

  const device = await pairWithAgent({
    host: offer.host,
    port: offer.port,
    code: offer.code,
    label: await label(),
  })

  // Die fertige Liste zurück, damit der Aufrufer sie nicht ein zweites Mal
  // lesen muss — sie ist gerade geschrieben worden.
  return saveLocalDevice(device)
}

/** Der Port, auf dem eine Gegenstelle ausschließlich ihre CA anbietet. */
const TRUST_PORT = 8442

/**
 * Wie dieses Gerät drüben heißen soll.
 *
 * Am Rechner ist das sein Windows-Name. Am Handy gibt es keinen — dort nennt
 * sich der eigene Host so, wie das Gerät in den Android-Einstellungen heißt.
 * Ohne das stünde in der Liste jedes PCs dreimal „RemoteDesktop", und niemand
 * wüsste, welches Handy gemeint ist.
 */
async function label(): Promise<string> {
  const platform = getPlatform()

  if (platform.machineName !== undefined) {
    return platform.machineName
  }

  const own = await platform.host.status().catch(() => undefined)

  return own?.deviceName !== undefined && own.deviceName.length > 0
    ? own.deviceName
    : 'RemoteDesktop'
}
