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
  /**
   * Bild als H.264 über WebRTC.
   *
   * <p>
   * **Warum das eine Fähigkeit sein muss:** die App versuchte es bei jedem
   * Gerät zuerst und fiel auf JPEG zurück, wenn nichts kam. Am Handy kam nie
   * etwas — es hat keinen WebRTC-Endpunkt —, und jede Verbindung dorthin begann
   * mit einem Fehlversuch samt Meldung „H.264 kam nicht zustande". Das war
   * keine Auskunft, sondern eine Entschuldigung für etwas, das gar nicht
   * angeboten wird.
   * </p>
   */
  | 'h264'
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
  // Ein Agent, der das Feld noch nicht kennt, ist ein Windows-Agent — und die
  // konnten H.264 schon immer. Ihn hier wegzulassen hieße, jedem älteren
  // Rechner den sparsamen Weg zu nehmen.
  'h264',
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

/**
 * Ob am anderen Ende Berührungen ankommen statt Tastendrücke — also ein Handy.
 *
 * <p>
 * Gefragt wird nach {@link Capability.keys} und nicht nach der Plattform: was
 * ein Gerät annimmt, sagt es selbst. Daran hängen die Eingaben vom Rechner aus:
 * ein Klick wird dort ein Tippen, ein gezogener Rechtsklick eine Zoomgeste, und
 * getippte Zeichen gehen als Text hinaus statt als Anschläge, die es dort nicht
 * gibt.
 * </p>
 */
export function isTouchTarget(abilities: readonly Capability[]): boolean {
  return abilities.includes('input') && !abilities.includes('keys')
}
