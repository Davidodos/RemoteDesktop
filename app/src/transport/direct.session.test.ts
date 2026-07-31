import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import type { Device } from '../lib/types.ts'
import type { Credentials } from './credentials.ts'
import { directTransport } from './direct.ts'
import { TransportError } from './index.ts'

/**
 * Was sich mit der Kopplung am Transport ändert: der Ausweis liegt nicht mehr
 * von Anfang an vor, sondern wird geholt — und läuft nach zwölf Stunden ab.
 */

const DEVICE: Device = {
  id: 'abc',
  name: 'PC',
  host: 'pc.example.ts.net',
  port: 8443,
  clientId: 'handy-1',
  canWake: false,
}

/** WebSocket-Ersatz, der nichts verbindet, sondern nur mitschreibt. */
class FakeSocket {
  static readonly OPEN = 1
  static instances: FakeSocket[] = []

  readyState = 0
  binaryType = 'blob'

  constructor(readonly url: string) {
    FakeSocket.instances.push(this)
  }

  addEventListener(): void {
    // Für diese Tests zählt nur, ob und mit welcher Adresse geöffnet wurde.
  }

  send(): void {}
  close(): void {}
}

/** Ein Ausweis, der erst auf Anforderung eintrifft — wie eine Anmeldung. */
function deferredCredentials(): Credentials & { deliver: (token: string) => void } {
  let token: string | undefined
  let release: ((value: string) => void) | undefined

  return {
    peek: () => token,
    obtain: () =>
      new Promise<string>((resolve) => {
        release = (value) => {
          token = value
          resolve(value)
        }
      }),
    invalidate: () => {
      token = undefined
    },
    deliver: (value) => release?.(value),
  }
}

function respond(status: number, body: string): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(body),
    json: () => Promise.resolve(JSON.parse(body) as unknown),
  } as unknown as Response
}

beforeEach(() => {
  FakeSocket.instances = []
  vi.stubGlobal('WebSocket', FakeSocket)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Anfragen mit Sitzungstoken', () => {
  test('vor der ersten Anfrage wird angemeldet', async () => {
    // Arrange
    const fetched = vi.fn(() => Promise.resolve(respond(200, '{"hostname":"PC"}')))
    vi.stubGlobal('fetch', fetched)

    const credentials = deferredCredentials()
    const pending = directTransport(DEVICE, credentials).control({ path: '/api/info' })

    // Act
    credentials.deliver('sitzung-1')
    await pending

    // Assert
    expect(fetched).toHaveBeenCalledWith(
      'https://pc.example.ts.net:8443/api/info',
      expect.objectContaining({ headers: { Authorization: 'Bearer sitzung-1' } }),
    )
  })

  test('eine abgelaufene Sitzung wird still erneuert', async () => {
    // Arrange — der Agent lehnt das alte Token ab, das zweite geht durch.
    const fetched = vi
      .fn<(url: string, options: RequestInit) => Promise<Response>>()
      .mockResolvedValueOnce(respond(401, '{"error":"Nicht angemeldet."}'))
      .mockResolvedValueOnce(respond(200, '{"hostname":"PC"}'))

    vi.stubGlobal('fetch', fetched)

    // Jede Anmeldung liefert ein neues Token — daran lässt sich ablesen, ob
    // tatsächlich neu angemeldet wurde.
    let issued = 0
    let current: string | undefined

    const credentials: Credentials = {
      peek: () => current,
      obtain: () => {
        current = `sitzung-${++issued}`
        return Promise.resolve(current)
      },
      invalidate: () => {
        current = undefined
      },
    }

    // Act
    const info = await directTransport(DEVICE, credentials).control({ path: '/api/info' })

    // Assert — nach zwölf Stunden darf der Nutzer davon nichts merken.
    expect(info).toEqual({ hostname: 'PC' })
    expect(fetched).toHaveBeenCalledTimes(2)
    expect(fetched.mock.calls[1]?.[1]).toMatchObject({
      headers: { Authorization: 'Bearer sitzung-2' },
    })
  })

  test('ein dauerhaft abgelehnter Ausweis endet nach dem zweiten Versuch', async () => {
    // Arrange
    const fetched = vi.fn(() => Promise.resolve(respond(401, '{"error":"Nicht angemeldet."}')))
    vi.stubGlobal('fetch', fetched)

    const credentials: Credentials = {
      peek: () => 'immer-falsch',
      obtain: () => Promise.resolve('immer-falsch'),
      invalidate: () => {},
    }

    // Act
    const failure = await directTransport(DEVICE, credentials)
      .control({ path: '/api/info' })
      .catch((error: unknown) => error)

    // Assert — sonst würde daraus eine Schleife gegen den Agent.
    expect(failure).toBeInstanceOf(TransportError)
    expect((failure as TransportError).status).toBe(401)
    expect(fetched).toHaveBeenCalledTimes(2)
  })

  test('andere Fehler werden nicht wiederholt', async () => {
    // Arrange
    const fetched = vi.fn(() => Promise.resolve(respond(500, '{"error":"kaputt"}')))
    vi.stubGlobal('fetch', fetched)

    const credentials: Credentials = {
      peek: () => 'sitzung',
      obtain: () => Promise.resolve('sitzung'),
      invalidate: () => {},
    }

    // Act
    await directTransport(DEVICE, credentials)
      .control({ path: '/api/info' })
      .catch(() => undefined)

    // Assert
    expect(fetched).toHaveBeenCalledTimes(1)
  })
})

