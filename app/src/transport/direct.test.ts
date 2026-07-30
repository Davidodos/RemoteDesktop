import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import type { Device } from '../lib/types.ts'
import { directTransport } from './direct.ts'
import { TransportError } from './index.ts'

const DEVICE: Device = {
  id: 'pc',
  name: 'PC',
  host: 'pc.example.ts.net',
  port: 8443,
  token: 'ge heim',
  canWake: true,
}

/** WebSocket-Ersatz, der nichts verbindet, sondern nur mitschreibt. */
class FakeSocket {
  static readonly OPEN = 1
  static instances: FakeSocket[] = []

  readyState = 0
  binaryType = 'blob'
  sent: string[] = []
  closed = false

  private readonly listeners = new Map<string, ((event: unknown) => void)[]>()

  constructor(readonly url: string) {
    FakeSocket.instances.push(this)
  }

  static get last(): FakeSocket {
    const socket = FakeSocket.instances.at(-1)

    if (socket === undefined) {
      throw new Error('Es wurde keine Verbindung geöffnet.')
    }

    return socket
  }

  addEventListener(type: string, handler: (event: unknown) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), handler])
  }

  send(payload: string): void {
    this.sent.push(payload)
  }

  close(): void {
    this.closed = true
    this.readyState = 3
    this.emit('close', {})
  }

  accept(): void {
    this.readyState = FakeSocket.OPEN
    this.emit('open', {})
  }

  deliver(data: unknown): void {
    this.emit('message', { data })
  }

  private emit(type: string, event: unknown): void {
    for (const handler of this.listeners.get(type) ?? []) {
      handler(event)
    }
  }
}

/** Antwort-Attrappe mit genau den Feldern, die der Transport anfasst. */
function respond(status: number, body: string): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(body),
    json: () => Promise.resolve(JSON.parse(body) as unknown),
  } as unknown as Response
}

function fetchMock(response: Response | Error): ReturnType<typeof vi.fn> {
  return vi.fn(() =>
    response instanceof Error ? Promise.reject(response) : Promise.resolve(response),
  )
}

beforeEach(() => {
  FakeSocket.instances = []
  vi.stubGlobal('WebSocket', FakeSocket)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Anfragen an den Agent', () => {
  test('Ziel und Berechtigung stehen im Aufruf', async () => {
    // Arrange
    const fetched = fetchMock(respond(200, '{"hostname":"PC"}'))
    vi.stubGlobal('fetch', fetched)

    // Act
    const info = await directTransport(DEVICE).control({ path: '/api/info' })

    // Assert
    expect(fetched).toHaveBeenCalledWith(
      'https://pc.example.ts.net:8443/api/info',
      expect.objectContaining({
        method: 'GET',
        headers: { Authorization: 'Bearer ge heim' },
      }),
    )
    expect(info).toEqual({ hostname: 'PC' })
  })

  test('ein Rumpf geht als JSON mit', async () => {
    // Arrange
    const fetched = fetchMock(respond(200, '{}'))
    vi.stubGlobal('fetch', fetched)

    // Act
    await directTransport(DEVICE).control({
      path: '/api/power',
      method: 'POST',
      body: { action: 'lock' },
    })

    // Assert
    expect(fetched.mock.calls[0]?.[1]).toMatchObject({
      method: 'POST',
      body: '{"action":"lock"}',
      headers: { 'Content-Type': 'application/json' },
    })
  })

  test('eine Antwort ohne Inhalt ist kein Fehler', async () => {
    // Arrange — so antwortet der Agent auf DELETE.
    vi.stubGlobal('fetch', fetchMock(respond(204, '')))

    // Act
    const result = await directTransport(DEVICE).control({
      path: '/api/webrtc/abc',
      method: 'DELETE',
    })

    // Assert
    expect(result).toBeUndefined()
  })
})

