import { describe, expect, test } from 'vitest'
import {
  DEFAULT_SHORTCUTS,
  makeShortcutId,
  parseShortcuts,
  removeShortcut,
  upsertShortcut,
  type Shortcut,
} from './shortcuts.ts'

const WIN_TAB: Shortcut = { id: 'a', label: 'Übersicht', keys: ['win', 'tab'] }

describe('Gespeicherte Shortcuts lesen', () => {
  test('ohne Eintrag stehen die Voreinstellungen bereit', () => {
    expect(parseShortcuts(undefined)).toEqual(DEFAULT_SHORTCUTS)
  })

  test('gespeicherte Liste kommt zurück', () => {
    // Act
    const parsed = parseShortcuts(JSON.stringify([WIN_TAB]))

    // Assert
    expect(parsed).toEqual([WIN_TAB])
  })

  test('eine leere Liste bleibt leer', () => {
    // Assert — wer alle löscht, will keine zurückbekommen.
    expect(parseShortcuts('[]')).toEqual([])
  })

  test('kaputter Inhalt kostet nicht die ganze Liste', () => {
    // Arrange — so etwas kann von einer früheren Fassung stammen.
    const raw = JSON.stringify([WIN_TAB, { id: 'b' }, null, { id: 'c', label: '', keys: ['a'] }])

    // Assert
    expect(parseShortcuts(raw)).toEqual([WIN_TAB])
  })

  test('ein Shortcut ohne Tasten fällt raus', () => {
    expect(parseShortcuts(JSON.stringify([{ id: 'b', label: 'Leer', keys: [] }]))).toEqual([])
  })

  test('unlesbares JSON fällt auf die Voreinstellungen zurück', () => {
    expect(parseShortcuts('{kein json')).toEqual(DEFAULT_SHORTCUTS)
  })
})

describe('Shortcuts bearbeiten', () => {
  test('ein neuer Eintrag kommt hinten dazu', () => {
    // Act
    const next = upsertShortcut([WIN_TAB], { id: 'b', label: 'Neu', keys: ['f5'] })

    // Assert
    expect(next).toHaveLength(2)
    expect(next[1]?.id).toBe('b')
  })

  test('ein bekannter Eintrag wird ersetzt, nicht verdoppelt', () => {
    // Act
    const next = upsertShortcut([WIN_TAB], { ...WIN_TAB, label: 'Anders' })

    // Assert
    expect(next).toEqual([{ ...WIN_TAB, label: 'Anders' }])
  })

  test('Löschen trifft nur den gemeinten', () => {
    expect(removeShortcut([WIN_TAB, { id: 'b', label: 'B', keys: ['f5'] }], 'a')).toHaveLength(1)
  })

  test('neue Bezeichner wiederholen sich nicht', () => {
    // Assert — sonst überschriebe ein neuer Shortcut den vorigen.
    const ids = new Set(Array.from({ length: 50 }, () => makeShortcutId()))

    expect(ids.size).toBe(50)
  })
})
