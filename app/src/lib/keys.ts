import { LAYOUTS } from './keyboardLayout.ts'

/** Modifier, die sich feststellen lassen. */
export const MODIFIERS = ['ctrl', 'alt', 'shift', 'win'] as const

export type Modifier = (typeof MODIFIERS)[number]

const MODIFIER_NAMES: readonly string[] = MODIFIERS

export function isModifier(key: string): boolean {
  return MODIFIER_NAMES.includes(key)
}

/** Beschriftungen aus dem Tastaturlayout — damit steht jede Taste nur einmal irgendwo. */
const LABELS: ReadonlyMap<string, string> = new Map(
  Object.values(LAYOUTS)
    .flat(2)
    .flatMap((cap) => {
      const name = cap.modifier ?? cap.key

      return name === undefined ? [] : [[name, cap.label] as [string, string]]
    }),
)

/** Beschriftung einer Taste; Buchstaben und Ziffern haben keine eigene. */
export function labelForKey(key: string): string {
  return LABELS.get(key) ?? key.toUpperCase()
}

/** Eine gesammelte Kombination als Text, z.B. „Strg + ⇧ + Esc“. */
export function describeChord(keys: readonly string[]): string {
  return keys.map(labelForKey).join(' + ')
}

/**
 * Nimmt eine Taste in die Kombination auf.
 *
 * Doppelt gedrückte Tasten fallen wieder heraus — so lässt sich ein Fehlgriff
 * durch nochmaliges Tippen zurücknehmen, ohne die ganze Kombination zu
 * verwerfen.
 */
export function toggleChordKey(keys: readonly string[], key: string): string[] {
  return keys.includes(key) ? keys.filter((entry) => entry !== key) : [...keys, key]
}
