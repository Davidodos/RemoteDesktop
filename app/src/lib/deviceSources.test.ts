import { describe, expect, test } from 'vitest'
import { collectDevices, parseDevices, type DeviceSource } from './deviceSources.ts'
import type { Device } from './types.ts'

/**
 * Die Geräteliste. Ein kaputter Eintrag darf nie die ganze Liste kosten — was
 * dort steht, hat eine frühere Fassung der App hinterlassen.
 */
describe('gespeicherte Geräte lesen', () => {
  test('ein gekoppeltes Gerät braucht kein Token', () => {
    // Arrange — seit Phase 10 ist die Kennung des Clients der Ausweis.
    const raw = JSON.stringify([
      { id: 'abc', name: 'PC', host: 'pc.ts.net', port: 8443, clientId: 'handy-1' },
    ])

    // Act
    const devices = parseDevices(raw)

    // Assert
    expect(devices).toEqual([
      { id: 'abc', name: 'PC', host: 'pc.ts.net', port: 8443, clientId: 'handy-1', canWake: false },
    ])
  })

  test('ein Gerät mit altem Token bleibt gültig', () => {
    // Arrange — bis Phase 12 muss der alte Weg offenbleiben.
    const raw = JSON.stringify([
      { id: 'pc', name: 'PC', host: 'pc.ts.net', port: 8443, token: 'geheim', canWake: true },
    ])

    // Assert
    expect(parseDevices(raw)[0]).toMatchObject({ token: 'geheim', canWake: true })
  })

  test('ohne jeden Ausweis fliegt der Eintrag raus', () => {
    // Arrange — sonst käme die App bis zur ersten Anfrage und stünde vor einem
    // 401, das wie ein Fehler des Agents aussieht.
    const raw = JSON.stringify([{ id: 'pc', name: 'PC', host: 'pc.ts.net', port: 8443 }])

    // Assert
    expect(parseDevices(raw)).toEqual([])
  })

  test('der Fingerabdruck des Agents kommt mit', () => {
    // Arrange
    const raw = JSON.stringify([
      {
        id: 'abc',
        host: 'pc.ts.net',
        port: 8443,
        clientId: 'handy-1',
        fingerprint: 'abc123',
      },
    ])

    // Assert
    expect(parseDevices(raw)[0]).toMatchObject({ fingerprint: 'abc123', name: 'abc' })
  })

  test('kaputte Einträge kosten nur sich selbst', () => {
    // Arrange
    const raw = JSON.stringify([
      { id: '', host: 'x', port: 8443, clientId: 'a' },
      { id: 'ok', host: 'pc.ts.net', port: 8443, clientId: 'a' },
      { id: 'schlechterPort', host: 'pc.ts.net', port: 0, clientId: 'a' },
      'gar kein Objekt',
    ])

    // Assert
    expect(parseDevices(raw).map((device) => device.id)).toEqual(['ok'])
  })

  test('unlesbarer Speicher ergibt eine leere Liste', () => {
    // Assert
    expect(parseDevices(undefined)).toEqual([])
    expect(parseDevices('{kein JSON')).toEqual([])
    expect(parseDevices('{"kein":"Array"}')).toEqual([])
  })
})

describe('Quellen zusammenlegen', () => {
  test('die erste Quelle gewinnt bei gleicher Kennung', async () => {
    // Arrange — ein selbst gekoppeltes Gerät bringt eigene Zugangsdaten mit und
    // soll nicht von einem alten Hub-Eintrag überschrieben werden.
    const local = source('lokal', [device('pc', { clientId: 'handy-1' })])
    const hub = source('hub', [device('pc', { token: 'alt' })])

    // Act
    const { devices } = await collectDevices([local, hub])

    // Assert
    expect(devices).toHaveLength(1)
    expect(devices[0]?.clientId).toBe('handy-1')
  })

  test('eine ausgefallene Quelle beendet das Einsammeln nicht', async () => {
    // Arrange
    const local = source('lokal', [device('pc', { clientId: 'handy-1' })])
    const broken: DeviceSource = {
      id: 'hub',
      list: () => Promise.reject(new Error('NAS aus')),
    }

    // Act
    const { devices, failures } = await collectDevices([local, broken])

    // Assert — fällt der Hub aus, bleiben die gekoppelten Geräte bedienbar.
    expect(devices).toHaveLength(1)
    expect(failures).toHaveLength(1)
  })
})

function source(id: string, devices: Device[]): DeviceSource {
  return { id, list: () => Promise.resolve(devices) }
}

function device(id: string, extra: Partial<Device>): Device {
  return { id, name: id, host: `${id}.ts.net`, port: 8443, canWake: false, ...extra }
}
