/**
 * Das Kürzel, mit dem der Rechner die Kontrolle abgibt und wieder übernimmt.
 *
 * <p>
 * Es ist die eine Taste, die **nicht** hinausgeht. Solange die Übernahme läuft,
 * wandert jeder Anschlag zum anderen Rechner — auch Alt+Tab, auch die
 * Windows-Taste. Ohne einen Griff, der garantiert hier bleibt, käme man aus
 * dieser Lage nur noch über den Ausschalter heraus. Deshalb wird er einmal
 * abgefragt und danach nie wieder stillschweigend geändert.
 * </p>
 *
 * <p>
 * Gemerkt wird `KeyboardEvent.code` und nicht `key` — dieselbe Entscheidung wie
 * in `hardwareKeyboard.ts` und aus demselben Grund: `code` sagt, welche Taste
 * unter dem Finger liegt, unabhängig von Layout und Modifiern. Bei `key` wäre
 * aus Strg+Alt+Q auf einer deutschen Tastatur ein `@` geworden, und das Kürzel
 * hinge davon ab, in welcher Sprache jemand gerade schreibt.
 * </p>
 */

export interface Hotkey {
  ctrl: boolean
  alt: boolean
  shift: boolean
  /** Die Windows-Taste. */
  meta: boolean
  /** `KeyboardEvent.code`, etwa `KeyK` oder `F9`. */
  code: string
}

/**
 * Der Vorschlag, der im Feld steht, wenn zum ersten Mal danach gefragt wird.
 *
 * Zwei Modifier, damit er nicht versehentlich fällt, und ein Buchstabe, auf dem
 * unter Windows nichts Bekanntes liegt. Übernehmen muss ihn trotzdem jemand —
 * eine Vorgabe, die niemand gesehen hat, ist keine.
 */
export const SUGGESTED_HOTKEY: Hotkey = {
  ctrl: true,
  alt: true,
  shift: false,
  meta: false,
  code: 'KeyK',
}

/** Tasten, die für sich genommen kein Kürzel ergeben. */
const MODIFIER_CODES = new Set([
  'ControlLeft',
  'ControlRight',
  'AltLeft',
  'AltRight',
  'ShiftLeft',
  'ShiftRight',
  'MetaLeft',
  'MetaRight',
])

/**
 * Was gerade gedrückt wurde, als Kürzel.
 *
 * @returns `undefined`, solange nur Modifier liegen — beim Greifen nach
 *   Strg+Alt+K ist das der Normalfall und kein Fehler.
 */
export function hotkeyFromEvent(event: KeyboardEvent): Hotkey | undefined {
  if (MODIFIER_CODES.has(event.code) || event.code.length === 0) {
    return undefined
  }

  return {
    ctrl: event.ctrlKey,
    alt: event.altKey,
    shift: event.shiftKey,
    meta: event.metaKey,
    code: event.code,
  }
}

/** Ob dieser Anschlag genau dieses Kürzel ist. */
export function hotkeyMatches(event: KeyboardEvent, hotkey: Hotkey): boolean {
  return (
    event.code === hotkey.code &&
    event.ctrlKey === hotkey.ctrl &&
    event.altKey === hotkey.alt &&
    event.shiftKey === hotkey.shift &&
    event.metaKey === hotkey.meta
  )
}

/**
 * Ein Kürzel ohne Modifier ist eine Falle: es fiele mitten im Tippen, und die
 * Übernahme schaltete sich in einem Textfeld ab. Verlangt wird deshalb
 * mindestens einer.
 */
export function isUsableHotkey(hotkey: Hotkey): boolean {
  return hotkey.ctrl || hotkey.alt || hotkey.meta
}

/** Wie es dasteht: „Strg+Alt+K". */
export function describeHotkey(hotkey: Hotkey): string {
  const parts: string[] = []

  if (hotkey.ctrl) {
    parts.push('Strg')
  }

  if (hotkey.alt) {
    parts.push('Alt')
  }

  if (hotkey.shift) {
    parts.push('Umschalt')
  }

  if (hotkey.meta) {
    parts.push('Windows')
  }

  parts.push(describeCode(hotkey.code))

  return parts.join('+')
}

/** Der Tastenname für Menschen — `KeyK` ist keiner. */
function describeCode(code: string): string {
  if (/^Key[A-Z]$/.test(code)) {
    return code.slice(3)
  }

  if (/^Digit[0-9]$/.test(code)) {
    return code.slice(5)
  }

  if (/^Numpad[0-9]$/.test(code)) {
    return `Num ${code.slice(6)}`
  }

  const named: Record<string, string> = {
    Escape: 'Esc',
    Enter: 'Eingabe',
    NumpadEnter: 'Num Eingabe',
    Space: 'Leertaste',
    Tab: 'Tab',
    Backspace: 'Rücktaste',
    Delete: 'Entf',
    Insert: 'Einfg',
    Home: 'Pos1',
    End: 'Ende',
    PageUp: 'Bild ↑',
    PageDown: 'Bild ↓',
    ScrollLock: 'Rollen',
    Pause: 'Pause',
  }

  return named[code] ?? code
}

/**
 * Für die Ablage: `ctrl+alt+KeyK`.
 *
 * Eine Zeichenkette und kein JSON, weil sie am Rechner in einer Datei landet,
 * die auch ein Mensch aufmachen kann — siehe `setup/HotkeyFile.cs`.
 */
export function serializeHotkey(hotkey: Hotkey): string {
  const parts: string[] = []

  if (hotkey.ctrl) {
    parts.push('ctrl')
  }

  if (hotkey.alt) {
    parts.push('alt')
  }

  if (hotkey.shift) {
    parts.push('shift')
  }

  if (hotkey.meta) {
    parts.push('meta')
  }

  parts.push(hotkey.code)

  return parts.join('+')
}

/** @returns `undefined`, wenn dort nichts Brauchbares stand. */
export function parseHotkey(raw: string | undefined): Hotkey | undefined {
  if (raw === undefined) {
    return undefined
  }

  const parts = raw.trim().split('+').filter((part) => part.length > 0)
  const code = parts.pop()

  if (code === undefined || MODIFIER_CODES.has(code)) {
    return undefined
  }

  const named = new Set(parts.map((part) => part.toLowerCase()))

  const hotkey: Hotkey = {
    ctrl: named.has('ctrl'),
    alt: named.has('alt'),
    shift: named.has('shift'),
    meta: named.has('meta'),
    code,
  }

  return isUsableHotkey(hotkey) ? hotkey : undefined
}
