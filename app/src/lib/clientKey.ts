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
