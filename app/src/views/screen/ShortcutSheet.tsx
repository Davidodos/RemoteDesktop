import { useState } from 'react'
import type { InputChannel } from '../../lib/inputChannel.ts'
import { describeChord } from '../../lib/keys.ts'
import { loadShortcuts } from '../../lib/shortcuts.ts'

interface Props {
  input: InputChannel
}

/**
 * Die gespeicherten Tastenkombinationen als Knöpfe.
 *
 * Gelesen wird beim Einblenden — wer im Menü etwas ändert, sieht es beim
 * nächsten Öffnen.
 */
export function ShortcutSheet({ input }: Props): React.JSX.Element {
  const [shortcuts] = useState(loadShortcuts)

  return (
    <div className="shortcut-sheet">
      {shortcuts.length === 0 ? (
        <p className="now-playing-empty">
          Keine Shortcuts angelegt — im Menü unter „Shortcuts" geht das.
        </p>
      ) : (
        shortcuts.map((shortcut) => (
          <button
            key={shortcut.id}
            type="button"
            className="shortcut-button"
            onClick={() => {
              input.chord(shortcut.keys)
              navigator.vibrate?.(25)
            }}
          >
            <span className="device-name">{shortcut.label}</span>
            <span className="shortcut-keys">{describeChord(shortcut.keys)}</span>
          </button>
        ))
      )}
    </div>
  )
}
