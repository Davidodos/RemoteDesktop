import { afterEach, describe, expect, it } from 'vitest'
import {
  capacitorPlatform,
  isCapacitor,
  readAllPreferences,
  type CapacitorPlugins,
} from './capacitor.ts'
import { PlatformError } from './errors.ts'
import { webPlatform } from './web.ts'

/**
 * Die Android-Plattform, geprüft ohne Android.
 *
 * Was hier zählt, ist nicht das Durchreichen an die Plugins, sondern zwei
 * Zusagen, die die App macht: der Speicher antwortet synchron, obwohl die
 * Brücke asynchron ist, und die Fähigkeiten stehen genau dort auf `true`, wo
 * die APK tatsächlich mehr kann als die PWA. Eine zu großzügige Angabe wird
 * später ein Knopf, der nur eine Fehlermeldung erzeugt.
 */

interface Aufzeichnung {
  plugins: CapacitorPlugins
  geschrieben: { key: string; value: string }[]
  geloescht: string[]
  gestartet: string[]
  gestoppt: number
}

function attrappe(overrides: Partial<CapacitorPlugins> = {}): Aufzeichnung {
  const geschrieben: { key: string; value: string }[] = []
  const geloescht: string[] = []
  const gestartet: string[] = []
  const zaehler = { gestoppt: 0 }

  const plugins: CapacitorPlugins = {
    preferences: {
      keys: () => Promise.resolve({ keys: [] }),
      get: () => Promise.resolve({ value: null }),
      set: (options) => {
        geschrieben.push(options)
        return Promise.resolve()
      },
      remove: ({ key }) => {
        geloescht.push(key)
        return Promise.resolve()
      },
    },
    clipboard: {
      read: () => Promise.resolve({ value: 'aus der Zwischenablage' }),
      write: () => Promise.resolve(),
    },
    barcode: {
      scanBarcode: () => Promise.resolve({ ScanResult: 'remotedesktop://pair?host=pc&code=123456' }),
    },
    session: {
      start: ({ device }) => {
        gestartet.push(device)
        return Promise.resolve()
      },
      stop: () => {
        zaehler.gestoppt += 1
        return Promise.resolve()
      },
    },
    ...overrides,
  }

  return {
    plugins,
    geschrieben,
    geloescht,
    gestartet,
    get gestoppt() {
      return zaehler.gestoppt
    },
  }
}

afterEach(() => {
  delete window.Capacitor
})

describe('erkennen, wo die App läuft', () => {
  it('ohne Brücke ist es keine APK', () => {
    expect(isCapacitor()).toBe(false)
  })

  it('mit Brücke schon', () => {
    // Arrange
    window.Capacitor = { isNativePlatform: () => true }

    // Assert
    expect(isCapacitor()).toBe(true)
  })

  it('die Brücke im Browser zählt nicht', () => {
    // `@capacitor/core` setzt den Namen auch im Browser — dort meldet
    // isNativePlatform() aber false, und dann ist `web.ts` zuständig.
    window.Capacitor = { isNativePlatform: () => false }

    expect(isCapacitor()).toBe(false)
  })
})

