import { useEffect, useRef } from 'react'
import type { InputChannel } from '../../lib/inputChannel.ts'
import { KEYBOARD_PADDING, interpretInput, type KeyAction } from '../../lib/softKeyboard.ts'

interface Props {
  input: InputChannel
}

/**
 * Die Handy-Tastatur, direkt auf den Rechner geschaltet.
 *
 * Jeder Anschlag geht sofort raus — auch die Rücktaste. Ein Feld zum Sammeln
 * mit Senden-Knopf wäre für längere Texte bequemer, aber dann ließe sich
 * bereits Gesendetes nicht mehr löschen, ohne auf die Bildschirmtastatur zu
 * wechseln.
 *
 * Das Feld selbst bleibt leer: es dient nur dazu, die Tastatur zu öffnen und
 * ihre Ereignisse abzufangen.
 */
export function TextSheet({ input }: Props): React.JSX.Element {
  const fieldRef = useRef<HTMLTextAreaElement>(null)

  const perform = (action: KeyAction): void => {
    switch (action.kind) {
      case 'text':
        input.typeText(action.text)
        break

      case 'key':
        input.combo(action.key)
        break

      case 'chord':
        input.chord(action.keys)
        break
    }
  }

  // Der Handler wird bei jedem Render neu gebaut, die Listener bleiben aber
  // hängen — deshalb geht der Aufruf über diesen stets frischen Verweis.
  const handlerRef = useRef(perform)

  useEffect(() => {
    handlerRef.current = perform
  })

  // Bewusst native Listener statt React-Ereignisse: Android-Tastaturen liefern
  // die Rücktaste ausschließlich als `beforeinput`, und Reacts eigene
  // Aufbereitung deckt nicht jede Eingabeart ab.
  useEffect(() => {
    const field = fieldRef.current

    if (field === null) {
      return
    }

    field.focus()

    const onBeforeInput = (event: Event): void => {
      const { inputType, data } = event as InputEvent

      for (const action of interpretInput(inputType ?? 'insertText', data)) {
        handlerRef.current(action)
      }
    }

    /**
     * Beim Wischen über die Tastatur steht das Wort erst hier fest — die
     * Zwischenstände werden verworfen.
     */
    const onCompositionEnd = (event: Event): void => {
      const text = (event as CompositionEvent).data

      if (text.length > 0) {
        handlerRef.current({ kind: 'text', text })
      }
    }

    /**
     * Stellt den unsichtbaren Vorrat wieder her und setzt den Textcursor ans
     * Ende. Ohne das wäre das Feld nach ein paar Löschvorgängen leer und die
     * Rücktaste käme gar nicht mehr an.
     */
    const restorePadding = (): void => {
      field.value = KEYBOARD_PADDING
      field.setSelectionRange(KEYBOARD_PADDING.length, KEYBOARD_PADDING.length)
    }

    field.addEventListener('beforeinput', onBeforeInput)
    field.addEventListener('compositionend', onCompositionEnd)
    field.addEventListener('input', restorePadding)

    return () => {
      field.removeEventListener('beforeinput', onBeforeInput)
      field.removeEventListener('compositionend', onCompositionEnd)
      field.removeEventListener('input', restorePadding)
    }
  }, [])

  return (
    <div className="text-sheet">
      <div className="text-field">
        <textarea
          ref={fieldRef}
          defaultValue={KEYBOARD_PADDING}
          rows={1}
          // Autokorrektur würde ganze Wörter ersetzen, die längst auf dem
          // Rechner stehen — hier zählt der einzelne Anschlag.
          autoCapitalize="off"
          autoCorrect="off"
          autoComplete="off"
          spellCheck={false}
          aria-label="Tastatureingabe an den Rechner"
        />
      </div>
    </div>
  )
}
