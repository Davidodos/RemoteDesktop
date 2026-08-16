import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { getPlatform, setPlatform, type Platform } from '../platform/index.ts'
import { ensureClientKey, PairingError, pairWithAgent } from './pairing.ts'
import { storage } from './storage.ts'

/**
 * Die Kopplung. Sie ist der einzige Moment, in dem ein Nutzer noch etwas
 * abtippt — danach nie wieder.
 */

const ANSWER = {
  clientId: 'handy-1',
  scopes: ['screen', 'input'],
  hostname: 'PC',
  agentFingerprint: 'a1b2c3d4e5f60708',
}

/** Ein Ausweis, wie ihn die Gegenstelle dieses Geräts herausgäbe. */
const GERAET = { publicKey: 'oeffentlich', privateKey: 'privat' }

/** Die Vorgabe-Plattform, damit jeder Test wieder von ihr ausgeht. */
const original = getPlatform()

function respond(status: number, body: unknown): Response {
  const text = JSON.stringify(body)

  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(text),
    json: () => Promise.resolve(body),
  } as unknown as Response
}

/** `fetch`-Attrappe, die den erfolgreichen Kopplungsfall beantwortet. */
function fetchMock(): ReturnType<typeof vi.fn<(url: string, options: RequestInit) => Promise<Response>>> {
  return vi.fn<(url: string, options: RequestInit) => Promise<Response>>(() =>
    Promise.resolve(respond(200, ANSWER)),
  )
}

beforeEach(() => {
  localStorage.clear()
  setPlatform(original)
})

afterEach(() => {
  setPlatform(original)
  vi.unstubAllGlobals()
})

describe('das eigene Schlüsselpaar', () => {
  test('entsteht beim ersten Bedarf', async () => {
    // Act
    const key = await ensureClientKey()

    // Assert
    expect(key.publicKey.length).toBeGreaterThan(0)
    expect(storage.getClientKey()).toContain(key.publicKey)
  })

  test('bleibt danach dasselbe', async () => {
    // Arrange
    const first = await ensureClientKey()

    // Act
    const second = await ensureClientKey()

    // Assert — ein neues Paar hieße: alle Kopplungen sind wertlos.
    expect(second).toEqual(first)
  })

  test('ein halb geschriebener Eintrag wird ersetzt', async () => {
    // Arrange
    storage.setClientKey(JSON.stringify({ publicKey: 'nur die Hälfte' }))

    // Act
    const key = await ensureClientKey()

    // Assert — sonst liefe die Kopplung durch und jede Anmeldung danach nicht.
    expect(key.privateKey.length).toBeGreaterThan(0)
  })

  test('führt die Gegenstelle eines, gilt ihres', async () => {
    // Arrange — am Rechner liegt das Paar in {app}\data, am Handy bei den
    // Schlüsseln des Hosts. Dort liest es außer dieser App auch der Server
    // nebenan, und der schickt den öffentlichen Teil beim Koppeln mit.
    const platform = getPlatform()

    setPlatform({
      ...platform,
      node: { ...platform.node, key: () => Promise.resolve(GERAET) },
    } as Platform)

    // Act
    const key = await ensureClientKey()

    // Assert
    expect(key).toEqual(GERAET)

    // Und nichts davon landet nebenbei im eigenen Speicher: zwei Ablagen
    // desselben Ausweises wären zwei Gelegenheiten, verschiedene zu benutzen.
    expect(storage.getClientKey()).toBeUndefined()
  })

  test('ohne Gegenstelle bleibt es beim eigenen Speicher', async () => {
    // Arrange — der Browser führt keinen. Das ist kein Fehler: was niemand
    // steuern kann, muss auch niemand kennen.
    const platform = getPlatform()

    setPlatform({
      ...platform,
      node: { ...platform.node, key: () => Promise.reject(new Error('kein Fenster')) },
    } as Platform)

    // Act
    const key = await ensureClientKey()

    // Assert
    expect(key.privateKey.length).toBeGreaterThan(0)
    expect(storage.getClientKey()).toContain(key.publicKey)
  })
})

