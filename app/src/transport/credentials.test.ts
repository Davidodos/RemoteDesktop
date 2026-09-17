import { describe, expect, test, vi } from 'vitest'
import { createClientKey } from '../lib/clientKey.ts'
import { noCredentials, pairedCredentials, type SessionExchange } from './credentials.ts'

/**
 * Der Ausweis der App: ein Sitzungstoken, das erst per Challenge-Response
 * geholt wird — der Transport soll den Unterschied zwischen „liegt vor" und
 * „muss geholt werden" nur an einer Stelle merken.
 */
describe('ohne Kopplung', () => {
  test('gibt es keinen Ausweis, sondern eine Absage', async () => {
    const credentials = noCredentials()

    expect(credentials.peek()).toBeUndefined()
    await expect(credentials.obtain()).rejects.toThrow('nicht gekoppelt')
  })
})

describe('die Anmeldung eines gekoppelten Geräts', () => {
  test('erst nach der Anmeldung liegt ein Token vor', async () => {
    // Arrange
    const { credentials, exchange } = await setup()

    // Assert
    expect(credentials.peek()).toBeUndefined()

    // Act
    const token = await credentials.obtain()

    // Assert
    expect(token).toBe('sitzungstoken')
    expect(credentials.peek()).toBe('sitzungstoken')
    expect(exchange.challenge).toHaveBeenCalledWith('handy-1')
  })

  test('die Challenge wird unterschrieben weitergereicht', async () => {
    // Arrange
    const { credentials, exchange } = await setup()

    // Act
    await credentials.obtain()

    // Assert
    const [clientId, nonce, signature] = exchange.open.mock.calls[0] as string[]

    expect(clientId).toBe('handy-1')
    expect(nonce).toBe(btoa('challenge'))
    expect(atob(signature!).length).toBe(64)
  })

  test('mehrere Anfragen gleichzeitig lösen eine Anmeldung aus', async () => {
    // Arrange — beim Start fragen Bild, Eingabe und die Geräteauskunft
    // gleichzeitig nach einem Ausweis.
    const { credentials, exchange } = await setup()

    // Act
    await Promise.all([credentials.obtain(), credentials.obtain(), credentials.obtain()])

    // Assert
    expect(exchange.challenge).toHaveBeenCalledTimes(1)
  })

  test('ein gemerktes Token wird nicht neu geholt', async () => {
    // Arrange
    const { credentials, exchange } = await setup()

    // Act
    await credentials.obtain()
    await credentials.obtain()

    // Assert
    expect(exchange.challenge).toHaveBeenCalledTimes(1)
  })

  test('nach dem Verwerfen wird neu angemeldet', async () => {
    // Arrange
    const { credentials, exchange } = await setup()
    await credentials.obtain()

    // Act — so kommt die App nach zwölf Stunden wieder herein.
    credentials.invalidate()
    await credentials.obtain()

    // Assert
    expect(credentials.peek()).toBe('sitzungstoken')
    expect(exchange.challenge).toHaveBeenCalledTimes(2)
  })

  test('eine gescheiterte Anmeldung blockiert die nächste nicht', async () => {
    // Arrange
    const { credentials, exchange } = await setup()
    exchange.challenge.mockRejectedValueOnce(new Error('nicht erreichbar'))

    // Act
    await expect(credentials.obtain()).rejects.toThrow('nicht erreichbar')

    // Assert — ein hängengebliebener Vorgang würde die App dauerhaft aussperren.
    expect(await credentials.obtain()).toBe('sitzungstoken')
  })
})

/** Ein gekoppeltes Gerät mit echtem Schlüssel und einem Agent-Doppelgänger. */
async function setup(): Promise<{
  credentials: ReturnType<typeof pairedCredentials>
  exchange: { challenge: ReturnType<typeof vi.fn>; open: ReturnType<typeof vi.fn> }
}> {
  const key = await createClientKey()

  const exchange = {
    challenge: vi.fn(() => Promise.resolve(btoa('challenge'))),
    open: vi.fn(() => Promise.resolve('sitzungstoken')),
  }

  return {
    credentials: pairedCredentials(
      'handy-1',
      () => Promise.resolve(key.privateKey),
      exchange as unknown as SessionExchange,
    ),
    exchange,
  }
}
