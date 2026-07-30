import { useState } from 'react'
import type { InputChannel } from '../../lib/inputChannel.ts'
import {
  chordNameOf,
  keyEffect,
  nextPage,
  type KeyCap,
  type KeyboardPage,
} from '../../lib/keyboardLayout.ts'
import { describeChord, toggleChordKey } from '../../lib/keys.ts'
import { OnScreenKeyboard } from './OnScreenKeyboard.tsx'

/**
 * `page` — der eigene Tastatur-Tab, `overlay` — über dem Bildschirmbild.
 * Der Unterschied ist nur die Größe der Tasten.
 */
export type KeyboardLayout = 'page' | 'overlay'

interface Props {
  input: InputChannel
  layout: KeyboardLayout
}

/**
 * Bildschirmtastatur samt Kombi-Sammler.
 *
 * Alle Seiten sind gleich hoch und der Seitenwechsel sitzt als Taste auf der
 * Tastatur selbst — beim Umschalten bewegt sich dadurch nichts. Das war der
 * Hauptgrund, die Handy-Tastatur nicht zu verwenden.
 */
export function KeyboardControls({ input, layout }: Props): React.JSX.Element {
  const [page, setPage] = useState<KeyboardPage>('abc')

  // Modifier bleiben bis zum nächsten Tastendruck aktiv — wie Feststelltasten.
  const [heldModifiers, setHeldModifiers] = useState<string[]>([])

  // `undefined` heißt: es wird gerade keine Kombination gesammelt.
  const [chord, setChord] = useState<string[] | undefined>(undefined)

  const collecting = chord !== undefined

  const press = (cap: KeyCap): void => {
    navigator.vibrate?.(12)

    if (cap.page === true) {
      setPage(nextPage(page))
      return
    }

    if (chord !== undefined) {
      const name = chordNameOf(cap)

      // Ein Zeichen ohne eigene Taste (etwa „€") lässt sich nicht kombinieren.
      if (name !== undefined) {
        setChord(toggleChordKey(chord, name))
      }

      return
    }

    const modifier = cap.modifier

    if (modifier !== undefined) {
      setHeldModifiers((current) =>
        current.includes(modifier)
          ? current.filter((entry) => entry !== modifier)
          : [...current, modifier],
      )
      return
    }

    const effect = keyEffect(cap, heldModifiers)

    if (effect === undefined) {
      return
    }

    if (effect.kind === 'text') {
      input.typeText(effect.text)
    } else {
      input.combo(effect.key, effect.mods)
    }

    // Die Modifier danach lösen: sonst müsste man nach jedem Strg+C erst Strg
    // wieder abwählen.
    setHeldModifiers([])
  }

  const sendChord = (): void => {
    if (chord !== undefined && chord.length > 0) {
      input.chord(chord)
      navigator.vibrate?.(25)
    }
  }

  const status = collecting
    ? chord.length > 0
      ? describeChord(chord)
      : 'Tasten nacheinander wählen …'
    : describeChord(heldModifiers)

  return (
    <div className={`keyboard-controls ${layout}`}>
      <div className="keyboard-bar">
        <button
          type="button"
          className={collecting ? 'page-tab active' : 'page-tab'}
          onClick={() => setChord(collecting ? undefined : [])}
        >
          Kombi
        </button>

        {/* Zeigt die gesammelte Kombination oder die festgestellten Modifier —
            sonst wüsste man nicht, dass Strg noch aktiv ist. */}
        <span className="chord-preview">{status}</span>

        {collecting && (
          <>
            <button
              type="button"
              className="page-tab"
              onClick={sendChord}
              disabled={chord.length === 0}
            >
              Senden
            </button>
            <button
              type="button"
              className="page-tab"
              onClick={() => setChord([])}
              disabled={chord.length === 0}
              aria-label="Kombination leeren"
            >
              ✕
            </button>
          </>
        )}
      </div>

      <OnScreenKeyboard
        page={page}
        activeKeys={chord ?? heldModifiers}
        onPress={press}
      />
    </div>
  )
}
