import type { InputChannel } from '../lib/inputChannel.ts'
import { KeyboardControls } from './keyboard/KeyboardControls.tsx'

interface Props {
  input: InputChannel
}

/**
 * Der Tastatur-Tab: oben die Sondertasten, unten die Handy-Tastatur.
 *
 * Dieselbe Bedienung gibt es als Overlay über dem Bildschirmbild — dort teilen
 * sich beide den Platz, hier ist genug für alles auf einmal.
 */
export function KeyboardView({ input }: Props): React.JSX.Element {
  return <KeyboardControls input={input} layout="page" />
}
