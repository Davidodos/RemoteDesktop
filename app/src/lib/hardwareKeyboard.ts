/**
 * Übersetzt Anschläge einer echten Tastatur in die Tastennamen des Agents.
 *
 * Ausgewertet wird `KeyboardEvent.code`, nicht `key`: `code` sagt, *welche
 * Taste* gedrückt wurde, unabhängig von Layout und Modifiern. Bei `key` wäre
 * aus Strg+Alt+Q auf einer deutschen Tastatur ein `@` geworden — der Agent
 * bekäme dann eine Taste, die niemand gedrückt hat.
 *
 * Der Agent löst die Namen in `agent/Native/VirtualKeys.cs` auf; was er dort
 * nicht kennt, wird hier gar nicht erst geschickt.
 */

const NAMED: Readonly<Record<string, string>> = {
  ControlLeft: 'ctrl',
  ControlRight: 'ctrl',
  ShiftLeft: 'shift',
  ShiftRight: 'shift',
  AltLeft: 'alt',
  // AltGr meldet Chromium als AltRight. Am Zielrechner ist das ebenfalls die
  // rechte Alt-Taste, also stimmt die Übersetzung — nur die Sonderzeichen
  // darüber entstehen dort und nicht hier.
  AltRight: 'alt',
  MetaLeft: 'win',
  MetaRight: 'win',

  Escape: 'escape',
  Tab: 'tab',
  Enter: 'enter',
  NumpadEnter: 'enter',
  Space: 'space',
  Backspace: 'backspace',
  Delete: 'delete',
  Insert: 'insert',
  Home: 'home',
  End: 'end',
  PageUp: 'pageup',
  PageDown: 'pagedown',
  CapsLock: 'capslock',
  PrintScreen: 'printscreen',

  ArrowUp: 'arrowup',
  ArrowDown: 'arrowdown',
  ArrowLeft: 'arrowleft',
  ArrowRight: 'arrowright',
}

/** `undefined` heißt: diese Taste geht nicht hinaus. */
export function toAgentKey(code: string): string | undefined {
  const named = NAMED[code]

  if (named !== undefined) {
    return named
  }

  if (/^Key[A-Z]$/.test(code)) {
    return code.slice(3).toLowerCase()
  }

  // Digit1 ist die Zeile über den Buchstaben, Numpad1 der Nummernblock. Beide
  // liefern dieselbe Ziffer — der Unterschied interessiert am Zielrechner nicht.
  if (/^(Digit|Numpad)[0-9]$/.test(code)) {
    return code.slice(-1)
  }

  if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code)) {
    return code.toLowerCase()
  }

  return undefined
}

/**
 * Ob dieser Anschlag überhaupt an den Zielrechner gehört.
 *
 * Tippt jemand gerade in ein Eingabefeld der App — den Kopplungscode etwa —,
 * dann meint er dieses Feld und nicht den fernen Rechner. Ohne diese Prüfung
 * ließe sich kein einziges Formular mehr ausfüllen.
 */
export function belongsToRemote(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) {
    return true
  }

  if (target.closest('input, textarea, select, [contenteditable="true"]') !== null) {
    return false
  }

  return true
}
