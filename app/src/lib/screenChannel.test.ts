import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { ScreenChannel } from './screenChannel.ts'
import type { ChannelHandlers, Transport } from '../transport/index.ts'
import type { Device } from './types.ts'

const DEVICE: Device = { id: 'pixel', name: 'Pixel', host: 'pixel', port: 8443, clientId: 'x', canWake: false }

/** Ein Transport, der nur Bild-Sockets öffnet und mitzählt. */
function fakeTransport(): { transport: Transport; opened: ChannelHandlers[] } {
  const opened: ChannelHandlers[] = []

  const transport = {
    screenStream: (_monitor: number, handlers: ChannelHandlers) => {
      opened.push(handlers)

      let open = true

      return {
        get isOpen() {
          return open
        },
        send: () => undefined,
        close: () => {
          open = false
        },
      }
    },
  } as unknown as Transport

  return { transport, opened }
}

function channelWith(transport: Transport): ScreenChannel {
  return new ScreenChannel(DEVICE, 0, {
    onAwaiting: () => undefined,
    onMeta: () => undefined,
    onStats: () => undefined,
    onState: () => undefined,
    onError: () => undefined,
    onAvailability: () => undefined,
  } as unknown as ConstructorParameters<typeof ScreenChannel>[2], transport)
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

/**
 * Der Wächter baut eine stumme Verbindung nach sechs Sekunden neu auf. Solange
 * drüben gefragt wird, ist Stille aber kein Abbruch: die Karte und der
 * Systemdialog brauchen, solange jemand liest. Riss der Wächter dort ab, vergaß
 * der Host die Zustimmung, und am Handy kam alles zweimal (18.09.2026).
 */
describe('ScreenChannel beim Warten auf die Zustimmung', () => {
  test('baut nicht neu auf, solange gefragt wird', () => {
    const { transport, opened } = fakeTransport()
    const channel = channelWith(transport)

    channel.connect()
    opened[0]!.onOpen?.()
    opened[0]!.onText?.(JSON.stringify({ t: 'awaiting' }))

    vi.advanceTimersByTime(30_000)

    expect(opened).toHaveLength(1)

    channel.disconnect()
  })

  test('nach der Antwort gilt der Wächter wieder', () => {
    const { transport, opened } = fakeTransport()
    const channel = channelWith(transport)

    channel.connect()
    opened[0]!.onOpen?.()
    opened[0]!.onText?.(JSON.stringify({ t: 'awaiting' }))
    opened[0]!.onText?.(JSON.stringify({ t: 'stats', fps: 0 }))

    vi.advanceTimersByTime(10_000)

    expect(opened.length).toBeGreaterThan(1)

    channel.disconnect()
  })
})