describe('der Speicher antwortet synchron', () => {
  it('gibt zurück, was beim Start eingelesen wurde', () => {
    // Arrange — genau dafür gibt es den Abzug: `storage.getDevices()` wird beim
    // ersten Rendern gelesen, da ist kein Platz für ein await.
    const cache = new Map([['remotedesktop.devices', '[]']])

    // Act
    const platform = capacitorPlatform(attrappe().plugins, cache)

    // Assert
    expect(platform.storage.get('remotedesktop.devices')).toBe('[]')
  })

  it('gibt einen unbekannten Schlüssel als undefined zurück, nicht als null', () => {
    expect(capacitorPlatform(attrappe().plugins, new Map()).storage.get('gibt.es.nicht'))
      .toBeUndefined()
  })

  it('liest sofort, was gerade geschrieben wurde', () => {
    // Arrange
    const platform = capacitorPlatform(attrappe().plugins, new Map())

    // Act
    platform.storage.set('remotedesktop.lastDevice', 'pc')

    // Assert — ohne den Abzug käme hier undefined zurück, bis die Brücke
    // geantwortet hat.
    expect(platform.storage.get('remotedesktop.lastDevice')).toBe('pc')
  })

  it('reicht das Schreiben an die Preferences weiter', () => {
    // Arrange
    const aufzeichnung = attrappe()
    const platform = capacitorPlatform(aufzeichnung.plugins, new Map())

    // Act
    platform.storage.set('a', 'b')

    // Assert
    expect(aufzeichnung.geschrieben).toEqual([{ key: 'a', value: 'b' }])
  })

  it('entfernt aus Abzug und Preferences', () => {
    // Arrange
    const aufzeichnung = attrappe()
    const platform = capacitorPlatform(aufzeichnung.plugins, new Map([['a', 'b']]))

    // Act
    platform.storage.remove('a')

    // Assert
    expect(platform.storage.get('a')).toBeUndefined()
    expect(aufzeichnung.geloescht).toEqual(['a'])
  })

  it('bleibt stumm, wenn das Schreiben scheitert', async () => {
    // Arrange — ein voller Speicher darf die App nicht beenden. Unbehandelt
    // wäre die abgewiesene Zusage ein Absturz.
    const aufzeichnung = attrappe({
      preferences: {
        keys: () => Promise.resolve({ keys: [] }),
        get: () => Promise.resolve({ value: null }),
        set: () => Promise.reject(new Error('kein Platz')),
        remove: () => Promise.reject(new Error('kein Platz')),
      },
    })
    const platform = capacitorPlatform(aufzeichnung.plugins, new Map())

    // Act
    expect(() => platform.storage.set('a', 'b')).not.toThrow()

    // Die abgewiesene Zusage muss abgefangen sein, bevor der Zyklus endet —
    // sonst meldet der Testlauf sie hier als unbehandelt und schlägt fehl.
    await new Promise((fertig) => setTimeout(fertig, 0))

    // Assert — der Wert steht trotzdem im Abzug, die Sitzung läuft weiter.
    expect(platform.storage.get('a')).toBe('b')

    // Act
    expect(() => platform.storage.remove('a')).not.toThrow()
    await new Promise((fertig) => setTimeout(fertig, 0))

    // Assert
    expect(platform.storage.get('a')).toBeUndefined()
  })

  it('teilt sich die Ablage mit dem Schlüsselspeicher', () => {
    // Arrange — Android trennt das nicht; die Schlüssel sind ohnehin verschieden.
    const platform = capacitorPlatform(attrappe().plugins, new Map())

    // Act
    platform.keystore.set('remotedesktop.clientKey', '{}')

    // Assert
    expect(platform.storage.get('remotedesktop.clientKey')).toBe('{}')
  })
})

describe('den Speicher beim Start einlesen', () => {
  it('holt jeden abgelegten Schlüssel', async () => {
    // Arrange — dynamische Schlüssel wie `remotedesktop.monitor.<id>` lassen
    // sich nicht aufzählen; deshalb wird über die Schlüsselliste gegangen.
    const ablage = new Map([
      ['remotedesktop.devices', '[]'],
      ['remotedesktop.monitor.pc', '1'],
    ])
    const aufzeichnung = attrappe({
      preferences: {
        keys: () => Promise.resolve({ keys: [...ablage.keys()] }),
        get: ({ key }) => Promise.resolve({ value: ablage.get(key) ?? null }),
        set: () => Promise.resolve(),
        remove: () => Promise.resolve(),
      },
    })

    // Act
    const cache = await readAllPreferences(aufzeichnung.plugins)

    // Assert
    expect(cache).toEqual(ablage)
  })

  it('lässt einen leeren Wert weg statt null einzutragen', async () => {
    // Arrange
    const aufzeichnung = attrappe({
      preferences: {
        keys: () => Promise.resolve({ keys: ['verwaist'] }),
        get: () => Promise.resolve({ value: null }),
        set: () => Promise.resolve(),
        remove: () => Promise.resolve(),
      },
    })

    // Act
    const cache = await readAllPreferences(aufzeichnung.plugins)

    // Assert — null würde sich sonst als Wert durch die ganze App ziehen.
    expect(cache.has('verwaist')).toBe(false)
  })

  it('startet mit leerem Speicher, statt beim Start abzubrechen', async () => {
    // Arrange — eine Brücke, die schon beim Lesen scheitert, ist ein
    // Ausnahmefall. Ein weißer Bildschirm wäre die schlechtere Antwort als eine
    // App, in der neu gekoppelt werden muss.
    const aufzeichnung = attrappe({
      preferences: {
        keys: () => Promise.reject(new Error('Brücke antwortet nicht')),
        get: () => Promise.resolve({ value: null }),
        set: () => Promise.resolve(),
        remove: () => Promise.resolve(),
      },
    })

    // Act
    const cache = await readAllPreferences(aufzeichnung.plugins)

    // Assert
    expect(cache.size).toBe(0)
  })
})

