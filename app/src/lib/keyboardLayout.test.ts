import { describe, expect, test } from 'vitest'
import {
  LAYOUTS,
  PAGES,
  chordNameOf,
  keyEffect,
  nextPage,
  pageLabel,
  type KeyCap,
} from './keyboardLayout.ts'
import { describeChord, labelForKey } from './keys.ts'

const ALL_KEYS: KeyCap[] = Object.values(LAYOUTS).flat(2)

describe('Aufbau der Bildschirmtastatur', () => {
  test('jede Seite hat gleich viele Reihen', () => {
    // Assert — sonst springt das Bild beim Seitenwechsel.
    const rowCounts = Object.values(LAYOUTS).map((page) => page.length)

    expect(new Set(rowCounts).size).toBe(1)
  })

  test('jede Taste tut genau eine Sache', () => {
    for (const cap of ALL_KEYS) {
      const roles = [cap.char, cap.key, cap.modifier, cap.page].filter(
        (entry) => entry !== undefined,
      )

      expect(roles, cap.label).toHaveLength(1)
    }
  })

  test('jede Seite hat einen Knopf zum Weiterblättern', () => {
    // Assert — sonst käme man von der Seite nicht mehr weg.
    for (const page of Object.values(LAYOUTS)) {
      expect(page.flat().filter((cap) => cap.page === true)).toHaveLength(1)
    }
  })

  test('der Seitenknopf blättert im Kreis', () => {
    // Act
    const round = PAGES.map((_, index) =>
      // Arrange — von 'abc' aus so oft weiter, wie es Seiten gibt.
      Array.from({ length: index }).reduce<'abc' | 'sym' | 'fn'>(
        (current) => nextPage(current),
        'abc',
      ),
    )

    // Assert
    expect(round).toEqual(PAGES)
    expect(nextPage(PAGES[PAGES.length - 1]!)).toBe('abc')
  })

  test('die Beschriftung zählt die Seiten durch', () => {
    expect(pageLabel('abc')).toBe('1/3')
    expect(pageLabel('fn')).toBe('3/3')
  })

  test('der Seitenknopf löst keine Eingabe aus', () => {
    // Assert — sonst tippte ein Seitenwechsel etwas auf dem Rechner.
    expect(keyEffect({ label: 'Seite', page: true }, ['ctrl'])).toBeUndefined()
    expect(chordNameOf({ label: 'Seite', page: true })).toBeUndefined()
  })

  test('keine Reihe wird unbedienbar schmal', () => {
    // Arrange — mehr als zwölf Tasten nebeneinander trifft niemand mehr.
    for (const page of Object.values(LAYOUTS)) {
      for (const row of page) {
        expect(row.length).toBeLessThanOrEqual(12)
      }
    }
  })
})

describe('Wirkung eines Tastendrucks', () => {
  const q: KeyCap = { label: 'q', char: 'q' }
  const euro: KeyCap = { label: '€', char: '€' }
  const escape: KeyCap = { label: 'Esc', key: 'escape' }

  test('Buchstabe ohne Modifier wird als Text getippt', () => {
    // Act
    const effect = keyEffect(q, [])

    // Assert — als Unicode, damit es unabhängig vom Layout des Rechners ankommt.
    expect(effect).toEqual({ kind: 'text', text: 'q' })
  })

  test('mit Shift kommt der Großbuchstabe', () => {
    expect(keyEffect(q, ['shift'])).toEqual({ kind: 'text', text: 'Q' })
  })

  test('mit Strg wird daraus eine echte Tastenkombination', () => {
    // Assert — getippter Text kennt kein „mit Strg".
    expect(keyEffect(q, ['ctrl'])).toEqual({ kind: 'combo', key: 'q', mods: ['ctrl'] })
  })

  test('Sondertaste nimmt die festgestellten Modifier mit', () => {
    expect(keyEffect(escape, ['ctrl', 'shift'])).toEqual({
      kind: 'combo',
      key: 'escape',
      mods: ['ctrl', 'shift'],
    })
  })

  test('Zeichen ohne eigene Taste wird trotz Modifier als Text getippt', () => {
    // Arrange — Strg+€ gibt es nicht, das Zeichen soll aber ankommen.
    expect(keyEffect(euro, ['ctrl'])).toEqual({ kind: 'text', text: '€' })
  })
})

describe('Tasten in eine Kombination aufnehmen', () => {
  test('Modifier und Sondertasten haben einen Namen', () => {
    expect(chordNameOf({ label: 'Strg', modifier: 'ctrl' })).toBe('ctrl')
    expect(chordNameOf({ label: 'F5', key: 'f5' })).toBe('f5')
    expect(chordNameOf({ label: 'q', char: 'q' })).toBe('q')
  })

  test('Zeichen ohne Entsprechung lassen sich nicht kombinieren', () => {
    expect(chordNameOf({ label: '€', char: '€' })).toBeUndefined()
    expect(chordNameOf({ label: 'Leer', char: ' ' })).toBeUndefined()
  })

  test('die Vorschau nutzt die Beschriftung der Tastatur', () => {
    // Assert — „Strg" statt „CTRL".
    expect(labelForKey('ctrl')).toBe('Strg')
    expect(describeChord(['ctrl', 'shift', 'escape'])).toBe('Strg + ⇧ + Esc')
  })
})
