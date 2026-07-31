import { describe, expect, test } from 'vitest'
import { createClientKey, signChallenge } from './clientKey.ts'

/**
 * Das Schlüsselpaar dieses Geräts. Es ersetzt das abgetippte Token — geht hier
 * etwas schief, kommt die App bei keinem Rechner mehr herein.
 */
describe('Schlüsselpaar', () => {
  test('liefert beide Teile als Base64', async () => {
    // Act
    const key = await createClientKey()

    // Assert
    expect(key.publicKey).toMatch(/^[A-Za-z0-9+/]+=*$/)
    expect(key.privateKey).toMatch(/^[A-Za-z0-9+/]+=*$/)
  })

  test('zweimal erzeugt heißt zweimal verschieden', async () => {
    // Act
    const first = await createClientKey()
    const second = await createClientKey()

    // Assert
    expect(first.publicKey).not.toBe(second.publicKey)
  })

  test('der öffentliche Teil ist ein P-256-Schlüssel im SPKI-Format', async () => {
    // Arrange
    const key = await createClientKey()

    // Act — gelingt der Import unter genau diesen Angaben, versteht ihn auch
    // die Gegenstelle in .NET.
    const imported = await crypto.subtle.importKey(
      'spki',
      Uint8Array.from(atob(key.publicKey), (character) => character.charCodeAt(0)),
      { name: 'ECDSA', namedCurve: 'P-256' },
      true,
      ['verify'],
    )

    // Assert
    expect(imported.type).toBe('public')
  })
})

describe('Unterschrift über die Challenge', () => {
  test('die eigene Unterschrift geht durch die eigene Prüfung', async () => {
    // Arrange
    const key = await createClientKey()
    const nonce = btoa('eine Challenge vom Agent')

    // Act
    const signature = await signChallenge(key.privateKey, nonce)

    // Assert
    expect(await verify(key.publicKey, nonce, signature)).toBe(true)
  })

  test('eine andere Challenge fällt durch', async () => {
    // Arrange
    const key = await createClientKey()
    const signature = await signChallenge(key.privateKey, btoa('Challenge eins'))

    // Assert — sonst genügte eine einmal mitgeschnittene Unterschrift für immer.
    expect(await verify(key.publicKey, btoa('Challenge zwei'), signature)).toBe(false)
  })

  test('ein fremder Schlüssel fällt durch', async () => {
    // Arrange
    const mine = await createClientKey()
    const stranger = await createClientKey()
    const nonce = btoa('eine Challenge')

    // Act
    const signature = await signChallenge(stranger.privateKey, nonce)

    // Assert
    expect(await verify(mine.publicKey, nonce, signature)).toBe(false)
  })

  test('die Unterschrift hat die feste Länge von r und s', async () => {
    // Arrange
    const key = await createClientKey()

    // Act
    const signature = await signChallenge(key.privateKey, btoa('x'))

    // Assert — der Agent prüft ausdrücklich dieses Format und nicht DER. 64
    // Byte heißt: zweimal 32, ohne Hülle drumherum.
    expect(atob(signature).length).toBe(64)
  })
})

/** Prüft so, wie es der Agent tut — nur eben mit den Mitteln des Browsers. */
async function verify(publicKey: string, nonce: string, signature: string): Promise<boolean> {
  const key = await crypto.subtle.importKey(
    'spki',
    decode(publicKey),
    { name: 'ECDSA', namedCurve: 'P-256' },
    false,
    ['verify'],
  )

  return await crypto.subtle.verify(
    { name: 'ECDSA', hash: 'SHA-256' },
    key,
    decode(signature),
    decode(nonce),
  )
}

function decode(value: string): Uint8Array<ArrayBuffer> {
  const binary = atob(value)
  const bytes = new Uint8Array(new ArrayBuffer(binary.length))

  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index)
  }

  return bytes
}
