import { describe, expect, test } from 'vitest'
import { findLatestApk, isDifferentVersion, stripTagPrefix } from './appUpdate.ts'

const RELEASE = {
  tag_name: 'v1.2.0',
  assets: [
    { name: 'RemoteDesktopAgent.exe', browser_download_url: 'https://example.invalid/agent.exe' },
    { name: 'remotedesktop.apk', browser_download_url: 'https://example.invalid/app.apk' },
  ],
}

const liefert =
  (antwort: unknown) =>
  (): Promise<unknown> =>
    Promise.resolve(antwort)

describe('findLatestApk', () => {
  test('findet die APK und ihre Fassung', async () => {
    expect(await findLatestApk(liefert(RELEASE))).toEqual({
      version: '1.2.0',
      url: 'https://example.invalid/app.apk',
    })
  })

  test('ohne APK im Release gibt es nichts anzubieten', async () => {
    const ohne = { ...RELEASE, assets: [RELEASE.assets[0]] }

    expect(await findLatestApk(liefert(ohne))).toBeUndefined()
  })

  test('ein Fehler von GitHub endet als „nichts Neues"', async () => {
    // Weder ein Netzfehler noch eine Fehlermeldung von GitHub dürfen die App
    // stören — sie prüft das beim Start.
    const wirft = (): Promise<unknown> => Promise.reject(new Error('offline'))

    expect(await findLatestApk(wirft)).toBeUndefined()
  })

  test.each([{ message: 'Not Found' }, [], 'kein Objekt', null])(
    'eine unpassende Antwort (%s) ergibt nichts',
    async (antwort) => {
      expect(await findLatestApk(liefert(antwort))).toBeUndefined()
    },
  )
})

describe('stripTagPrefix', () => {
  test('v1.2.0 und 1.2.0 bedeuten dasselbe', () => {
    expect(stripTagPrefix('v1.2.0')).toBe('1.2.0')
    expect(stripTagPrefix('1.2.0')).toBe('1.2.0')
  })
})

describe('isDifferentVersion', () => {
  test('gleiche Fassung heißt nichts zu tun', () => {
    expect(isDifferentVersion('v1.2.0', '1.2.0')).toBe(false)
  })

  test('eine andere Fassung wird angeboten', () => {
    expect(isDifferentVersion('v1.3.0', '1.2.0')).toBe(true)
  })

  test('auch eine zurückgenommene Ausgabe wird angeboten', () => {
    // Was tatsächlich installiert werden darf, entscheidet Android anhand der
    // versionCode — hier zu filtern hieße, dem Nutzer den Weg zurück zu nehmen.
    expect(isDifferentVersion('v1.1.0', '1.2.0')).toBe(true)
  })

  test('ohne bekannte eigene Fassung wird angeboten', () => {
    expect(isDifferentVersion('v1.2.0', undefined)).toBe(true)
  })
})
