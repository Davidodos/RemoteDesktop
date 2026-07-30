import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { InputChannel } from './inputChannel.ts'
import type { Device } from './types.ts'

const DEVICE: Device = {
  id: 'pc',
  name: 'PC',
  host: 'pc.example.ts.net',
  port: 8443,
  token: 'geheim',
  canWake: true,
}

/** WebSocket-Ersatz, der nichts verbindet, sondern nur mitschreibt. */
class FakeSocket {
  static readonly OPEN = 1
  static instances: FakeSocket[] = []

  readyState = 0
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

  /** Simuliert die zustande gekommene Verbindung. */
  accept(): void {
    this.readyState = FakeSocket.OPEN
    this.emit('open', {})
  }

  receive(payload: unknown): void {
    this.emit('message', { data: JSON.stringify(payload) })
  }

  private emit(type: string, event: unknown): void {
    for (const handler of this.listeners.get(type) ?? []) {
      handler(event)
    }
  }
}

/** Gesammelte Frame-Callbacks, damit Tests den Zeitpunkt selbst bestimmen. */
let frames: FrameRequestCallback[] = []

function runFrame(): void {
  const pending = frames
  frames = []

  for (const callback of pending) {
    callback(0)
  }
}

function sentPayloads(): Record<string, unknown>[] {
  return FakeSocket.last.sent.map((raw) => JSON.parse(raw) as Record<string, unknown>)
}

function connected(): InputChannel {
  const channel = new InputChannel(DEVICE, () => {}, () => {})
  channel.connect()
  FakeSocket.last.accept()

  return channel
}

beforeEach(() => {
  FakeSocket.instances = []
  frames = []

  vi.stubGlobal('WebSocket', FakeSocket)
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    frames.push(callback)
    return frames.length
  })
  vi.stubGlobal('cancelAnimationFrame', (handle: number) => {
    frames = frames.filter((_, index) => index !== handle - 1)
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Verbindungsaufbau', () => {
  test('Token und Ziel stehen in der URL', () => {
    // Act
    connected()

    // Assert
    expect(FakeSocket.last.url).toBe(
      'wss://pc.example.ts.net:8443/ws/input?token=geheim')
  })

  test('nach dem Trennen wird nicht erneut verbunden', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.disconnect()

    // Assert
    expect(FakeSocket.instances).toHaveLength(1)
  })
})

describe('Bewegungen zusammenfassen', () => {
  test('mehrere Positionen pro Frame ergeben nur die letzte', () => {
    // Arrange
    const channel = connected()

    // Act — so schnell liefert ein Display die Fingerposition.
    channel.moveTo(0, 0.1, 0.1)
    channel.moveTo(0, 0.2, 0.2)
    channel.moveTo(0, 0.3, 0.3)
    runFrame()

    // Assert
    expect(sentPayloads()).toEqual([{ t: 'move', monitor: 0, x: 0.3, y: 0.3 }])
  })

  test('relative Bewegungen werden aufaddiert statt verworfen', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.moveBy(5, 5)
    channel.moveBy(3, -2)
    runFrame()

    // Assert — sonst ginge ein Teil der Wischstrecke verloren.
    expect(sentPayloads()).toEqual([{ t: 'moverel', dx: 8, dy: 3 }])
  })

  test('nach dem Senden beginnt ein neues Fenster', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.moveBy(5, 0)
    runFrame()
    channel.moveBy(2, 0)
    runFrame()

    // Assert
    expect(sentPayloads()).toEqual([
      { t: 'moverel', dx: 5, dy: 0 },
      { t: 'moverel', dx: 2, dy: 0 },
    ])
  })
})

