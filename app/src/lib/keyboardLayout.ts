/**
 * Die eigene Bildschirmtastatur.
 *
 * Statt der Handy-Tastatur, weil die sich beim Öffnen über das halbe Display
 * schiebt und ihre Höhe niemand kennt: eine eigene Tastatur ist immer gleich
 * hoch, ihre Seiten wechseln ohne dass sich das Bild darüber bewegt, und sie
 * kann Tasten anbieten, die es auf einem Handy gar nicht gibt.
 */

export type KeyboardPage = 'abc' | 'sym' | 'fn'

/** Eine Taste auf der Bildschirmtastatur. */
export interface KeyCap {
  label: string
  /** Zeichen, das getippt wird — für Buchstaben und Satzzeichen. */
  char?: string
  /** Tastenname des Protokolls, z.B. `escape` oder `f5`. */
  key?: string
  /** Modifier, der beim Antippen feststellt statt zu senden. */
  modifier?: string
  /** Blättert zur nächsten Seite. Die Beschriftung entsteht erst beim Zeichnen. */
  page?: true
  /** Breite als Vielfaches einer Standardtaste. */
  span?: number
}

/** Reihenfolge, in der der Seitenknopf blättert. */
export const PAGES: KeyboardPage[] = ['abc', 'sym', 'fn']

const char = (value: string, label = value): KeyCap => ({ label, char: value })
const key = (name: string, label: string, span?: number): KeyCap =>
  span === undefined ? { label, key: name } : { label, key: name, span }
const mod = (name: string, label: string): KeyCap => ({ label, modifier: name })

/**
 * Der Seitenwechsel sitzt auf der Tastatur statt in einer eigenen Leiste — eine
 * Reihe weniger, und der Daumen ist ohnehin dort unten.
 */
const PAGE_KEY: KeyCap = { label: 'Seite', page: true, span: 1.3 }

const SPACE: KeyCap = { label: 'Leer', char: ' ', span: 3 }
const BACKSPACE = key('backspace', '⌫', 1.5)
const ENTER = key('enter', '⏎', 1.5)

/**
 * QWERTZ, weil der Rechner deutsch eingestellt ist. Jede Seite hat genau vier
 * Reihen — dadurch bleibt die Tastatur beim Seitenwechsel gleich hoch.
 */
export const LAYOUTS: Record<KeyboardPage, KeyCap[][]> = {
  abc: [
    'qwertzuiopü'.split('').map((letter) => char(letter)),
    'asdfghjklöä'.split('').map((letter) => char(letter)),
    [
      mod('shift', '⇧'),
      ...'yxcvbnm'.split('').map((letter) => char(letter)),
      char(','),
      char('.'),
      BACKSPACE,
    ],
    [PAGE_KEY, mod('ctrl', 'Strg'), mod('alt', 'Alt'), mod('win', 'Win'), SPACE, ENTER],
  ],

  sym: [
    '1234567890'.split('').map((digit) => char(digit)),
    '!"§$%&/()='.split('').map((symbol) => char(symbol)),
    ['?', 'ß', '+', '-', '_', '#', '*', "'", ':'].map((symbol) => char(symbol)).concat(BACKSPACE),
    [
      PAGE_KEY,
      mod('ctrl', 'Strg'),
      mod('alt', 'Alt'),
      char(';'),
      char('@'),
      char('€'),
      { label: 'Leer', char: ' ', span: 2 },
      ENTER,
    ],
  ],

  fn: [
    [
      key('escape', 'Esc'),
      key('tab', 'Tab'),
      key('insert', 'Einfg'),
      key('delete', 'Entf'),
      key('home', 'Pos1'),
      key('end', 'Ende'),
    ],
    [
      key('pageup', 'Bild↑'),
      key('pagedown', 'Bild↓'),
      key('arrowleft', '←'),
      key('arrowup', '↑'),
      key('arrowdown', '↓'),
      key('arrowright', '→'),
    ],
    Array.from({ length: 6 }, (_, index) => key(`f${index + 1}`, `F${index + 1}`)),
    [PAGE_KEY, ...Array.from({ length: 6 }, (_, index) => key(`f${index + 7}`, `F${index + 7}`))],
  ],
}

/** Was ein Tastendruck auslöst. */
export type KeyEffect =
  | { kind: 'text'; text: string }
  | { kind: 'combo'; key: string; mods: string[] }

/** Zeichen, die der Agent auch als Taste kennt — nur die taugen für Kombinationen. */
function keyNameOf(value: string): string | undefined {
  return /^[a-z0-9]$/i.test(value) ? value.toLowerCase() : undefined
}

/**
 * Übersetzt einen Tastendruck in einen Befehl.
 *
 * Zeichen gehen als Unicode-Text raus — nur so kommen Umlaute und das Eurozeichen
 * unabhängig vom Tastaturlayout des Rechners an. Sobald aber ein Modifier
 * feststeckt, muss es eine echte Taste sein, denn getippter Text kennt kein
 * „mit Strg".
 */
export function keyEffect(cap: KeyCap, mods: readonly string[]): KeyEffect | undefined {
  if (cap.page === true) {
    return undefined
  }

  if (cap.key !== undefined) {
    return { kind: 'combo', key: cap.key, mods: [...mods] }
  }

  if (cap.char === undefined) {
    return undefined
  }

  const shifted = mods.includes('shift')
  const others = mods.filter((entry) => entry !== 'shift')
  const name = keyNameOf(cap.char)

  if (others.length > 0 && name !== undefined) {
    return { kind: 'combo', key: name, mods: [...mods] }
  }

  // Strg+€ gibt es nicht — dann zählt das Zeichen, der Modifier fällt weg.
  return { kind: 'text', text: shifted ? cap.char.toUpperCase() : cap.char }
}

/** Die Seite nach der aktuellen — der Seitenknopf blättert im Kreis. */
export function nextPage(current: KeyboardPage): KeyboardPage {
  return PAGES[(PAGES.indexOf(current) + 1) % PAGES.length]!
}

/** Beschriftung des Seitenknopfs, z.B. „2/3". */
export function pageLabel(current: KeyboardPage): string {
  return `${PAGES.indexOf(current) + 1}/${PAGES.length}`
}

/** Der Tastenname, unter dem eine Taste in eine Kombination aufgenommen wird. */
export function chordNameOf(cap: KeyCap): string | undefined {
  return cap.modifier ?? cap.key ?? (cap.char === undefined ? undefined : keyNameOf(cap.char))
}
