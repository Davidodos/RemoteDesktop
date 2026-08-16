import { describe, expect, it } from 'vitest'
import {
  describeHotkey,
  hotkeyFromEvent,
  hotkeyMatches,
  isUsableHotkey,
  parseHotkey,
  serializeHotkey,
  SUGGESTED_HOTKEY,
  type Hotkey,
} from './hotkey.ts'

/** Ein Anschlag, wie ihn der Browser meldet. */
function press(code: string, modifiers: Partial<Record<'ctrl' | 'alt' | 'shift' | 'meta', boolean>> = {}): KeyboardEvent {
  return new KeyboardEvent('keydown', {
    code,
    ctrlKey: modifiers.ctrl ?? false,
    altKey: modifiers.alt ?? false,
    shiftKey: modifiers.shift ?? false,
    metaKey: modifiers.meta ?? false,
  })
}

describe('hotkeyFromEvent', () => {
  it('nimmt Modifier und Taste, wie sie liegen', () => {
    expect(hotkeyFromEvent(press('KeyK', { ctrl: true, alt: true }))).toEqual(SUGGESTED_HOTKEY)
  })

  /**
   * Beim Greifen nach Strg+Alt+K kommt zuerst Strg allein an. Würde das schon
   * als Kürzel gelten, stünde in der Abfrage „Strg" — und die Übernahme fiele
   * danach bei jedem Kopieren.
   */
  it('ein Modifier allein ist noch kein Kürzel', () => {
    expect(hotkeyFromEvent(press('ControlLeft', { ctrl: true }))).toBeUndefined()
  })
})

describe('hotkeyMatches', () => {
  it('trifft nur bei genau denselben Modifiern', () => {
    expect(hotkeyMatches(press('KeyK', { ctrl: true, alt: true }), SUGGESTED_HOTKEY)).toBe(true)
    expect(hotkeyMatches(press('KeyK', { ctrl: true }), SUGGESTED_HOTKEY)).toBe(false)
  })

  /** Ein zusätzlich gehaltenes Umschalt ist ein anderer Griff. */
  it('mehr Modifier als vereinbart trifft nicht', () => {
    expect(
      hotkeyMatches(press('KeyK', { ctrl: true, alt: true, shift: true }), SUGGESTED_HOTKEY),
    ).toBe(false)
  })
})

describe('isUsableHotkey', () => {
  it('verlangt mindestens einen Modifier', () => {
    expect(isUsableHotkey({ ctrl: false, alt: false, shift: true, meta: false, code: 'KeyK' })).toBe(
      false,
    )
  })

  it('Strg genügt', () => {
    expect(isUsableHotkey({ ...SUGGESTED_HOTKEY, alt: false })).toBe(true)
  })
})

describe('serialize und parse', () => {
  it('kommen bei demselben Kürzel wieder heraus', () => {
    const hotkey: Hotkey = { ctrl: true, alt: false, shift: true, meta: false, code: 'F9' }

    expect(parseHotkey(serializeHotkey(hotkey))).toEqual(hotkey)
  })

  it('schreibt eine Zeile, die ein Mensch lesen kann', () => {
    expect(serializeHotkey(SUGGESTED_HOTKEY)).toBe('ctrl+alt+KeyK')
  })

  it('nichts gespeichert heißt: noch nicht vergeben', () => {
    expect(parseHotkey(undefined)).toBeUndefined()
    expect(parseHotkey('')).toBeUndefined()
  })

  /**
   * Eine Datei, die auch von Hand beschreibbar ist, enthält irgendwann Unsinn.
   * Ein Kürzel ohne Modifier fiele beim Tippen — dann lieber keins.
   */
  it('verwirft, was ohne Modifier dasteht', () => {
    expect(parseHotkey('KeyK')).toBeUndefined()
  })
})

describe('describeHotkey', () => {
  it('schreibt aus, was auf den Tasten steht', () => {
    expect(describeHotkey(SUGGESTED_HOTKEY)).toBe('Strg+Alt+K')
  })

  it('kennt auch die Tasten ohne Aufdruck', () => {
    expect(
      describeHotkey({ ctrl: false, alt: true, shift: false, meta: false, code: 'PageUp' }),
    ).toBe('Alt+Bild ↑')
  })
})
