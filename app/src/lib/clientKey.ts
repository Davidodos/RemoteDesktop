/**
 * Das Schlüsselpaar dieses Clients.
 *
 * Es entsteht einmal und gilt für alle Rechner: jeder Agent merkt sich bei der
 * Kopplung den öffentlichen Teil, der private verlässt das Gerät nie. Damit gibt
 * es kein Geheimnis mehr, das man abtippt, weitergibt oder versehentlich in eine
 * Konfigurationsdatei schreibt.
 *
 * ECDSA P-256, weil der Browser es eingebaut hat und .NET 8 ebenfalls — keine
 * Abhängigkeit auf beiden Seiten.
 */

import { getPlatform } from '../platform/index.ts'
import { storage } from './storage.ts'

const ALGORITHM: EcKeyGenParams = { name: 'ECDSA', namedCurve: 'P-256' }
const SIGNATURE: EcdsaParams = { name: 'ECDSA', hash: 'SHA-256' }

export interface ClientKeyPair {
  /** Öffentlicher Schlüssel als Base64 im SPKI-Format — das versteht .NET direkt. */
  publicKey: string
  /** Privater Schlüssel als Base64 im PKCS-8-Format. Gehört in den Schlüsselspeicher. */
  privateKey: string
}

export async function createClientKey(): Promise<ClientKeyPair> {
  const pair = await crypto.subtle.generateKey(ALGORITHM, true, ['sign', 'verify'])

  return {
    publicKey: toBase64(await crypto.subtle.exportKey('spki', pair.publicKey)),
    privateKey: toBase64(await crypto.subtle.exportKey('pkcs8', pair.privateKey)),
  }
}

/**
 * Wie dieses Gerät auf der Gegenseite heißt: die Kennung, unter der es in ihrer
 * `clients.json` steht.
 *
 * <p>
 * Sie kommt aus dem Schlüssel selbst — SHA-256 über den öffentlichen Teil, davon
 * die ersten 16 Stellen. Beide Gegenstellen rechnen genauso
 * (`PairingService.FingerprintOf` und `shortFingerprint`), und deshalb kann
 * dieses Gerät sie ausrechnen, statt sie sich sagen zu lassen. Gebraucht wird
 * das für die Gegenrichtung: dort kommt kein `clientId` über die Leitung, weil
 * gar kein Kopplungsaufruf stattfindet.
 * </p>
 */
export async function clientFingerprint(publicKey: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', fromBase64(publicKey))

  return [...new Uint8Array(digest)]
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('')
    .slice(0, 16)
}

/**
 * Unterschreibt die Challenge des Agents.
 *
 * WebCrypto liefert die Unterschrift als r und s hintereinander, nicht als DER.
 * Der Agent prüft ausdrücklich in diesem Format — wer eine der beiden Seiten
 * umstellt, bekommt eine Prüfung, die immer fehlschlägt.
 */
export async function signChallenge(privateKey: string, nonce: string): Promise<string> {
  const key = await crypto.subtle.importKey('pkcs8', fromBase64(privateKey), ALGORITHM, false, [
    'sign',
  ])

  return toBase64(await crypto.subtle.sign(SIGNATURE, key, fromBase64(nonce)))
}

/**
 * Base64 statt der Rohbytes, weil der Schlüssel durch JSON und durch den
 * Schlüsselspeicher muss — beide können nur Text.
 */
function toBase64(buffer: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(buffer)))
}

/**
 * Der Rückweg legt ausdrücklich einen eigenen `ArrayBuffer` an. `Uint8Array.from`
 * liefert einen Typ, der auch auf geteiltem Speicher sitzen könnte — und den
 * nimmt WebCrypto nicht an.
 */
function fromBase64(value: string): Uint8Array<ArrayBuffer> {
  const binary = atob(value)
  const bytes = new Uint8Array(new ArrayBuffer(binary.length))

  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index)
  }

  return bytes
}

/**
 * Das eigene Schlüsselpaar.
 *
 * <p>
 * Ein Paar für alle Rechner: die Identität dieses Geräts ist überall dieselbe,
 * freigeschaltet wird sie bei jedem Agent einzeln.
 * </p>
 *
 * <p>
 * **Führt die Gegenstelle dieses Geräts eines, gilt ihres.** Am Rechner liegt
 * es in `{app}\data\clientkey.json`, am Handy bei den übrigen Schlüsseln des
 * Hosts — an beiden Stellen liest es außer dieser App auch der Server nebenan,
 * und der braucht es: beim Koppeln schickt er den öffentlichen Teil mit, damit
 * die Gegenseite dieses Gerät ohne einen zweiten Aufruf steuern darf.
 * </p>
 *
 * <p>
 * **Der Befund dahinter (16.08.2026):** vorher lag das Paar nur hier, und die
 * App hinterlegte den öffentlichen Teil beim Start beim eigenen Server. Wer im
 * Fenster nie die Fernsteuerung anzeigte, hinterlegte nie etwas — die
 * Gegenseite bekam ein leeres `clientKey` und konnte diesen Rechner danach
 * nicht steuern, ohne dass irgendwo stand, warum.
 * </p>
 *
 * <p>
 * Im Browser gibt es keine Gegenstelle. Dort bleibt es beim eigenen Speicher,
 * und das ist richtig so: was niemand steuern kann, muss auch niemand kennen.
 * </p>
 */
export async function ensureClientKey(): Promise<ClientKeyPair> {
  const provided = await getPlatform()
    .node.key()
    .catch(() => undefined)

  if (provided !== undefined) {
    return provided
  }

  const existing = parseClientKey(storage.getClientKey())

  if (existing !== undefined) {
    return existing
  }

  const created = await createClientKey()
  storage.setClientKey(JSON.stringify(created))

  return created
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

/**
 * Nur der private Teil — für die Anmeldung bei einem Agent.
 *
 * <p>
 * **Der Befund dahinter (16.08.2026):** der Transport las den Schlüssel
 * **synchron** aus dem Speicher der App und fiel auf ein leeres Token zurück,
 * wenn dort nichts stand. Seit 31h steht dort aber nichts mehr: der Ausweis
 * liegt nativ, am Handy in `clientkey.txt`, am Rechner in
 * `{app}\data\clientkey.json`. Solange noch ein Rest aus der Zeit davor im
 * Speicher lag, fiel das nicht auf — nach einer wirklich sauberen
 * Neuinstallation schickte die App jede Anfrage ohne Berechtigung los, und der
 * Agent notierte „Abgelehnt (Nicht angemeldet.)" für jede einzelne.
 * </p>
 */
export async function clientPrivateKey(): Promise<string> {
  return (await ensureClientKey()).privateKey
}