describe('Reihenfolge der Befehle', () => {
  test('ein Klick kommt nie vor der Bewegung an, die ihn positioniert', () => {
    // Arrange
    const channel = connected()

    // Act — genau diese Folge löst ein Tippen auf das Bildschirmbild aus.
    channel.moveTo(0, 0.75, 0.25)
    channel.click('left')

    // Assert
    expect(sentPayloads()).toEqual([
      { t: 'move', monitor: 0, x: 0.75, y: 0.25 },
      { t: 'click', button: 'left' },
    ])
  })

  test('die vorgezogene Bewegung wird nicht doppelt gesendet', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.moveTo(0, 0.5, 0.5)
    channel.click('left')
    runFrame()

    // Assert
    expect(sentPayloads()).toHaveLength(2)
  })

  test('Tastenbefehle ziehen wartende Bewegungen ebenfalls mit', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.moveBy(4, 4)
    channel.combo('c', ['ctrl'])

    // Assert
    expect(sentPayloads()[0]).toEqual({ t: 'moverel', dx: 4, dy: 4 })
  })
})

describe('Verbindung nicht offen', () => {
  test('Eingaben werden verworfen statt gepuffert', () => {
    // Arrange — Verbindung wird nie angenommen.
    const channel = new InputChannel(DEVICE, () => {}, () => {})
    channel.connect()

    // Act
    channel.click('left')

    // Assert — ein zehn Sekunden später nachgereichter Klick trifft auf einen
    // völlig anderen Bildschirminhalt.
    expect(FakeSocket.last.sent).toEqual([])
  })

  test('beim Trennen wartende Bewegungen fallen weg', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.moveBy(10, 10)
    channel.disconnect()
    runFrame()

    // Assert
    expect(FakeSocket.last.sent).toEqual([])
  })
})

describe('Rückmeldungen des Agents', () => {
  test('Fehlermeldungen erreichen die Oberfläche', () => {
    // Arrange
    const errors: string[] = []
    const channel = new InputChannel(DEVICE, () => {}, (message) => errors.push(message))
    channel.connect()
    FakeSocket.last.accept()

    // Act
    FakeSocket.last.receive({ t: 'error', message: 'Unbekannte Taste.' })

    // Assert
    expect(errors).toEqual(['Unbekannte Taste.'])
  })

  test('unverständliche Antworten kosten nicht die Verbindung', () => {
    // Arrange
    const errors: string[] = []
    const channel = new InputChannel(DEVICE, () => {}, (message) => errors.push(message))
    channel.connect()
    FakeSocket.last.accept()

    // Act
    FakeSocket.last.receive('kein Objekt')
    channel.click('left')

    // Assert
    expect(errors).toEqual([])
    expect(FakeSocket.last.sent).toHaveLength(1)
  })

  test('Zustandswechsel werden gemeldet', () => {
    // Arrange
    const states: string[] = []
    const channel = new InputChannel(DEVICE, (state) => states.push(state), () => {})

    // Act
    channel.connect()
    FakeSocket.last.accept()
    channel.disconnect()

    // Assert
    expect(states).toEqual(['connecting', 'connected', 'disconnected'])
  })
})

describe('Frei zusammengestellte Tastenkombination', () => {
  test('Tasten gehen der Reihe nach runter und rückwärts wieder hoch', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.chord(['ctrl', 'shift', 'escape'])

    // Assert
    expect(sentPayloads()).toEqual([
      { t: 'keydown', key: 'ctrl' },
      { t: 'keydown', key: 'shift' },
      { t: 'keydown', key: 'escape' },
      { t: 'keyup', key: 'escape' },
      { t: 'keyup', key: 'shift' },
      { t: 'keyup', key: 'ctrl' },
    ])
  })

  test('doppelt aufgenommene Tasten bleiben nicht gedrückt hängen', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.chord(['ctrl', 'ctrl', 'a'])

    // Assert — zu jedem keydown gehört genau ein keyup.
    expect(sentPayloads()).toEqual([
      { t: 'keydown', key: 'ctrl' },
      { t: 'keydown', key: 'a' },
      { t: 'keyup', key: 'a' },
      { t: 'keyup', key: 'ctrl' },
    ])
  })

  test('eine leere Kombination schickt gar nichts', () => {
    // Arrange
    const channel = connected()

    // Act
    channel.chord([])

    // Assert
    expect(sentPayloads()).toEqual([])
  })
})
