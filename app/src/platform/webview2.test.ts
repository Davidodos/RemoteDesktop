import { afterEach, describe, expect, test } from 'vitest'
import { webPlatform } from './web.ts'
import { isWebView2, webview2Platform } from './webview2.ts'

/**
 * Die Plattform des Windows-Fensters. Sie unterscheidet sich von `web.ts` genau
 * dort, wo der Desktop tatsächlich mehr kann — jede Fähigkeit, die hier zu
 * großzügig steht, wird später ein Knopf, der nur eine Fehlermeldung erzeugt.
 */
const HOST = { machineName: 'PC-DAVID' }

afterEach(() => {
  delete window.remoteDesktopHost
})

describe('erkennen, wo die App läuft', () => {
  test('ohne Wirtsprogramm ist es kein Fenster', () => {
    expect(isWebView2()).toBe(false)
  })

  test('mit Wirtsprogramm schon', () => {
    // Arrange
    window.remoteDesktopHost = HOST

    // Assert
    expect(isWebView2()).toBe(true)
  })
})

describe('was das Fenster kann', () => {
  test('der Rechnername kommt vom Wirtsprogramm', () => {
    // Assert — nur damit lässt sich die Selbstverbindung sperren.
    expect(webview2Platform(HOST).machineName).toBe('PC-DAVID')
    expect(webPlatform.machineName).toBeUndefined()
  })

  test('Pointer Lock, Tastatur und Zwischenablage sind da', () => {
    // Act
    const { capabilities } = webview2Platform(HOST)

    // Assert — das sind die drei Dinge, die am Desktop besser gehen als am Handy.
    expect(capabilities.pointerLock).toBe(true)
    expect(capabilities.physicalKeyboard).toBe(true)
    expect(capabilities.clipboard).toBe(true)
  })

  test('die Sitzung überlebt den Hintergrund', () => {
    // Assert — anders als ein Browser-Tab wird das Fenster nicht gedrosselt.
    expect(webview2Platform(HOST).capabilities.backgroundSession).toBe(true)
    expect(webPlatform.capabilities.backgroundSession).toBe(false)
  })

  test('Kamera und Selbst-Update stehen bewusst auf false', () => {
    // Act
    const { capabilities } = webview2Platform(HOST)

    // Assert — die Kamera braucht am eigenen Rechner niemand, das Selbst-Update
    // kommt erst mit den GitHub-Releases in Phase 14.
    expect(capabilities.camera).toBe(false)
    expect(capabilities.selfUpdate).toBe(false)
  })

  test('sie heißt webview2', () => {
    expect(webview2Platform(HOST).name).toBe('webview2')
  })
})

describe('was sie trotzdem nicht kann', () => {
  test('QR-Scan endet mit einer Meldung, nicht mit einem Absturz', async () => {
    await expect(webview2Platform(HOST).qr.scan()).rejects.toThrow('keine Kamera')
  })

  test('Installieren ebenso', async () => {
    await expect(
      webview2Platform(HOST).update.install({ version: '2.0.0', url: 'https://example.invalid' }),
    ).rejects.toThrow('noch nicht selbst')
  })

  test('nach Updates zu suchen ist kein Fehler, sondern ergebnislos', async () => {
    await expect(webview2Platform(HOST).update.check()).resolves.toBeUndefined()
  })
})

describe('der Speicher des Fensters', () => {
  test('legt ab und gibt zurück', () => {
    // Arrange
    const { storage } = webview2Platform(HOST)

    // Act
    storage.set('probe', 'wert')

    // Assert
    expect(storage.get('probe')).toBe('wert')

    // Act
    storage.remove('probe')

    // Assert
    expect(storage.get('probe')).toBeUndefined()
  })

  test('der Schlüsselspeicher ebenso', () => {
    // Arrange
    const { keystore } = webview2Platform(HOST)

    // Act
    keystore.set('schlüssel', 'geheim')

    // Assert
    expect(keystore.get('schlüssel')).toBe('geheim')
  })
})
