import { describe, expect, test } from 'vitest'
import { belongsToRemote, toAgentKey } from './hardwareKeyboard.ts'

/**
 * Die echte Tastatur am Desktop. Übersetzt wird `KeyboardEvent.code` — was der
 * Agent in `VirtualKeys.cs` nicht kennt, darf hier gar nicht erst hinausgehen.
 */
describe('Tasten übersetzen', () => {
  test('Buchstaben kommen als Kleinbuchstabe an', () => {
    expect(toAgentKey('KeyA')).toBe('a')
    expect(toAgentKey('KeyZ')).toBe('z')
  })

  test('Ziffern beider Reihen ergeben dieselbe Zahl', () => {
    // Arrange — am Zielrechner ist es dieselbe Ziffer, egal woher sie kommt.
    expect(toAgentKey('Digit7')).toBe('7')
    expect(toAgentKey('Numpad7')).toBe('7')
  })

  test('Funktionstasten bis F24', () => {
    expect(toAgentKey('F1')).toBe('f1')
    expect(toAgentKey('F12')).toBe('f12')
    expect(toAgentKey('F24')).toBe('f24')
  })

  test('es gibt keine F25', () => {
    // Assert — der Agent löst nur f1..f24 auf, alles andere wäre ein 400.
    expect(toAgentKey('F25')).toBeUndefined()
    expect(toAgentKey('F0')).toBeUndefined()
  })

  test('Modifier landen auf den Namen des Agents', () => {
    expect(toAgentKey('ControlLeft')).toBe('ctrl')
    expect(toAgentKey('ControlRight')).toBe('ctrl')
    expect(toAgentKey('ShiftRight')).toBe('shift')
    expect(toAgentKey('AltLeft')).toBe('alt')
    expect(toAgentKey('MetaLeft')).toBe('win')
  })

  test('Steuer- und Pfeiltasten', () => {
    expect(toAgentKey('Escape')).toBe('escape')
    expect(toAgentKey('Enter')).toBe('enter')
    expect(toAgentKey('NumpadEnter')).toBe('enter')
    expect(toAgentKey('Space')).toBe('space')
    expect(toAgentKey('ArrowUp')).toBe('arrowup')
    expect(toAgentKey('PageDown')).toBe('pagedown')
  })

  test('Unbekanntes geht nicht hinaus', () => {
    // Assert — lieber eine Taste zu wenig als eine, die der Agent ablehnt.
    expect(toAgentKey('BrowserFavorites')).toBeUndefined()
    expect(toAgentKey('IntlBackslash')).toBeUndefined()
    expect(toAgentKey('')).toBeUndefined()
  })

  test('nur genau geschriebene Codes zählen', () => {
    // Assert — Chromium schreibt sie so, und ein lockerer Vergleich würde
    // Tippfehler zu gültigen Tasten machen.
    expect(toAgentKey('keya')).toBeUndefined()
    expect(toAgentKey('KeyAB')).toBeUndefined()
  })
})

describe('wohin ein Anschlag gehört', () => {
  test('ohne Ziel geht er an den Zielrechner', () => {
    expect(belongsToRemote(null)).toBe(true)
  })

  test('ein Eingabefeld behält seine Tasten', () => {
    // Arrange — sonst ließe sich der Kopplungscode nicht mehr eintippen.
    const input = document.createElement('input')

    // Assert
    expect(belongsToRemote(input)).toBe(false)
  })

  test('auch verschachtelt in einem Feld', () => {
    // Arrange
    const wrapper = document.createElement('div')
    const area = document.createElement('textarea')
    wrapper.append(area)

    // Assert
    expect(belongsToRemote(area)).toBe(false)
  })

  test('ein bearbeitbarer Bereich ebenso', () => {
    // Arrange
    const editable = document.createElement('div')
    editable.setAttribute('contenteditable', 'true')

    // Assert
    expect(belongsToRemote(editable)).toBe(false)
  })

  test('ein gewöhnlicher Knopf hält nichts zurück', () => {
    // Arrange
    const button = document.createElement('button')

    // Assert
    expect(belongsToRemote(button)).toBe(true)
  })
})
