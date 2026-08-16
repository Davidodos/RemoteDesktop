import { useEffect, useState } from 'react'
import {
  describeHotkey,
  hotkeyFromEvent,
  isUsableHotkey,
  SUGGESTED_HOTKEY,
  type Hotkey,
} from '../lib/hotkey.ts'

interface Props {
  onChoose: (hotkey: Hotkey) => void
  /** Später. Gefragt wird dann beim nächsten Start wieder. */
  onLater: () => void
}

/**
 * Die eine Frage, die beim ersten Verbinden zu einem Rechner gestellt wird:
 * **womit gibst du die Kontrolle wieder zurück?**
 *
 * <p>
 * Sie steht vor der Übernahme und nicht in den Einstellungen, weil sie sich
 * hinterher nicht mehr stellen lässt: läuft die Übernahme erst, geht jeder
 * Anschlag zum anderen Rechner, und ein Kürzel, das man dann erst sucht, ist
 * keines. Danach steht es im Fenster unter „Einstellungen" und ist dort
 * änderbar.
 * </p>
 *
 * <p>
 * Gefragt wird durch Drücken und nicht durch eine Liste zum Auswählen: eine
 * Taste, die im eigenen Griff schon belegt ist, merkt man nur, wenn man sie
 * drückt.
 * </p>
 */
export function TakeoverSetup({ onChoose, onLater }: Props): React.JSX.Element {
  const [hotkey, setHotkey] = useState<Hotkey>(SUGGESTED_HOTKEY)
  const [caught, setCaught] = useState(false)
  const [hint, setHint] = useState<string | undefined>(undefined)

  useEffect(() => {
    const listen = (event: KeyboardEvent): void => {
      // Diese Karte liegt über allem; solange sie steht, gehört jeder Anschlag
      // ihr. Sonst tippte man den Griff nebenbei in den fernen Rechner.
      event.preventDefault()

      const pressed = hotkeyFromEvent(event)

      if (pressed === undefined) {
        return
      }

      if (!isUsableHotkey(pressed)) {
        setHint('Mindestens Strg, Alt oder die Windows-Taste — sonst fällt es beim Tippen.')
        return
      }

      setHotkey(pressed)
      setCaught(true)
      setHint(undefined)
    }

    window.addEventListener('keydown', listen, true)

    return () => window.removeEventListener('keydown', listen, true)
  }, [])

  return (
    <div className="request-overlay" role="presentation">
      <div
        className="overlay-card"
        role="dialog"
        aria-modal="true"
        aria-label="Kürzel für den Vollzugriff"
      >
        <h2>Vollzugriff auf einen anderen Rechner</h2>

        <p>
          Ein Kürzel schaltet ihn ein und wieder aus. Solange er läuft, gehen Maus und Tastatur
          vollständig hinüber — dieses Kürzel ist das Einzige, was hier bleibt.
        </p>

        <p className="pairing-code address">{describeHotkey(hotkey)}</p>

        <p className="device-hint" role="status">
          {hint ?? (caught ? 'Übernehmen oder eine andere Taste drücken.' : 'Jetzt drücken — oder den Vorschlag übernehmen.')}
        </p>

        <div className="device-rename-row">
          <button type="button" className="secondary" onClick={() => onChoose(hotkey)}>
            Übernehmen
          </button>

          <button type="button" className="secondary" onClick={onLater}>
            Später
          </button>
        </div>
      </div>
    </div>
  )
}
