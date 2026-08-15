import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { grantPeer } from './bothWays.ts'
import { getPlatform, noLocalNode, setPlatform } from '../platform/index.ts'
import type { DeviceProfile, LocalNode, Platform } from '../platform/index.ts'

/**
 * Die Gegenrichtung einer Kopplung.
 *
 * <p>
 * Geprüft wird hier vor allem das Gegenteil des Erfolgs. Eine Gegenrichtung, die
 * scheitert, sieht am Bildschirm genauso aus wie eine, die nie angeboten wurde —
 * und das war der Fehler: am echten Gerät stand danach „kennt dieses Gerät nicht
 * mehr", direkt nach einer Kopplung, die eben erst gelungen war.
 * </p>
 */

const original = getPlatform()

/** Ein lokaler Knoten, der mitschreibt, was bei ihm eingetragen wurde. */
function node(overrides: Partial<LocalNode> = {}): LocalNode & { granted: string[] } {
  const granted: string[] = []

  return {
    ...noLocalNode,
    available: true,
    profile: (): Promise<DeviceProfile | undefined> =>
      Promise.resolve({ host: '192.168.178.31', port: 8443, name: 'Handy' }),
    grant: (publicKey: string): Promise<void> => {
      granted.push(publicKey)

      return Promise.resolve()
    },
    granted,
    ...overrides,
  }
}

function use(local: LocalNode): void {
  setPlatform({ ...getPlatform(), node: local } as Platform)
}

beforeEach(() => {
  setPlatform(original)
})

afterEach(() => {
  setPlatform(original)
  vi.restoreAllMocks()
})

describe('die Gegenseite eintragen', () => {
  test('trägt den Schlüssel bei einem Gerät ein, das eine Gegenstelle ist', async () => {
    // Arrange
    const local = node()
    use(local)

    // Act
    const warnung = await grantPeer({ name: 'PC', clientKey: 'AAAA' })

    // Assert
    expect(warnung).toBeUndefined()
    expect(local.granted).toEqual(['AAAA'])
  })

  test('trägt auch ohne eigene Adresse ein', async () => {
    // Arrange — ein Handy, das im Augenblick der Kopplung in keinem Netz hängt.
    // Genau hier brach die Gegenrichtung vorher still ab: gefragt wurde nach
    // einem Steckbrief, und der braucht eine Adresse. Ob dieses Gerät gerade
    // erreichbar ist, hat mit der Frage, wer es steuern darf, aber nichts zu
    // tun — der Eintrag wirkt, sobald der Server startet.
    const local = node({ profile: () => Promise.resolve(undefined) })
    use(local)

    // Act
    const warnung = await grantPeer({ name: 'PC', clientKey: 'AAAA' })

    // Assert
    expect(warnung).toBeUndefined()
    expect(local.granted).toEqual(['AAAA'])
  })

  test('meldet, wenn die Gegenseite ihren Ausweis nicht mitgeschickt hat', async () => {
    // Arrange — die Gegenstelle antwortet, ihr Fenster hat den Ausweis aber nie
    // beim eigenen Agent hinterlegt.
    const local = node()
    use(local)

    // Act
    const warnung = await grantPeer({ name: 'PC' })

    // Assert — still zu bleiben hieße, eine halbe Kopplung als ganze auszugeben.
    expect(warnung).toContain('PC')
    expect(local.granted).toEqual([])
  })

  test('meldet einen Fehlschlag beim Eintragen', async () => {
    // Arrange
    const local = node({ grant: () => Promise.reject(new Error('kein Platz')) })
    use(local)

    // Act
    const warnung = await grantPeer({ name: 'PC', clientKey: 'AAAA' })

    // Assert
    expect(warnung).toContain('kein Platz')
  })

  test('schweigt, wo es gar keine Gegenstelle gibt', async () => {
    // Arrange — der Browser. Dort ist eine fehlende Gegenrichtung der
    // Normalfall und keine Meldung wert.
    use(noLocalNode)

    // Act
    const warnung = await grantPeer({ name: 'PC', clientKey: 'AAAA' })

    // Assert
    expect(warnung).toBeUndefined()
  })

  test('schweigt bei einem Waker, der keine Gegenrichtung anbietet', async () => {
    // Arrange
    const local = node()
    use(local)

    // Act
    const warnung = await grantPeer(undefined)

    // Assert
    expect(warnung).toBeUndefined()
    expect(local.granted).toEqual([])
  })
})
