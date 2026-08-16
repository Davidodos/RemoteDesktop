import { clientFingerprint } from './clientKey.ts'
import { ensureClientKey } from './pairing.ts'
import { TRUST_PORT } from './certificateTrust.ts'
import { saveLocalDevice } from './deviceSources.ts'
import { getPlatform } from '../platform/index.ts'
import type { DeviceProfile } from '../platform/index.ts'
import type { Device } from './types.ts'

/**
 * Die Kopplung geht immer in beide Richtungen — und beide Hälften stehen hier.
 *
 * <p>
 * **Kein Netzverkehr mehr.** Beim Koppeln haben beide Seiten ausgetauscht, was
 * sie voneinander brauchen: die Anfrage trug den Steckbrief des Anrufers, die
 * Antwort den Ausweis der Gegenseite. Was danach zu tun ist, erledigt jede Seite
 * bei sich zu Hause — der Schlüssel der anderen in die eigene `clients.json`,
 * ihr Steckbrief in die eigene Geräteliste.
 * </p>
 *
 * <p>
 * **Der Vorgänger reichte stattdessen einen Kopplungscode weiter**, den die
 * Gegenseite binnen fünf Minuten einlösen musste. Damit hing die Gegenrichtung
 * an einem laufenden Server, einem offenen Fenster und einer Uhr — und wer beim
 * Koppeln die Freigabe nicht eingeschaltet hatte, bekam sie nie, auch später
 * nicht. Ein Steckbrief hat keine Frist: er wirkt, sobald der Server startet.
 * </p>
 */

/**
 * Der Ausweis der Gegenseite, so wie er über die Leitung kommt.
 *
 * `clientKey` fehlt, wenn die Gegenstelle zwar antwortet, ihr eigenes Fenster
 * den Ausweis aber nie hinterlegt hat. Das ist ein anderer Fall als „keine
 * Gegenseite" und wird seit dem Befund in {@link grantPeer} auch anders
 * behandelt.
 */
export interface PeerCredential {
  name: string
  clientKey?: string
}

/**
 * Trägt die Gegenseite bei sich ein: ihre Oberfläche darf dieses Gerät steuern.
 *
 * Ohne Rückfrage. Der Schlüssel kam über eine Verbindung, an deren Anfang jemand
 * einen Code eingetippt oder einen QR-Code gescannt hat — dieselbe Entscheidung
 * ein zweites Mal zu verlangen wäre keine Sicherheit, sondern eine Zumutung.
 *
 * @returns Ein Satz, wenn es nicht geklappt hat. Ein Fehlschlag, der still
 *   bleibt, sieht genauso aus wie eine Gegenrichtung, die nie angeboten wurde —
 *   und danach sucht niemand mehr.
 */
export async function grantPeer(
  peer: PeerCredential | undefined,
): Promise<string | undefined> {
  const node = getPlatform().node

  // Kein Ziel, keine Gegenrichtung — im Browser ist das der Normalfall und
  // kein Fehler.
  //
  // Gefragt wird `available` und **nicht mehr**, ob es gerade einen Steckbrief
  // gibt. Das war der Fehler: ein Steckbrief braucht eine Adresse, und ein
  // Handy, das im Augenblick der Kopplung in keinem Netz hing, hatte keine.
  // Dann wurde der Schlüssel der Gegenseite nicht eingetragen — und weil das
  // still geschah, sah es aus wie eine gelungene Kopplung. Danach stand das
  // Gerät auf beiden Seiten in der Liste, und jede Anfrage kam mit „kenne ich
  // nicht" zurück. Ob dieses Gerät gerade eine Adresse hat, hat mit der Frage,
  // wer es steuern darf, nichts zu tun.
  if (peer === undefined || !(await node.ready())) {
    return undefined
  }

  // Die Gegenseite ist eine Gegenstelle, hat aber keinen Ausweis mitgeschickt.
  // Das ist der Fall, der vorher genauso aussah wie „gar keine Gegenrichtung
  // angeboten" — und deshalb nie jemandem auffiel.
  //
  // Seit 31h liegt der Ausweis dort in einer Datei neben den übrigen
  // Schlüsseln, und die legt an, wer zuerst kommt. Bleibt das Feld trotzdem
  // leer, ist die Gegenseite entweder älter als dieser Umbau oder kommt in
  // ihren eigenen Datenordner nicht hinein.
  if (peer.clientKey === undefined) {
    return (
      `${peer.name} kann dieses Gerät noch nicht steuern: die Gegenseite hat ` +
      'ihren Ausweis nicht mitgeschickt. Dort läuft vermutlich eine ältere ' +
      'Fassung — dort aktualisieren, und danach noch einmal koppeln.'
    )
  }

  try {
    await node.grant(peer.clientKey, peer.name)

    return undefined
  } catch (failure) {
    return `${peer.name} kann dieses Gerät noch nicht steuern: ${
      failure instanceof Error ? failure.message : String(failure)
    }`
  }
}

