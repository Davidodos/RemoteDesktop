import {
  LAYOUTS,
  chordNameOf,
  pageLabel,
  type KeyCap,
  type KeyboardPage,
} from '../../lib/keyboardLayout.ts'

interface Props {
  page: KeyboardPage
  /** Hervorgehobene Tasten — festgestellte Modifier oder die gesammelte Kombination. */
  activeKeys: readonly string[]
  onPress: (cap: KeyCap) => void
}

/** Die Tastenfläche selbst. Entscheidet nichts, meldet nur, was gedrückt wurde. */
export function OnScreenKeyboard({ page, activeKeys, onPress }: Props): React.JSX.Element {
  return (
    <div className="onscreen-keyboard">
      {LAYOUTS[page].map((row, rowIndex) => (
        // Die Reihen einer Seite sind fest — der Index ist hier ein stabiler
        // Schlüssel.
        <div className="kbd-row" key={rowIndex}>
          {row.map((cap) => {
            const name = chordNameOf(cap)
            const active = name !== undefined && activeKeys.includes(name)

            return (
              <button
                key={cap.label}
                type="button"
                className={active ? 'kbd-key active' : 'kbd-key'}
                style={{ flexGrow: cap.span ?? 1 }}
                onClick={() => onPress(cap)}
              >
                {cap.page === true ? pageLabel(page) : cap.label}
              </button>
            )
          })}
        </div>
      ))}
    </div>
  )
}
