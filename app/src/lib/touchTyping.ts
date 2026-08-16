/**
 * Was von einer echten Tastatur bei einem Gerät ankommt, das keine hat.
 *
 * <p>
 * **Der Befund dahinter (16.08.2026):** die App schickte jeden Anschlag als
 * Tastendruck hinaus — `keydown`/`keyup` mit dem Namen aus
 * `hardwareKeyboard.ts`. Ein Windows-Agent macht daraus einen Anschlag; ein
 * Handy kann das nicht, und es hat auch recht damit: `dispatchGesture` kennt
 * Berührungen, keine Tasten. Es antwortete deshalb mit „„e" gibt es auf einem
 * Handy nicht." — bei jedem einzelnen Buchstaben, den jemand tippen wollte.
 * </p>
 *
 * <p>
 * Der Fehler lag nicht drüben. Ein Buchstabe ist auf einem Handy kein
 * Tastendruck, sondern **Text**: er geht in das Feld, das gerade den Fokus hat.
 * Genau das wird hier entschieden — und zwar an dieser einen Stelle, damit
 * niemand sie in der Ereignisbehandlung wiederholt.
 * </p>
 */

/** Was aus einem Anschlag wird. */
export type TouchInput =
  /** Ein Zeichen für das fokussierte Feld. */
  | { kind: 'text'; text: string }
  /** Eine der wenigen Tasten, für die es drüben eine Entsprechung gibt. */
  | { kind: 'key'; key: string }

/**
 * Die Tasten, die ein Handy versteht — Gegenstück zu `pressKey` in
 * `host/RemoteInputService.kt`.
 *
 * Kurz, und das mit Absicht: was dort keine Entsprechung hat, gehört hier gar
 * nicht erst hinaus. Eine Taste zu schicken, damit die Gegenseite sie ablehnen
 * kann, ist keine Rückmeldung, sondern Lärm.
 */
const NAMED: Readonly<Record<string, string>> = {
  Enter: 'enter',
  Backspace: 'backspace',
  Escape: 'escape',
  Tab: 'tab',
}

/** Was der Anschlag interessiert: das Zeichen und die gehaltenen Modifier. */
export interface Anschlag {
  key: string
  ctrlKey: boolean
  altKey: boolean
  metaKey: boolean
}

/**
 * @returns `undefined` für alles, was drüben nicht ankommt — Funktionstasten,
 *   Pfeile, Tastenkombinationen. Es wird verschluckt und nicht geschickt: der
 *   Nutzer weiß, dass ein Handy kein F5 hat, und braucht dafür keine Zeile in
 *   der Statuszeile.
 */
export function touchInputFor(event: Anschlag): TouchInput | undefined {
  // Strg und Windows halten heißt: hier ist eine Kombination gemeint. Die gibt
  // es drüben nicht — außer Einfügen, und das behandelt der Aufrufer, weil es
  // die Zwischenablage braucht.
  if (event.ctrlKey || event.metaKey) {
    return undefined
  }

  // Ein einzelnes Zeichen ist ein Zeichen, auch mit Alt Gr: das @ einer
  // deutschen Tastatur entsteht so, und der Browser meldet es fertig.
  if ([...event.key].length === 1) {
    return { kind: 'text', text: event.key }
  }

  const named = NAMED[event.key]

  return named === undefined ? undefined : { kind: 'key', key: named }
}
