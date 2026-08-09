import type { AgentInfo } from './types.ts'

/**
 * Was ein Gerät kann. Gegenstück zu `AgentCapabilities` im Agent — die Namen
 * stehen auf beiden Seiten gleich, und ein Test hält das dort fest.
 */
export type Capability =
  | 'screen'
  | 'input'
  /** Echte Tastendrücke: Strg+C, F5, Pfeiltasten. Ein Handy kann das nicht. */
  | 'keys'
  | 'media'
  | 'power'
  | 'actions'
  | 'wake'
  | 'files'

/**
 * Was ein Agent konnte, bevor es das Feld gab — also jeder Windows-Agent vor
 * V4.
 *
 * Ohne diesen Rückfall verlöre beim App-Update jeder noch nicht aktualisierte
 * Rechner die halbe Oberfläche, und zwar lautlos. `files` steht bewusst nicht
 * darin: den Dienst gab es damals nicht, und ein Aufruf ins Leere sähe nach
 * einem kaputten Rechner aus.
 */
export const LEGACY_CAPABILITIES: readonly Capability[] = [
  'screen',
  'input',
  'keys',
  'media',
  'power',
  'actions',
  'wake',
]

/**
 * Die Fähigkeiten eines Geräts, oder die Liste von früher.
 *
 * Auch eine noch nicht eingetroffene Auskunft ergibt die alte Liste: dann steht
 * alles da, bis das Gerät widerspricht. Andersherum — erst nichts zeigen und
 * dann nachschieben — flackerte bei jedem Gerätewechsel die halbe Leiste.
 */
export function capabilitiesOf(info: AgentInfo | undefined): readonly Capability[] {
  if (info?.capabilities === undefined) {
    return LEGACY_CAPABILITIES
  }

  // Was der Agent meldet und diese App nicht kennt, wird verworfen statt
  // durchgereicht: es gibt hier ohnehin keine Seite dafür.
  return info.capabilities.filter(isCapability)
}

export function can(info: AgentInfo | undefined, capability: Capability): boolean {
  return capabilitiesOf(info).includes(capability)
}

function isCapability(value: string): value is Capability {
  return (LEGACY_CAPABILITIES as readonly string[]).includes(value) || value === 'files'
}