describe('Fehler auf dem Weg zum Agent', () => {
  test('kommt keine Verbindung zustande, bleibt der Status offen', async () => {
    // Arrange
    vi.stubGlobal('fetch', fetchMock(new Error('nicht erreichbar')))

    // Act
    const failure = await directTransport(DEVICE)
      .control({ path: '/api/info' })
      .catch((error: unknown) => error)

    // Assert — nur so kann der Aufrufer „Rechner aus" von „Agent meckert"
    // unterscheiden.
    expect(failure).toBeInstanceOf(TransportError)
    expect((failure as TransportError).status).toBeUndefined()
  })

  test('die Klartextmeldung des Agents kommt durch', async () => {
    // Arrange
    vi.stubGlobal('fetch', fetchMock(respond(500, '{"error":"ffmpeg fehlt."}')))

    // Act
    const failure = (await directTransport(DEVICE)
      .control({ path: '/api/info' })
      .catch((error: unknown) => error)) as TransportError

    // Assert
    expect(failure.status).toBe(500)
    expect(failure.serverMessage).toBe('ffmpeg fehlt.')
  })

  test('ohne JSON im Fehler bleibt nur der Statuscode', async () => {
    // Arrange
    vi.stubGlobal('fetch', fetchMock(respond(401, 'nope')))

    // Act
    const failure = (await directTransport(DEVICE)
      .control({ path: '/api/info' })
      .catch((error: unknown) => error)) as TransportError

    // Assert
    expect(failure.status).toBe(401)
    expect(failure.serverMessage).toBeUndefined()
  })
})

describe('Adressen für den Browser', () => {
  test('das Token hängt kodiert hinten dran', () => {
    // Act
    const url = directTransport(DEVICE).resourceUrl('/api/media/thumbnail', {
      session: 'Spotify.exe',
      v: 'Lied 1',
    })

    // Assert
    expect(url).toBe(
      'https://pc.example.ts.net:8443/api/media/thumbnail' +
        '?session=Spotify.exe&v=Lied%201&token=ge%20heim',
    )
  })
})

describe('Kanäle', () => {
  test('der Eingabekanal geht an /ws/input', () => {
    // Act
    directTransport(DEVICE).inputChannel({})

    // Assert
    expect(FakeSocket.last.url).toBe('wss://pc.example.ts.net:8443/ws/input?token=ge%20heim')
  })

  test('der Bildkanal nimmt den Monitor mit und erwartet Binärdaten', () => {
    // Act
    directTransport(DEVICE).screenStream(2, {})

    // Assert
    expect(FakeSocket.last.url).toBe(
      'wss://pc.example.ts.net:8443/ws/screen?monitor=2&token=ge%20heim',
    )
    expect(FakeSocket.last.binaryType).toBe('arraybuffer')
  })

  test('vor dem Verbinden wird nichts gesendet', () => {
    // Arrange
    const channel = directTransport(DEVICE).inputChannel({})

    // Act
    channel.send('{"t":"click"}')

    // Assert — ein gepufferter Klick träfe später auf ein anderes Bild.
    expect(FakeSocket.last.sent).toEqual([])
    expect(channel.isOpen).toBe(false)
  })

  test('nach dem Verbinden geht es raus', () => {
    // Arrange
    const channel = directTransport(DEVICE).inputChannel({})
    FakeSocket.last.accept()

    // Act
    channel.send('{"t":"click"}')

    // Assert
    expect(FakeSocket.last.sent).toEqual(['{"t":"click"}'])
    expect(channel.isOpen).toBe(true)
  })

  test('Text und Binärdaten landen bei verschiedenen Rückrufen', () => {
    // Arrange
    const texts: string[] = []
    const binaries: ArrayBuffer[] = []

    directTransport(DEVICE).screenStream(0, {
      onText: (data) => texts.push(data),
      onBinary: (data) => binaries.push(data),
    })

    // Act
    FakeSocket.last.deliver('{"t":"meta"}')
    FakeSocket.last.deliver(new ArrayBuffer(8))

    // Assert
    expect(texts).toEqual(['{"t":"meta"}'])
    expect(binaries).toHaveLength(1)
  })

  test('Öffnen, Schließen und Störungen werden gemeldet', () => {
    // Arrange
    const events: string[] = []

    directTransport(DEVICE).inputChannel({
      onOpen: () => events.push('open'),
      onClose: () => events.push('close'),
      onError: () => events.push('error'),
    })

    // Act
    FakeSocket.last.accept()
    FakeSocket.last.close()

    // Assert
    expect(events).toEqual(['open', 'close'])
  })
})