describe('was die APK kann und die PWA nicht', () => {
  it('Kamera und Hintergrundsitzung', () => {
    // Act
    const { capabilities } = capacitorPlatform(attrappe().plugins, new Map())

    // Assert — das sind die beiden Gründe, aus denen es die APK überhaupt gibt.
    expect(capabilities.camera).toBe(true)
    expect(capabilities.backgroundSession).toBe(true)
    expect(webPlatform.capabilities.camera).toBe(false)
    expect(webPlatform.capabilities.backgroundSession).toBe(false)
  })

  it('aber weiterhin kein Pointer Lock und keine echte Tastatur', () => {
    // Act
    const { capabilities } = capacitorPlatform(attrappe().plugins, new Map())

    // Assert — ein Finger ist kein einfangbarer Zeiger, und `keydown` meldet am
    // Handy nur 229. Beides ändert die APK nicht.
    expect(capabilities.pointerLock).toBe(false)
    expect(capabilities.physicalKeyboard).toBe(false)
  })

  it('Selbst-Update kann sie seit Phase 14', () => {
    // Ein Knopf und ein Systemdialog: stiller lässt Android es außerhalb von
    // Google Play nicht zu.
    expect(capacitorPlatform(attrappe().plugins, new Map()).capabilities.selfUpdate).toBe(true)
  })

  it('sie heißt capacitor und kennt keinen Rechnernamen', () => {
    // Ein Handy ist nie ein Agent — es gibt nichts, wovor die
    // Selbstverbindungssperre schützen müsste.
    const platform = capacitorPlatform(attrappe().plugins, new Map())

    expect(platform.name).toBe('capacitor')
    expect(platform.machineName).toBeUndefined()
  })
})

describe('der QR-Scanner', () => {
  it('liefert den gelesenen Inhalt', async () => {
    await expect(capacitorPlatform(attrappe().plugins, new Map()).qr.scan()).resolves.toBe(
      'remotedesktop://pair?host=pc&code=123456',
    )
  })

  it('macht aus dem Abbruch einen Satz statt einer Fehlernummer', async () => {
    // Arrange — das Plugin meldet den Abbruch über die Zurück-Taste als
    // `OS-PLUG-BARC-…`. Das am Handy anzuzeigen hilft niemandem.
    const aufzeichnung = attrappe({
      barcode: { scanBarcode: () => Promise.reject(new Error('OS-PLUG-BARC-0005')) },
    })

    // Assert
    await expect(capacitorPlatform(aufzeichnung.plugins, new Map()).qr.scan()).rejects.toBeInstanceOf(
      PlatformError,
    )
  })

  it('behandelt ein leeres Ergebnis wie einen Fehlschlag', async () => {
    // Arrange
    const aufzeichnung = attrappe({
      barcode: { scanBarcode: () => Promise.resolve({ ScanResult: '' }) },
    })

    // Assert — sonst liefe ein leerer Text in den Parser und ergäbe dort eine
    // zweite, unpassendere Meldung.
    await expect(capacitorPlatform(aufzeichnung.plugins, new Map()).qr.scan()).rejects.toThrow(
      /nicht gelesen/,
    )
  })
})

describe('der Vordergrunddienst', () => {
  it('startet mit dem Namen des Rechners', async () => {
    // Arrange — der Name steht in der Benachrichtigung; ohne ihn sieht man
    // nicht, wohin die offene Verbindung geht.
    const aufzeichnung = attrappe()

    // Act
    await capacitorPlatform(aufzeichnung.plugins, new Map()).session.begin('Arbeitsrechner')

    // Assert
    expect(aufzeichnung.gestartet).toEqual(['Arbeitsrechner'])
  })

  it('hält einen Wechsel des Geräts aus', async () => {
    // Arrange — die Geräteauswahl wechselt öfter, als eine Sitzung endet.
    const aufzeichnung = attrappe()
    const { session } = capacitorPlatform(aufzeichnung.plugins, new Map())

    // Act
    await session.begin('PC')
    await session.begin('Laptop')

    // Assert
    expect(aufzeichnung.gestartet).toEqual(['PC', 'Laptop'])
  })

  it('endet auf Zuruf', async () => {
    // Arrange
    const aufzeichnung = attrappe()

    // Act
    await capacitorPlatform(aufzeichnung.plugins, new Map()).session.end()

    // Assert
    expect(aufzeichnung.gestoppt).toBe(1)
  })

  it('im Browser passiert an derselben Stelle nichts', async () => {
    // Assert — die Aufrufstelle in App.tsx kommt ohne Fallunterscheidung aus.
    await expect(webPlatform.session.begin('PC')).resolves.toBeUndefined()
    await expect(webPlatform.session.end()).resolves.toBeUndefined()
  })
})

describe('die Zwischenablage', () => {
  it('liest den Text heraus', async () => {
    await expect(capacitorPlatform(attrappe().plugins, new Map()).clipboard.readText())
      .resolves.toBe('aus der Zwischenablage')
  })
})

describe('was auch die APK nicht kann', () => {
  it('sich selbst installieren', async () => {
    await expect(
      capacitorPlatform(attrappe().plugins, new Map()).update.install({
        version: '2.0.0',
        url: 'https://example.invalid',
      }),
    ).rejects.toBeInstanceOf(PlatformError)
  })

  it('nach Updates zu suchen ist kein Fehler, sondern ergebnislos', async () => {
    await expect(
      capacitorPlatform(attrappe().plugins, new Map()).update.check(),
    ).resolves.toBeUndefined()
  })
})
