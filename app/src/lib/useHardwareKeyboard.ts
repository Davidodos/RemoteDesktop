import { useEffect, type Dispatch, type MutableRefObject, type SetStateAction } from 'react'
import { belongsToRemote, toAgentKey } from './hardwareKeyboard.ts'
import { hotkeyMatches, type Hotkey } from './hotkey.ts'
import type { InputChannel } from './inputChannel.ts'
import { touchInputFor } from './touchTyping.ts'
import type { Device } from './types.ts'
import { getPlatform } from '../platform/index.ts'

interface Options {
  selected: Device | undefined
  inputRef: MutableRefObject<InputChannel | undefined>
  hotkey: Hotkey | undefined
  takeover: boolean
  setTakeover: Dispatch<SetStateAction<boolean>>
  /** Ob am anderen Ende ein Handy sitzt — dort ist ein Buchstabe Text, kein Anschlag. */
  touchTarget: boolean
  onError: (message: string) => void
}

/**
 * Die echte Tastatur — vier Fälle, in dieser Reihenfolge.
 *
 * <ol>
 * <li><b>Das Umschaltkürzel.</b> Es bleibt immer hier. Ginge es mit hinaus,
 *   gäbe es aus der Übernahme keinen Weg zurück.</li>
 * <li><b>Übernahme.</b> Alles hinaus, auch aus Eingabefeldern heraus — es
 *   gibt in diesem Zustand keine eigenen mehr, das Bild füllt den
 *   Bildschirm.</li>
 * <li><b>Ein Handy.</b> Dort ist ein Buchstabe kein Anschlag, sondern Text.
 *   Siehe `touchTyping.ts`.</li>
 * <li><b>Ein Rechner ohne Übernahme.</b> Was in ein Feld dieser App gehört,
 *   bleibt hier.</li>
 * </ol>
 *
 * Aus `App.tsx` herausgezogen (Durchsicht D3); die Fälle sind dieselben.
 */
export function useHardwareKeyboard({
  selected,
  inputRef,
  hotkey,
  takeover,
  setTakeover,
  touchTarget,
  onError,
}: Options): void {
  useEffect(() => {
    const input = inputRef.current
    const platform = getPlatform()

    if (selected === undefined || input === undefined || !platform.capabilities.physicalKeyboard) {
      return
    }

    const forward = (event: KeyboardEvent, down: boolean): void => {
      if (hotkey !== undefined && hotkeyMatches(event, hotkey)) {
        event.preventDefault()

        // Nur beim Drücken: das Loslassen desselben Griffs schaltete ihn sonst
        // sofort wieder zurück.
        if (down) {
          setTakeover((running) => !running)
        }

        return
      }

      if (takeover) {
        event.preventDefault()

        const key = toAgentKey(event.code)

        if (key === undefined) {
          return
        }

        if (down) {
          input.keyDown(key)
        } else {
          input.keyUp(key)
        }

        return
      }

      if (!belongsToRemote(event.target)) {
        return
      }

      if (touchTarget) {
        forwardToTouch(event, down)
        return
      }

      const key = toAgentKey(event.code)

      if (key === undefined) {
        return
      }

      // Sonst löst der Browser seine eigenen Kürzel aus — Strg+W schlösse das
      // Fenster, statt am Zielrechner einen Tab zu schließen.
      event.preventDefault()

      if (down) {
        input.keyDown(key)
      } else {
        input.keyUp(key)
      }
    }

    /**
     * Ein Handy nimmt Text an, keine Anschläge. Zwei Sonderfälle stehen hier
     * und nicht in `touchTyping.ts`: das Einfügen braucht die Zwischenablage,
     * und die gibt es nur über die Plattform.
     */
    const forwardToTouch = (event: KeyboardEvent, down: boolean): void => {
      if (!down) {
        // Geschickt wird beim Drücken. Ein zweites Mal beim Loslassen wäre
        // jeder Buchstabe doppelt.
        return
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'v') {
        event.preventDefault()

        void platform.clipboard.readText().then(
          (text) => text.length > 0 && input.typeText(text),
          () =>
            onError(
              'Die Zwischenablage ließ sich nicht lesen. Einmal ins Fenster klicken und '
              + 'erneut einfügen.',
            ),
        )

        return
      }

      const touch = touchInputFor(event)

      // Auch das Verschluckte wird abgefangen: F5 soll nicht nebenbei dieses
      // Fenster neu laden, nur weil drüben nichts damit anzufangen ist.
      event.preventDefault()

      if (touch === undefined) {
        return
      }

      if (touch.kind === 'text') {
        input.typeText(touch.text)
      } else {
        input.combo(touch.key)
      }
    }

    const onDown = (event: KeyboardEvent): void => forward(event, true)
    const onUp = (event: KeyboardEvent): void => forward(event, false)

    window.addEventListener('keydown', onDown)
    window.addEventListener('keyup', onUp)

    return () => {
      window.removeEventListener('keydown', onDown)
      window.removeEventListener('keyup', onUp)
    }
  }, [selected, hotkey, takeover, touchTarget, inputRef, setTakeover, onError])
}
