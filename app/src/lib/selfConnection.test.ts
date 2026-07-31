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
    expect(isSelfConnection('PC-DAVID', 'PC-DAVID')).toBe(true)
  })

  test('Groß- und Kleinschreibung zählt nicht', () => {
    // Arrange — Windows meldet den Namen mal in Versalien, mal wie eingetragen.
    expect(isSelfConnection('pc-david', 'PC-DAVID')).toBe(true)
    expect(isSelfConnection('PC-David', 'pc-DAVID')).toBe(true)
  })

  test('ein Domänen-Suffix ändert nichts', () => {
    // Arrange — /api/info liefert den kurzen Namen, der Tailnet-Name ist lang.
    expect(isSelfConnection('pc-david.tailnet.ts.net', 'PC-DAVID')).toBe(true)
    expect(isSelfConnection('PC-DAVID', 'pc-david.fritz.box')).toBe(true)
  })

  test('Leerzeichen am Rand zählen nicht', () => {
    expect(isSelfConnection('  PC-DAVID  ', 'pc-david')).toBe(true)
  })
})

describe('lässt fremde Rechner durch', () => {
  test('ein anderer Name ist ein anderer Rechner', () => {
    expect(isSelfConnection('LAPTOP', 'PC-DAVID')).toBe(false)
  })

  test('ein ähnlicher Name ist noch kein gleicher', () => {
    // Assert — sonst ließe sich der Laptop vom PC aus nicht mehr steuern.
    expect(isSelfConnection('PC-DAVID-2', 'PC-DAVID')).toBe(false)
  })

  test('ohne eigenen Namen wird nichts gesperrt', () => {
    // Arrange — im Browser gibt es den Rechnernamen nicht, und dort kann die
    // Frage auch gar nicht auftreten.
    expect(isSelfConnection('PC-DAVID', undefined)).toBe(false)
  })

  test('ohne Auskunft des Agents wird nichts gesperrt', () => {
    expect(isSelfConnection(undefined, 'PC-DAVID')).toBe(false)
  })

  test('leere Angaben sperren nicht', () => {
    // Assert — zwei leere Namen sind nicht „derselbe Rechner", sondern gar keiner.
    expect(isSelfConnection('', '')).toBe(false)
    expect(isSelfConnection('   ', 'PC-DAVID')).toBe(false)
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
