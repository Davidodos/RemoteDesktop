import { AgentClient } from './agentClient.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from './types.ts'

/**
 * Ob ein gekoppeltes Gerät denselben Stand hat wie dieses hier.
 *
 * <p>
 * **Warum das in die Geräteliste gehört.** Agent, Fenster und App werden aus
 * demselben Release gebaut, aber getrennt installiert — und der Rechner im
 * Nebenzimmer aktualisiert sich nicht von allein, solange ihn niemand einschaltet
 * und benutzt. Was man vorher sah, war ein Gerät, das sich seltsam verhielt; was
 * man nicht sah, war der Grund. Eine Fassung neben dem Namen macht aus „irgendwas
 * stimmt nicht" ein „das ist zwei Ausgaben alt".
 * </p>
 *
 * <p>
 * **Verglichen wird gegen dieses Gerät und nicht gegen GitHub.** Die Frage in
 * der Liste ist, ob zwei Geräte zusammenpassen — das entscheidet sich zwischen
 * ihnen. Ob es darüber hinaus etwas Neueres gibt, sagt der Update-Bereich in den
 * Einstellungen, und der ist dafür der richtige Ort.
 * </p>
 */
export type VersionMatch =
  /** Gleicher Stand — es gibt nichts zu tun. */
  | 'same'
  /** Die Gegenseite ist älter als dieses Gerät. */
  | 'older'
  /**
   * Die Gegenseite ist neuer. Kein Fehler, sondern der Hinweis, dass dieses
   * Gerät dran wäre — aktualisieren muss es sich dann selbst.
   */
  | 'newer'
  /** Eine der beiden Fassungen ist unbekannt. Dann wird nichts behauptet. */
  | 'unknown'

/** `v1.2.0` und `1.2.0` sind dieselbe Fassung, `1.2.0+abc` ebenfalls. */
export function normalizeVersion(value: string | undefined): string | undefined {
  if (value === undefined) {
    return undefined
  }

  const trimmed = value.trim().replace(/^v/i, '')
  const build = trimmed.indexOf('+')
  const core = build < 0 ? trimmed : trimmed.slice(0, build)

  return core.length === 0 ? undefined : core
}

/**
 * Vergleicht zwei Fassungen Zahl für Zahl.
 *
 * Nicht als Text: `1.10.0` ist neuer als `1.9.0`, sortiert sich aber davor.
 * Was hinter der letzten Zahl steht — ein `-beta` etwa —, entscheidet nur noch
 * über „gleich oder nicht": eine Ordnung darüber wäre geraten.
 */
export function compareVersions(
  own: string | undefined,
  other: string | undefined,
): VersionMatch {
  const mine = normalizeVersion(own)
  const theirs = normalizeVersion(other)

  if (mine === undefined || theirs === undefined) {
    return 'unknown'
  }

  if (mine === theirs) {
    return 'same'
  }

  const a = parts(mine)
  const b = parts(theirs)

  for (let index = 0; index < Math.max(a.length, b.length); index += 1) {
    const left = a[index] ?? 0
    const right = b[index] ?? 0

    if (left !== right) {
      return right < left ? 'older' : 'newer'
    }
  }

  // Gleiche Zahlen — dann kommt es darauf an, was sonst noch dasteht. Sind
  // beide reine Zahlenfolgen, ist `1.3` dieselbe Fassung wie `1.3.0`: die
  // fehlende Stelle ist eine Null und kein Unterschied. Steht dagegen bei einer
  // von beiden noch etwas anderes — ein `-beta` etwa —, wäre eine Ordnung
  // darüber geraten, und geraten wird hier nichts.
  return numericOnly(mine) && numericOnly(theirs) ? 'same' : 'unknown'
}

function numericOnly(version: string): boolean {
  return /^\d+(\.\d+)*$/.test(version)
}

function parts(version: string): number[] {
  return version
    .split(/[.\-+]/)
    .map((piece) => Number.parseInt(piece, 10))
    .filter((piece) => Number.isFinite(piece))
}

/**
 * Die Fassung dieses Geräts.
 *
 * Am Handy fragt die Plattform das Paket, im Fenster liegt sie im
 * Wirtsprogramm. Im Browser gibt es sie nicht — dort steht neben den Geräten
 * dann eben nichts.
 */
export async function ownVersion(): Promise<string | undefined> {
  try {
    return normalizeVersion(await getPlatform().update.installed())
  } catch {
    // Eine fehlende Auskunft ist keine Störung: die Liste zeigt dann keine
    // Fassungen und funktioniert sonst wie immer.
    return undefined
  }
}

/**
 * Fragt die Fassung eines gekoppelten Geräts ab.
 *
 * Über `/api/info` und damit angemeldet — die Fassung steht ausdrücklich nicht
 * in `/health`. Der wäre billiger, ist aber der einzige Endpunkt ohne Ausweis,
 * und was ein Rechner ungefragt über sich verrät, gehört so klein wie möglich
 * gehalten.
 *
 * `undefined` heißt: nicht erreicht oder eine Gegenstelle, die ihre Fassung
 * nicht nennt (älter als Phase 14).
 */
export async function fetchVersion(device: Device): Promise<string | undefined> {
  try {
    return normalizeVersion((await new AgentClient(device).getInfo()).version)
  } catch {
    return undefined
  }
}

/**
 * Ob dieses Gerät die Gegenseite aus der Ferne aktualisieren kann.
 *
 * <p>
 * Nur ein Rechner: dort startet der Agent den Installer mit den Rechten, die er
 * ohnehin hat, und niemand muss etwas bestätigen. Ein Handy geht nicht, und das
 * ist keine Lücke — Android verlangt für jede Installation einen Systemdialog,
 * und den beantwortet nur, wer das Gerät in der Hand hält. Wer es in der Hand
 * hält, drückt dort auf den Knopf.
 * </p>
 */
export function canUpdateRemotely(device: Device, match: VersionMatch): boolean {
  return device.platform !== 'android' && device.waker !== true && match === 'older'
}

/**
 * Die Zeile unter dem Gerätenamen.
 *
 * <p>
 * **Der Zusatz steht nur da, wenn es etwas zu tun gibt.** Vorher stand hinter
 * jeder Fassung ein Vergleich — „wie hier", „neuer als hier" —, und damit
 * verlangte eine Zeile, die meistens nichts zu melden hat, jedes Mal gelesen zu
 * werden. Ein Gerät auf demselben Stand nennt seine Fassung und sonst nichts;
 * nur ein veraltetes sagt dazu, dass es eins gibt.
 * </p>
 *
 * <p>
 * Ein neueres Gerät bekommt den Zusatz ausdrücklich nicht: dort ist nichts zu
 * tun. Zu aktualisieren wäre dann dieses Gerät hier, und dafür gibt es den
 * Update-Bereich in den Einstellungen — er weiß auch, woher.
 * </p>
 */
export function describeMatch(match: VersionMatch, version: string | undefined): string {
  if (version === undefined) {
    return ''
  }

  return match === 'older'
    ? `Version ${version} - Update verfügbar`
    : `Version ${version}`
}
