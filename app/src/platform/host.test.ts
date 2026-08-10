import { describe, expect, it, vi } from 'vitest'
import { capacitorPlatform } from './capacitor.ts'
import { noHost } from './host.ts'

/**
 * Die Brücke zum Host-Plugin.
 *
 * Geprüft wird vor allem der Fall, den man am Gerät nicht mehr reparieren kann:
 * eine APK, die noch ohne dieses Plugin gebaut wurde. Sie muss weiterlaufen und
 * die Freigabe gar nicht erst anbieten — statt beim Öffnen der Einstellungen an
 * einem Aufruf ins Leere zu scheitern.
 */
describe('noHost', () => {
  it('meldet, dass es hier nicht geht', () => {
    expect(noHost.available).toBe(false)
  })

  it('sagt beim Status nichts Falsches, statt zu scheitern', async () => {
    await expect(noHost.status()).resolves.toEqual({
      running: false,
      deviceName: '',
      port: 0,
      addresses: [],
    })
  })

  it('lehnt das Einschalten mit einem lesbaren Satz ab', async () => {
    await expect(noHost.start()).rejects.toThrow(/nicht steuerbar machen/)
  })

  it('hat keine berechtigten Geräte statt eines Fehlers', async () => {
    await expect(noHost.clients()).resolves.toEqual([])
  })
})

describe('capacitorPlatform.host', () => {
  const plugins = () => ({
    preferences: { get: vi.fn(), set: vi.fn(), remove: vi.fn() },
    clipboard: { read: vi.fn(), write: vi.fn() },
    barcode: { scanBarcode: vi.fn() },
    session: { begin: vi.fn(), end: vi.fn() },
  })

  it('bleibt ohne Plugin bei noHost', () => {
    const platform = capacitorPlatform(plugins() as never, new Map())

    expect(platform.host.available).toBe(false)
  })

  it('reicht Start, Stopp und Code an das Plugin durch', async () => {
    const status = {
      running: true,
      deviceName: 'Pixel',
      port: 8443,
      addresses: ['192.168.178.31'],
      caFingerprint: 'ab12',
    }

    const host = {
      status: vi.fn().mockResolvedValue(status),
      start: vi.fn().mockResolvedValue(status),
      stop: vi.fn().mockResolvedValue({ ...status, running: false }),
      pairingCode: vi.fn().mockResolvedValue({
        code: '123456',
        expiresInSeconds: 300,
        pairingUri: 'remotedesktop://pair?host=192.168.178.31&port=8443&code=123456',
      }),
      clients: vi.fn().mockResolvedValue({ clients: [{ id: 'a', label: 'PC', scopes: [] }] }),
      revoke: vi.fn().mockResolvedValue(undefined),
    }

    const platform = capacitorPlatform({ ...plugins(), host } as never, new Map())

    expect(platform.host.available).toBe(true)
    await expect(platform.host.start()).resolves.toEqual(status)
    await expect(platform.host.stop()).resolves.toMatchObject({ running: false })
    await expect(platform.host.pairingCode()).resolves.toMatchObject({ code: '123456' })

    // Die Liste kommt drüben in einem Umschlag an und wird hier ausgepackt —
    // die Oberfläche soll sich nicht mit der Form der Brücke befassen.
    await expect(platform.host.clients()).resolves.toEqual([
      { id: 'a', label: 'PC', scopes: [] },
    ])

    await platform.host.revoke('a')

    expect(host.revoke).toHaveBeenCalledWith({ id: 'a' })
  })
})