describe('Kanäle mit Sitzungstoken', () => {
  test('der Socket wartet auf die Anmeldung', async () => {
    // Arrange
    const credentials = deferredCredentials()

    // Act
    const channel = directTransport(DEVICE, credentials).inputChannel({})

    // Assert — vorher darf nichts geöffnet werden, sonst läuft die Verbindung
    // ohne Ausweis in ein 401.
    expect(FakeSocket.instances).toHaveLength(0)
    expect(channel.isOpen).toBe(false)

    // Act
    credentials.deliver('sitzung-1')
    await vi.waitFor(() => expect(FakeSocket.instances).toHaveLength(1))

    // Assert
    expect(FakeSocket.instances[0]?.url).toBe(
      'wss://pc.example.ts.net:8443/ws/input?token=sitzung-1',
    )
  })

  test('bis dahin wird nichts gesendet', () => {
    // Arrange
    const channel = directTransport(DEVICE, deferredCredentials()).inputChannel({})

    // Act
    channel.send('{"t":"click"}')

    // Assert — ein gepufferter Klick träfe später auf ein anderes Bild.
    expect(FakeSocket.instances).toHaveLength(0)
  })

  test('ein vorher geschlossener Kanal verbindet sich nicht mehr', async () => {
    // Arrange — der Nutzer wischt weg, während die Anmeldung noch läuft.
    const credentials = deferredCredentials()
    const channel = directTransport(DEVICE, credentials).screenStream(1, {})

    // Act
    channel.close()
    credentials.deliver('sitzung-1')
    await Promise.resolve()

    // Assert — sonst bliebe ein Socket offen, dem niemand mehr zuhört.
    expect(FakeSocket.instances).toHaveLength(0)
  })

  test('eine gescheiterte Anmeldung meldet sich als Störung', async () => {
    // Arrange
    const events: string[] = []
    const credentials: Credentials = {
      peek: () => undefined,
      obtain: () => Promise.reject(new Error('nicht erreichbar')),
      invalidate: () => {},
    }

    // Act
    directTransport(DEVICE, credentials).inputChannel({ onError: () => events.push('error') })
    await vi.waitFor(() => expect(events).toEqual(['error']))

    // Assert — ohne das wartete die Statusanzeige ewig auf „verbunden".
    expect(FakeSocket.instances).toHaveLength(0)
  })
})