/**
 * Holt die Steckbriefe ab, die beim Koppeln hier abgegeben wurden, und nimmt sie
 * in die Geräteliste auf.
 *
 * <p>
 * **Erst eintragen, dann vergessen.** Andersherum war es falsch: ging nach dem
 * Abholen irgendetwas schief, war der Steckbrief endgültig weg, und am
 * Bildschirm stand „noch kein Gerät gekoppelt" ohne zweiten Versuch.
 * </p>
 *
 * @returns Die neue Liste, oder `undefined`, wenn es nichts abzuholen gab.
 */
export async function collectPeers(): Promise<Device[] | undefined> {
  const found = await getPlatform().node.peers()

  if (found.length === 0) {
    return undefined
  }

  // Die eigene Kennung wird ausgerechnet, nicht erfragt: bei der Gegenrichtung
  // findet kein Kopplungsaufruf statt, aus dem sie zurückkäme. Beide
  // Gegenstellen bilden sie aus demselben Schlüssel auf dieselbe Weise.
  const clientId = await clientFingerprint((await ensureClientKey()).publicKey)

  let devices: Device[] | undefined
  const eingetragen: string[] = []

  for (const peer of found) {
    await trust(peer)

    devices = saveLocalDevice(await toDevice(peer, clientId))

    if (peer.id !== undefined) {
      eingetragen.push(peer.id)
    }
  }

  // Jetzt, und keinen Schritt früher.
  if (eingetragen.length > 0) {
    await getPlatform().node.forget(eingetragen)
  }

  return devices
}

/**
 * Der Stelle der Gegenseite vertrauen.
 *
 * Ohne das steht sie zwar in der Liste, ließe sich aber nicht verbinden — und
 * die Meldung darüber sieht aus wie ein Gerät, das nicht antwortet. Der
 * Fingerabdruck kam über die Kopplung mit; verglichen wird gegen ihn, damit
 * hier nicht irgendein Zertifikat vom offenen Port eingesammelt wird.
 *
 * Fehlschläge bleiben folgenlos: das Gerät kommt trotzdem in die Liste. Ist es
 * gerade nicht erreichbar, ist das kein Grund, es zu vergessen.
 */
async function trust(peer: DeviceProfile): Promise<void> {
  const platform = getPlatform()

  if (peer.caFingerprint === undefined || !platform.trust.available) {
    return
  }

  try {
    const certificate = await platform.trust.fetchAuthority?.(peer.host, TRUST_PORT)

    if (certificate !== undefined && certificate.fingerprint === peer.caFingerprint) {
      await platform.trust.install(certificate.base64, certificate.fingerprint)
    }
  } catch {
    // Siehe oben.
  }
}

/**
 * Aus einem Steckbrief wird ein Gerät.
 *
 * Die Kennung ist der Fingerabdruck des Agents — er bleibt gleich, auch wenn
 * Name oder Adresse wechseln. Fehlt er, tut es die Adresse; dasselbe tut die
 * Kopplung auf dem gewöhnlichen Weg.
 *
 * `clientId` ist der eigene Ausweis: unter dieser Kennung steht dieses Gerät in
 * der `clients.json` der Gegenseite. Ohne sie käme die App bis zur ersten
 * Anfrage und stünde dann vor einem 401, das wie ein Fehler der Gegenstelle
 * aussieht — und `parseDevices` wirft einen Eintrag ohne Ausweis ohnehin weg.
 */
async function toDevice(peer: DeviceProfile, clientId: string): Promise<Device> {
  return {
    id: peer.agentFingerprint ?? peer.host,
    clientId,
    name: peer.name,
    host: peer.host,
    port: peer.port,
    ...(peer.agentFingerprint === undefined ? {} : { fingerprint: peer.agentFingerprint }),
    ...(peer.caFingerprint === undefined ? {} : { caFingerprint: peer.caFingerprint }),
    ...(peer.platform === undefined ? {} : { platform: peer.platform }),
    // Unter dieser Kennung steht die Gegenseite in der eigenen Liste der
    // zugelassenen Geräte — ohne sie ließe „Entfernen" die Gegenrichtung stehen.
    ...(peer.clientKey === undefined
      ? {}
      : { peerClientId: await clientFingerprint(peer.clientKey) }),
    // Ein Handy weckt niemanden, und ein Rechner, den man selbst gekoppelt hat,
    // wird über seinen eigenen Eintrag geweckt — nicht über diesen.
    canWake: false,
  }
}
