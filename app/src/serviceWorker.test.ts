import { afterEach, describe, expect, test, vi } from 'vitest'
import { registerServiceWorker, removeServiceWorker } from './serviceWorker.ts'

/**
 * Der Service Worker gehört in den Browser und nicht in die APK.
 *
 * Der Befund, den das verhindert: die APK lief nach jedem Update eine
 * Startphase hinterher — neue Versionsnummer, alte Oberfläche, und erst beim
 * zweiten Start war alles da.
 */

interface FakeRegistration {
  unregister: () => Promise<boolean>
}

function stubServiceWorker(registrations: FakeRegistration[]): {
  register: ReturnType<typeof vi.fn>
} {
  const register = vi.fn(() => Promise.resolve())

  vi.stubGlobal('navigator', {
    serviceWorker: {
      register,
      getRegistrations: () => Promise.resolve(registrations),
    },
  })

  return { register }
}

function stubCaches(names: string[]): { deleted: string[] } {
  const deleted: string[] = []

  vi.stubGlobal('caches', {
    keys: () => Promise.resolve(names),
    delete: (name: string) => {
      deleted.push(name)

      return Promise.resolve(true)
    },
  })

  return { deleted }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('im Browser', () => {
  test('wird der Worker angemeldet', () => {
    const { register } = stubServiceWorker([])

    registerServiceWorker()

    expect(register).toHaveBeenCalledWith('/sw.js')
  })

  test('geht es auch ohne Unterstützung weiter', () => {
    vi.stubGlobal('navigator', {})

    // Kein Wurf: eine Seite ohne Worker ist eine Seite ohne Offline-Betrieb
    // und sonst nichts.
    expect(() => registerServiceWorker()).not.toThrow()
  })
})

describe('in APK und Windows-Fenster', () => {
  test('wird ein bereits angemeldeter Worker abgemeldet', async () => {
    // Nicht bloß „nicht anmelden": wer die App schon hat, hat auch schon einen
    // angemeldeten Worker — und der lieferte sonst für immer die Fassung von
    // vorgestern.
    const unregister = vi.fn(() => Promise.resolve(true))

    stubServiceWorker([{ unregister }, { unregister }])
    stubCaches([])

    await removeServiceWorker()

    expect(unregister).toHaveBeenCalledTimes(2)
  })

  test('wird sein Zwischenspeicher geleert', async () => {
    stubServiceWorker([])

    const { deleted } = stubCaches(['workbox-precache-v2', 'sonstiges'])

    await removeServiceWorker()

    expect(deleted).toEqual(['workbox-precache-v2', 'sonstiges'])
  })

  test('ist ein Fehlschlag beim Aufräumen kein Absturz', async () => {
    vi.stubGlobal('navigator', {
      serviceWorker: {
        getRegistrations: () => Promise.reject(new Error('geht gerade nicht')),
      },
    })

    await expect(removeServiceWorker()).resolves.toBeUndefined()
  })
})
