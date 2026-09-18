import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PlatformError } from './errors.ts'
import { fallbackPlatform } from './fallback.ts'

/**
 * Die Web-Umsetzung ist die Messlatte für Android und Windows: was sie
 * zusichert, müssen die anderen auch zusichern. Geprüft wird deshalb weniger
 * das Durchreichen an localStorage als das Verhalten in den Fällen, in denen
 * der Browser nicht mitspielt — genau dort ist bisher die App abgestürzt.
 */

describe('fallbackPlatform.storage', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('gibt zurück, was zuvor abgelegt wurde', () => {
    fallbackPlatform.storage.set('remotedesktop.test', 'wert')

    expect(fallbackPlatform.storage.get('remotedesktop.test')).toBe('wert')
  })

  it('meldet einen unbekannten Schlüssel als undefined, nicht als null', () => {
    // null würde sich durch die ganze App ziehen und überall eine zweite
    // Fallunterscheidung erzwingen.
    expect(fallbackPlatform.storage.get('gibt.es.nicht')).toBeUndefined()
  })

  it('entfernt einen Schlüssel wieder', () => {
    fallbackPlatform.storage.set('remotedesktop.test', 'wert')
    fallbackPlatform.storage.remove('remotedesktop.test')

    expect(fallbackPlatform.storage.get('remotedesktop.test')).toBeUndefined()
  })
})

describe('fallbackPlatform.storage im privaten Modus', () => {
  // Manche Browser werfen bei jedem localStorage-Zugriff. Ein Absturz beim
  // Start wäre die Folge — deshalb muss die Schicht das schlucken.
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('liefert undefined statt zu werfen, wenn das Lesen scheitert', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError')
    })

    expect(() => fallbackPlatform.storage.get('irgendwas')).not.toThrow()
    expect(fallbackPlatform.storage.get('irgendwas')).toBeUndefined()
  })

  it('bleibt beim Schreiben stumm, wenn kein Platz da ist', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError')
    })

    expect(() => fallbackPlatform.storage.set('a', 'b')).not.toThrow()
  })

  it('bleibt beim Entfernen stumm', () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('SecurityError')
    })

    expect(() => fallbackPlatform.storage.remove('a')).not.toThrow()
  })
})

describe('fallbackPlatform.capabilities', () => {
  it('sagt nein zu allem, was der Browser nicht kann', () => {
    // Die Oberfläche blendet danach Knöpfe aus. Stünde hier versehentlich
    // true, liefe der Nutzer in eine Funktion, die es nicht gibt.
    expect(fallbackPlatform.capabilities.camera).toBe(false)
    expect(fallbackPlatform.capabilities.pointerLock).toBe(false)
    expect(fallbackPlatform.capabilities.backgroundSession).toBe(false)
    expect(fallbackPlatform.capabilities.selfUpdate).toBe(false)
  })

  it('fragt die Zwischenablage erst beim Zugriff ab', () => {
    // Als fester Wert wäre die Angabe falsch, sobald sich navigator später
    // ändert — etwa im WebView2-Fenster.
    const beschreibung = Object.getOwnPropertyDescriptor(
      fallbackPlatform.capabilities,
      'clipboard',
    )

    expect(beschreibung?.get).toBeTypeOf('function')
  })
})

describe('fallbackPlatform: was der Browser nicht hergibt', () => {
  it('wirft einen PlatformError beim QR-Scan', async () => {
    await expect(fallbackPlatform.qr.scan()).rejects.toBeInstanceOf(PlatformError)
  })

  it('meldet „kein Update" als Ergebnis, nicht als Fehler', async () => {
    // Der Service Worker erledigt das. Ein Fehler wäre hier irreführend.
    await expect(fallbackPlatform.update.check()).resolves.toBeUndefined()
  })

  it('wirft, wenn jemand trotzdem installieren will', async () => {
    await expect(
      fallbackPlatform.update.install({ version: '2.0.0', url: 'https://example.invalid/app' }),
    ).rejects.toBeInstanceOf(PlatformError)
  })
})
