import { describe, expect, test } from 'vitest'
import { KEYBOARD_PADDING, interpretInput } from './softKeyboard.ts'

describe('Eingaben der Handy-Tastatur übersetzen', () => {
  test('getipptes Zeichen wird sofort zu Text', () => {
    // Act
    const actions = interpretInput('insertText', 'a')

    // Assert
    expect(actions).toEqual([{ kind: 'text', text: 'a' }])
  })

  test('Eingefügtes aus der Zwischenablage kommt am Stück', () => {
    expect(interpretInput('insertFromPaste', 'Hallo Welt')).toEqual([
      { kind: 'text', text: 'Hallo Welt' },
    ])
  })

  test('Eingabetaste wird zur Enter-Taste, nicht zu Text', () => {
    expect(interpretInput('insertLineBreak', null)).toEqual([{ kind: 'key', key: 'enter' }])
  })

  test('Rücktaste löscht auf dem Rechner', () => {
    // Assert — genau dafür geht jeder Anschlag sofort raus.
    expect(interpretInput('deleteContentBackward', null)).toEqual([
      { kind: 'key', key: 'backspace' },
    ])
  })

  test('Wischen über die Rücktaste löscht ein ganzes Wort', () => {
    expect(interpretInput('deleteWordBackward', null)).toEqual([
      { kind: 'chord', keys: ['ctrl', 'backspace'] },
    ])
  })

  test('Zwischenstände beim Wischen über die Tastatur werden verworfen', () => {
    // Assert — sonst käme jedes halbfertige Wort mehrfach an; das fertige holt
    // der Aufrufer bei `compositionend`.
    expect(interpretInput('insertCompositionText', 'Hal')).toEqual([])
  })

  test('Autokorrektur wird verworfen', () => {
    // Assert — die getippten Buchstaben stehen längst auf dem Rechner.
    expect(interpretInput('insertReplacementText', 'Hallo')).toEqual([])
  })

  test('Einfügen ohne Inhalt schickt nichts', () => {
    expect(interpretInput('insertText', '')).toEqual([])
    expect(interpretInput('insertText', null)).toEqual([])
  })

  test('unbekannte Eingabearten werden verworfen', () => {
    expect(interpretInput('formatBold', null)).toEqual([])
    expect(interpretInput('historyUndo', null)).toEqual([])
  })
})

describe('Füllzeichen des Eingabefeldes', () => {
  test('besteht ausschließlich aus unsichtbaren Zeichen', () => {
    // Assert — sichtbarer Inhalt würde den Hinweistext überlagern.
    expect(KEYBOARD_PADDING).toMatch(/^​+$/)
  })

  test('bietet genug Vorrat für mehrere Löschvorgänge', () => {
    expect(KEYBOARD_PADDING.length).toBeGreaterThan(4)
  })
})
