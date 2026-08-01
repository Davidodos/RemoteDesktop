import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import type { Channel, ChannelHandlers, Transport } from '../transport/index.ts'
import { InputChannel } from './inputChannel.ts'
import type { Device } from './types.ts'

const DEVICE: Device = {
  id: 'pc',
  name: 'PC',
  host: 'pc.example.ts.net',
  port: 8443,
  clientId: 'handy-1',
  canWake: false,
}

/**
 * Ein Transport, der jeden geöffneten Kanal festhält, statt zu verbinden.
 *
 * Er zählt mit, wie oft er zum Neu-Anmelden aufgefordert wurde — genau das ist
 * die Frage dieser Datei: meldet sich der Client nach einem Abriss neu an, oder
 * redet er mit einem Token weiter, das der Agent nach seinem Neustart gar nicht
 * mehr kennt?
 */
class FakeTransport implements Transport {
  readonly kanaele: { handlers: ChannelHandlers; geschlossen: boolean }[] = []
  neuAnmeldungen = 0

  control<T>(): Promise<T> {
    throw new Error('Wird hier nicht gebraucht.')
  }

  resourceUrl(): string {
    return ''
  }

  screenStream(): Channel {
    throw new Error('Wird hier nicht gebraucht.')
  }

  reauthenticate(): void {
    this.neuAnmeldungen += 1
  }

  inputChannel(handlers: ChannelHandlers): Channel {
    const eintrag = { handlers, geschlossen: false }
    this.kanaele.push(eintrag)

    return {
      send: () => undefined,
      close: () => {
        eintrag.geschlossen = true
      },
      get isOpen(): boolean {
        return !eintrag.geschlossen
      },
    }
  }

  /** Der zuletzt geöffnete Kanal kommt zustande. */
  akzeptieren(): void {
    this.letzter.handlers.onOpen?.()
  }

  /** Der zuletzt geöffnete Kanal fällt weg — so sieht ein Agent-Neustart aus. */
  abreissen(): void {
    this.letzter.handlers.onClose?.()
  }

  private get letzter(): { handlers: ChannelHandlers } {
    const kanal = this.kanaele.at(-1)

    if (kanal === undefined) {
      throw new Error('Es wurde kein Kanal geöffnet.')
    }

    return kanal
  }
}

describe('InputChannel — Wiederverbinden nach einem Agent-Neustart', () => {
  let transport: FakeTransport
  let fehler: string[]
  let zustaende: string[]
  let channel: InputChannel

  beforeEach(() => {
    vi.useFakeTimers()

    transport = new FakeTransport()
    fehler = []
    zustaende = []

    channel = new InputChannel(
      DEVICE,
      (state) => zustaende.push(state),
      (message) => fehler.push(message),
      transport,
    )
  })

  afterEach(() => {
    channel.disconnect()
    vi.useRealTimers()
  })

  test('nach einem Abriss wird erneut verbunden', () => {
    channel.connect()
    transport.akzeptieren()
    transport.abreissen()

    vi.advanceTimersByTime(500)

    expect(transport.kanaele).toHaveLength(2)
  })

  /**
   * Der Kernpunkt: der zweite Versuch kommt nicht zustande, weil der Agent das
   * alte Sitzungstoken nach seinem Neustart nicht mehr kennt. Ein WebSocket
   * bekommt dafür keinen Statuscode — er wird einfach geschlossen. Also muss
   * der Client daraus schließen, dass sein Ausweis abgelaufen ist.
   */
  test('ein Versuch, der nie zustande kam, führt zu einer neuen Anmeldung', () => {
    channel.connect()
    transport.akzeptieren()
    transport.abreissen()

    expect(transport.neuAnmeldungen).toBe(0)

    vi.advanceTimersByTime(500)
    transport.abreissen()

    expect(transport.neuAnmeldungen).toBe(1)
  })

  test('eine stehende Verbindung wird nicht grundlos neu angemeldet', () => {
    channel.connect()
    transport.akzeptieren()

    for (let runde = 0; runde < 3; runde++) {
      transport.abreissen()
      vi.advanceTimersByTime(10_000)
      transport.akzeptieren()
    }

    // Jeder Versuch kam zustande — das Token gilt also noch. Eine Anmeldung
    // pro Abriss wäre eine unnötige Runde und bei jedem WLAN-Zucken spürbar.
    expect(transport.neuAnmeldungen).toBe(0)
  })

  test('nach der neuen Anmeldung steht die Verbindung wieder', () => {
    channel.connect()
    transport.akzeptieren()
    transport.abreissen()

    vi.advanceTimersByTime(500)
    transport.abreissen()

    vi.advanceTimersByTime(1000)
    transport.akzeptieren()

    expect(zustaende.at(-1)).toBe('connected')
    expect(fehler).toEqual([])
  })

  test('die Wiederholversuche geben irgendwann auf', () => {
    channel.connect()
    transport.akzeptieren()

    // Zehn Versuche mit Verdopplung bis 8 s — zwei Minuten decken sie sicher ab.
    for (let runde = 0; runde < 20; runde++) {
      transport.abreissen()
      vi.advanceTimersByTime(10_000)
    }

    expect(transport.kanaele.length).toBeLessThanOrEqual(11)
    expect(fehler.at(-1)).toContain('meldet sich nicht mehr')
  })

  test('nach dem Aufgeben wird nicht weiter versucht', () => {
    channel.connect()
    transport.akzeptieren()

    for (let runde = 0; runde < 20; runde++) {
      transport.abreissen()
      vi.advanceTimersByTime(10_000)
    }

    const stand = transport.kanaele.length

    vi.advanceTimersByTime(120_000)

    expect(transport.kanaele).toHaveLength(stand)
  })

  /**
   * Ein Netzwechsel ist eine neue Lage — WLAN weg, Mobilfunk da. Dass vorher
   * zehnmal nichts ging, sagt darüber nichts aus.
   */
  test('ein Netzwechsel setzt den Zähler zurück', () => {
    channel.connect()
    transport.akzeptieren()

    for (let runde = 0; runde < 20; runde++) {
      transport.abreissen()
      vi.advanceTimersByTime(10_000)
    }

    const aufgegeben = transport.kanaele.length

    window.dispatchEvent(new Event('online'))

    expect(transport.kanaele.length).toBeGreaterThan(aufgegeben)
  })

  test('ein selbst geschlossener Kanal wird nicht wieder aufgebaut', () => {
    channel.connect()
    transport.akzeptieren()
    channel.disconnect()

    vi.advanceTimersByTime(60_000)

    expect(transport.kanaele).toHaveLength(1)
    expect(transport.neuAnmeldungen).toBe(0)
  })
})
