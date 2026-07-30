/**
 * Übersetzt die Eingaben der Handy-Tastatur in Befehle für den Agent.
 *
 * Android-Tastaturen liefern bei `keydown` grundsätzlich den Platzhalter-Code
 * 229 und verraten die getippte Taste nicht — auswerten lässt sich nur das
 * `beforeinput`-Ereignis. Dessen `inputType` sagt, was passieren soll, `data`
 * enthält den eingefügten Text.
 *
 * Jeder Anschlag geht sofort raus, auch die Rücktaste. Nur so lässt sich
 * bereits Getipptes wieder löschen, ohne auf die andere Tastatur zu wechseln.
 */

/** Was ein Tastendruck auf dem Handy auf dem entfernten Rechner auslöst. */
export type KeyAction =
  | { kind: 'text'; text: string }
  | { kind: 'key'; key: string }
  | { kind: 'chord'; keys: string[] }

export function interpretInput(inputType: string, data: string | null): KeyAction[] {
  switch (inputType) {
    case 'insertText':
    case 'insertFromPaste':
      return data === null || data.length === 0 ? [] : [{ kind: 'text', text: data }]

    case 'insertLineBreak':
    case 'insertParagraph':
      return [{ kind: 'key', key: 'enter' }]

    case 'deleteContentBackward':
      return [{ kind: 'key', key: 'backspace' }]

    case 'deleteContentForward':
      return [{ kind: 'key', key: 'delete' }]

    // Gboard schickt das beim Wischen über die Rücktaste.
    case 'deleteWordBackward':
      return [{ kind: 'chord', keys: ['ctrl', 'backspace'] }]

    case 'deleteWordForward':
      return [{ kind: 'chord', keys: ['ctrl', 'delete'] }]

    /*
      Bewusst nichts:

      `insertCompositionText` ist ein Zwischenstand beim Wischen über die
      Tastatur — das Wort wird bei jedem Buchstaben neu geschickt und käme
      vielfach an. Das fertige Wort holt der Aufrufer stattdessen bei
      `compositionend` ab.

      `insertReplacementText` ist die Autokorrektur. Die getippten Buchstaben
      sind längst auf dem Rechner; die Korrektur hinterherzuschicken ergäbe
      „helloHello".
    */
    default:
      return []
  }
}

/**
 * Füllzeichen für das Eingabefeld.
 *
 * Ohne Zeichen links vom Textcursor melden manche Tastaturen die Rücktaste
 * überhaupt nicht — sie sehen nichts zu löschen und schlucken den Druck. Das
 * Feld hält deshalb immer denselben unsichtbaren Vorrat, der nach jedem
 * Ereignis wiederhergestellt wird.
 */
export const KEYBOARD_PADDING = '\u200b'.repeat(16)
