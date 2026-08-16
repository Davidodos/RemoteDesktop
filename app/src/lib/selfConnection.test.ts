import { describe, expect, test } from 'vitest'
import { isSelfConnection, selfConnectionMessage } from './selfConnection.ts'

/**
 * Die Sperre gegen Selbstverbindung. Greift sie nicht, zeigt der Rechner sein
 * eigenes Fenster im eigenen Fenster und die Eingaben laufen im Kreis — dann
 * hilft nur noch, das Fenster von außen zu schließen.
 */
describe('erkennt den eigenen Rechner', () => {
  test('gleicher Name heißt: das sind wir selbst', () => {
    // Assert
    expect(isSelfConnection({ name: 'PC-DAVID' }, { name: 'PC-DAVID' })).toBe(true)
  })

  test('Groß- und Kleinschreibung zählt nicht', () => {
    // Arrange — Windows meldet den Namen mal in Versalien, mal wie eingetragen.
    expect(isSelfConnection({ name: 'pc-david' }, { name: 'PC-DAVID' })).toBe(true)
    expect(isSelfConnection({ name: 'PC-David' }, { name: 'pc-DAVID' })).toBe(true)
  })

  test('ein Domänen-Suffix ändert nichts', () => {
    // Arrange — /api/info liefert den kurzen Namen, der Tailnet-Name ist lang.
    expect(isSelfConnection({ name: 'pc-david.tailnet.ts.net' }, { name: 'PC-DAVID' })).toBe(true)
    expect(isSelfConnection({ name: 'PC-DAVID' }, { name: 'pc-david.fritz.box' })).toBe(true)
  })

  test('Leerzeichen am Rand zählen nicht', () => {
    expect(isSelfConnection({ name: '  PC-DAVID  ' }, { name: 'pc-david' })).toBe(true)
  })
})

describe('lässt fremde Rechner durch', () => {
  test('ein anderer Name ist ein anderer Rechner', () => {
    expect(isSelfConnection({ name: 'LAPTOP' }, { name: 'PC-DAVID' })).toBe(false)
  })

  test('ein ähnlicher Name ist noch kein gleicher', () => {
    // Assert — sonst ließe sich der Laptop vom PC aus nicht mehr steuern.
    expect(isSelfConnection({ name: 'PC-DAVID-2' }, { name: 'PC-DAVID' })).toBe(false)
  })

  test('ohne eigenen Namen wird nichts gesperrt', () => {
    // Arrange — im Browser gibt es den Rechnernamen nicht, und dort kann die
    // Frage auch gar nicht auftreten.
    expect(isSelfConnection({ name: 'PC-DAVID' }, { name: undefined })).toBe(false)
  })

  test('ohne Auskunft des Agents wird nichts gesperrt', () => {
    expect(isSelfConnection({ name: undefined }, { name: 'PC-DAVID' })).toBe(false)
  })

  test('leere Angaben sperren nicht', () => {
    // Assert — zwei leere Namen sind nicht „derselbe Rechner", sondern gar keiner.
    expect(isSelfConnection({ name: '' }, { name: '' })).toBe(false)
    expect(isSelfConnection({ name: '   ' }, { name: 'PC-DAVID' })).toBe(false)
  })
})

describe('die Meldung', () => {
  test('nennt den Rechner und den Grund', () => {
    // Act
    const message = selfConnectionMessage('PC-DAVID')

    // Assert — eine reine Verweigerung sieht aus wie ein Fehler.
    expect(message).toContain('PC-DAVID')
    expect(message).toContain('gesperrt')
  })
})

describe('der Fingerabdruck schlägt den Namen', () => {
  test('zwei Geräte mit gleichem Namen sind nicht dasselbe Gerät', () => {
    // Arrange — genau der Fall vom echten Gerät: ein Handy meldet als Namen,
    // was unter „Gerätename" in den Android-Einstellungen steht. Das ist
    // häufig der Vorname des Besitzers, und der Windows-Rechner heißt genauso.
    const handy = { name: 'David', fingerprint: 'aaaaaaaaaaaaaaaa' }
    const rechner = { name: 'David', fingerprint: 'bbbbbbbbbbbbbbbb' }

    // Act & Assert — vorher verweigerte die App hier die Verbindung mit der
    // Begründung, man wolle sich selbst fernsteuern.
    expect(isSelfConnection(handy, rechner)).toBe(false)
  })

  test('derselbe Fingerabdruck ist dasselbe Gerät, auch bei anderem Namen', () => {
    // Arrange — ein Rechner, den jemand zwischendurch umbenannt hat.
    const ziel = { name: 'pc-alt', fingerprint: 'aaaaaaaaaaaaaaaa' }
    const selbst = { name: 'pc-neu', fingerprint: 'aaaaaaaaaaaaaaaa' }

    // Act & Assert
    expect(isSelfConnection(ziel, selbst)).toBe(true)
  })

  test('ohne Fingerabdruck bleibt der Name der Notbehelf', () => {
    // Ein Waker hat keinen Agent-Schlüssel. Dort ist der Name das Einzige,
    // was es gibt — besser als gar keine Sperre.
    expect(isSelfConnection({ name: 'PC-DAVID' }, { name: 'pc-david' })).toBe(true)

    // Und auch dann zählt er nur, wenn beide Seiten einen haben.
    expect(
      isSelfConnection({ name: 'PC' }, { name: 'PC', fingerprint: 'aaaaaaaaaaaaaaaa' }),
    ).toBe(true)
  })
})