describe('mit einem Agent koppeln', () => {
  test('Code, Name und der öffentliche Schlüssel gehen hinaus', async () => {
    // Arrange
    const fetched = fetchMock()
    vi.stubGlobal('fetch', fetched)

    // Act
    await pairWithAgent({ host: 'pc.ts.net', port: 8443, code: '123456', label: 'Handy' })

    // Assert — der private Teil bleibt hier, das ist der ganze Punkt.
    const [url, options] = fetched.mock.calls[0]!
    const sent = JSON.parse(options.body as string) as Record<string, string>

    expect(url).toBe('https://pc.ts.net:8443/api/pair')
    expect(sent.code).toBe('123456')
    expect(sent.label).toBe('Handy')
    expect(sent.publicKey).toBe((await ensureClientKey()).publicKey)
    expect(JSON.stringify(sent)).not.toContain((await ensureClientKey()).privateKey)
  })

  test('das Gerät wird über den Fingerabdruck des Agents geführt', async () => {
    // Arrange
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respond(200, ANSWER))))

    // Act
    const device = await pairWithAgent({
      host: 'pc.ts.net',
      port: 8443,
      code: '123456',
      label: 'Handy',
    })

    // Assert — der bleibt gleich, auch wenn der Rechner umbenannt wird.
    expect(device).toEqual({
      id: 'a1b2c3d4e5f60708',
      name: 'PC',
      host: 'pc.ts.net',
      port: 8443,
      clientId: 'handy-1',
      fingerprint: 'a1b2c3d4e5f60708',
      canWake: false,
    })
    expect(device.token).toBeUndefined()
  })

  test('die Meldung des Agents kommt beim Nutzer an', async () => {
    // Arrange
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(respond(400, { error: 'Code falsch oder abgelaufen.' }))),
    )

    // Act
    const failure = await pairWithAgent({
      host: 'pc.ts.net',
      port: 8443,
      code: '000000',
      label: 'Handy',
    }).catch((error: unknown) => error)

    // Assert — „HTTP 400" hilft niemandem beim Abtippen eines Codes.
    expect(failure).toBeInstanceOf(PairingError)
    expect((failure as Error).message).toBe('Code falsch oder abgelaufen.')
  })

  test('ein toter Rechner wird als solcher benannt', async () => {
    // Arrange
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new Error('failed to fetch'))),
    )

    // Act
    const failure = await pairWithAgent({
      host: 'pc.ts.net',
      port: 8443,
      code: '123456',
      label: 'Handy',
    }).catch((error: unknown) => error)

    // Assert
    expect((failure as Error).message).toContain('pc.ts.net antwortet nicht')
  })
})

describe('der Fingerabdruck der Zertifizierungsstelle', () => {
  async function pair(caFingerprint: unknown): Promise<{ caFingerprint?: string }> {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(respond(200, { ...ANSWER, caFingerprint }))),
    )

    return await pairWithAgent({ host: 'pc', port: 8443, code: '123456', label: 'Handy' })
  }

  test('bleibt weg, wenn der Agent null meldet', async () => {
    // Genau das schreibt ein Agent mit Zertifikat von Tailscale. Kommt es
    // durch, verlangt die App danach eine Bestätigung, die es nicht zu geben
    // braucht — und der Knopf dafür kann nichts tun.
    expect((await pair(null)).caFingerprint).toBeUndefined()
  })

  test('bleibt weg, wenn der Agent das Feld gar nicht meldet', async () => {
    expect((await pair(undefined)).caFingerprint).toBeUndefined()
  })

  test('kommt an, wenn wirklich einer dasteht', async () => {
    const wert = 'b'.repeat(64)

    expect((await pair(wert)).caFingerprint).toBe(wert)
  })
})
