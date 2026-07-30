import { useState } from 'react'
import { chordNameOf, nextPage, type KeyCap, type KeyboardPage } from '../lib/keyboardLayout.ts'
import { describeChord, toggleChordKey } from '../lib/keys.ts'
import {
  loadShortcuts,
  makeShortcutId,
  removeShortcut,
  saveShortcuts,
  upsertShortcut,
  type Shortcut,
} from '../lib/shortcuts.ts'
import { OnScreenKeyboard } from './keyboard/OnScreenKeyboard.tsx'

/** Der Shortcut, der gerade bearbeitet wird. */
interface Draft {
  id: string
  label: string
  keys: string[]
}

/**
 * Verwaltung der eigenen Tastenkombinationen.
 *
 * Die Tasten werden auf der Bildschirmtastatur ausgewählt statt getippt: so
 * kann nur entstehen, was der Agent auch kennt, und niemand muss wissen, dass
 * die Windows-Taste im Protokoll `win` heißt.
 */
export function ShortcutsView(): React.JSX.Element {
  const [shortcuts, setShortcuts] = useState<Shortcut[]>(loadShortcuts)
  const [draft, setDraft] = useState<Draft | undefined>(undefined)
  const [page, setPage] = useState<KeyboardPage>('abc')

  const store = (next: Shortcut[]): void => {
    setShortcuts(next)
    saveShortcuts(next)
  }

  const collect = (cap: KeyCap): void => {
    if (draft === undefined) {
      return
    }

    if (cap.page === true) {
      setPage(nextPage(page))
      return
    }

    const name = chordNameOf(cap)

    if (name !== undefined) {
      navigator.vibrate?.(12)
      setDraft({ ...draft, keys: toggleChordKey(draft.keys, name) })
    }
  }

  if (draft !== undefined) {
    const complete = draft.label.trim().length > 0 && draft.keys.length > 0

    return (
      <div className="shortcuts-view">
        <input
          type="text"
          value={draft.label}
          placeholder="Name, z.B. Bildschirmfoto"
          onChange={(event) => setDraft({ ...draft, label: event.target.value })}
        />

        <div className="chord-bar">
          <span className="chord-preview">
            {draft.keys.length > 0 ? describeChord(draft.keys) : 'Tasten unten auswählen …'}
          </span>
          <button
            type="button"
            className="page-tab"
            disabled={draft.keys.length === 0}
            onClick={() => setDraft({ ...draft, keys: [] })}
          >
            Leeren
          </button>
        </div>

        <OnScreenKeyboard page={page} activeKeys={draft.keys} onPress={collect} />

        <div className="button-row">
          <button type="button" onClick={() => setDraft(undefined)}>
            Abbrechen
          </button>
          <button
            type="button"
            disabled={!complete}
            onClick={() => {
              store(upsertShortcut(shortcuts, { ...draft, label: draft.label.trim() }))
              setDraft(undefined)
            }}
          >
            Speichern
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="shortcuts-view">
      <span className="key-group-label">Shortcuts</span>

      {shortcuts.length === 0 && (
        <p className="now-playing-empty">Noch keine Kombination angelegt.</p>
      )}

      {shortcuts.map((shortcut) => (
        <div key={shortcut.id} className="shortcut-row">
          <button
            type="button"
            className="shortcut-entry"
            onClick={() => setDraft({ ...shortcut, keys: [...shortcut.keys] })}
          >
            <span className="device-name">{shortcut.label}</span>
            <span className="shortcut-keys">{describeChord(shortcut.keys)}</span>
          </button>
          <button
            type="button"
            className="danger"
            aria-label={`${shortcut.label} löschen`}
            onClick={() => store(removeShortcut(shortcuts, shortcut.id))}
          >
            ✕
          </button>
        </div>
      ))}

      <button
        type="button"
        onClick={() => setDraft({ id: makeShortcutId(), label: '', keys: [] })}
      >
        Neuer Shortcut
      </button>
    </div>
  )
}
