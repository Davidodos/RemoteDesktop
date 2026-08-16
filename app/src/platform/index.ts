// Reihenfolge mit Absicht: `web.ts` holt sich `noHost` und `noLocalNode` von
// hier zurück. Ein Modul wird ausgewertet, sobald es das erste Mal genannt wird —
// stünde `web.ts` vorn, entstünde die Vorgabe-Plattform, bevor es die beiden
// Leerlauf-Umsetzungen gibt, und ihre Felder wären `undefined`. Genau das war
// der Fall, und es fiel erst auf, als jemand `platform.node` im Browser las.
import type { HostService } from './host.ts'
import type { LocalNode } from './localNode.ts'
import type { SessionKeepAlive } from './session.ts'
import type { TrustService } from './trust.ts'
import type { SurfaceBoardPublisher } from './surfaces.ts'
import { webPlatform } from './web.ts'

/**
 * Was die App von ihrer Umgebung braucht.
 *
 * Dieselbe React-Oberfläche läuft später als PWA im Browser, als Capacitor-APK
 * auf dem Handy und in einem WebView2-Fenster unter Windows. Alles, was sich
 * dabei unterscheidet, steht hinter dieser Schnittstelle — die Ansichten und
 * die getunte Eingabelogik merken davon nichts.
 *
 * Umsetzungen: `web.ts` (heute), später `capacitor.ts` und `webview2.ts`.
 */

/** Einfacher Schlüssel-Wert-Speicher für Einstellungen. */
export interface KeyValueStore {
  get(key: string): string | undefined
  set(key: string, value: string): void
  remove(key: string): void
}

/**
 * Speicher für Geheimnisse — heute das Hub-Token und die Agent-Tokens der
 * Geräte, ab Phase 10 zusätzlich der private Geräteschlüssel.
 *
 * Getrennt vom gewöhnlichen Speicher, weil die anderen Plattformen dafür etwas
 * anderes anbieten: Android den Keystore, Windows die DPAPI. Im Browser gibt es
 * diesen Unterschied nicht, deshalb liegt heute beides im localStorage.
 */
export interface SecretStore {
  get(name: string): string | undefined
  set(name: string, secret: string): void
  remove(name: string): void
}

/**
 * Was die laufende Umgebung kann. Die Oberfläche fragt hier nach, bevor sie
 * einen Knopf anbietet — ein Knopf, der nur eine Fehlermeldung erzeugt, ist
 * schlimmer als keiner.
 */
export interface Capabilities {
  /** Kamera für den QR-Scanner der Kopplung. */
  camera: boolean
  /** Zwischenablage in beide Richtungen. */
  clipboard: boolean
  /** Zeiger einfangen für echte Relativbewegung statt Zeiger-Overlay. */
  pointerLock: boolean
  /** Die Sitzung überlebt, dass die App in den Hintergrund geht. */
  backgroundSession: boolean
  /** Die App kann sich selbst aktualisieren. */
  selfUpdate: boolean
  /**
   * Es gibt eine echte Tastatur, deren Anschläge die App abgreifen darf.
   *
   * Am Handy liefert `keydown` keine brauchbaren `code`-Werte und die
   * Systemtastatur schiebt sich über die Oberfläche — dort bleibt die eigene
   * Bildschirmtastatur der einzige Weg. Am Desktop ist es umgekehrt.
   */
  physicalKeyboard: boolean
}

/** Eine bereitstehende neue Fassung der App. */
export interface UpdateInfo {
  version: string
  /** Woher sie kommt — für die Rückfrage vor dem Installieren. */
  url: string
}

export interface UpdateService {
  /** `undefined` heißt: es gibt nichts Neues. */
  check(): Promise<UpdateInfo | undefined>
  install(update: UpdateInfo): Promise<void>
  /**
   * Welche Fassung gerade läuft — `undefined`, wo die Plattform das nicht
   * sagen kann. Sie gehört auf die Einstellungsseite: „nach Updates suchen"
   * ohne die Angabe, was man hat, ist eine Frage ohne Bezugspunkt.
   */
  installed(): Promise<string | undefined>
}

export interface ClipboardAccess {
  readText(): Promise<string>
  writeText(text: string): Promise<void>
}

export interface QrScanner {
  /** Öffnet die Kamera und liefert den gelesenen Inhalt. */
  scan(): Promise<string>
}



export interface Platform {
  readonly name: 'web' | 'capacitor' | 'webview2'
  /**
   * Wie der Rechner heißt, auf dem dieser Client läuft — `undefined`, wenn die
   * Umgebung das nicht verrät (im Browser tut sie es nie).
   *
   * Gebraucht wird das nur für einen Zweck: einen Rechner davon abzuhalten,
   * sich selbst fernzusteuern. Siehe `lib/selfConnection.ts`.
   */
  readonly machineName: string | undefined
  readonly storage: KeyValueStore
  readonly keystore: SecretStore
  readonly capabilities: Capabilities
  readonly update: UpdateService
  readonly clipboard: ClipboardAccess
  readonly qr: QrScanner
  readonly session: SessionKeepAlive
  /**
   * Flächen außerhalb der App — unter Android Widget, Tile und App-Kürzel.
   * Sie lösen Aktionen aus, ohne dass die App läuft, und brauchen dafür einen
   * Steckbrief (siehe `lib/surfaceBoard.ts`).
   */
  readonly surfaces: SurfaceBoardPublisher
  /**
   * Der Weg, einem selbst ausgestellten Agent-Zertifikat zu vertrauen. Nötig
   * überall dort, wo kein Tailscale läuft — also im Heimnetz und im eigenen VPN.
   */
  readonly trust: TrustService
  /**
   * Dieses Gerät steuerbar machen — seit V4 kann ein Handy auch die Gegenseite
   * sein. Wo es das nicht kann, steht `noHost`.
   */
  readonly host: HostService
  /**
   * Dieses Gerät als Gegenstelle — für die Kopplung in beide Richtungen.
   * Siehe `platform/localNode.ts`.
   */
  readonly node: LocalNode
}

export { PlatformError } from './errors.ts'
export { noSessionKeepAlive, type SessionKeepAlive } from './session.ts'
export { noSurfaces, type SurfaceBoardPublisher } from './surfaces.ts'
export {
  noTrust,
  type TrustedAuthority,
  type TrustOutcome,
  type TrustService,
} from './trust.ts'
export {
  noLocalNode,
  usableProfile,
  type ClientKey,
  type DevicePlatform,
  type DeviceProfile,
  type LocalNode,
} from './localNode.ts'
export {
  noHost,
  type ConnectionRequest,
  type HostClient,
  type HostPairingCode,
  type HostService,
  type HostStatus,
} from './host.ts'

let current: Platform = webPlatform

/**
 * Setzt die Umsetzung für diesen Lauf. Das rufen der Android- und der
 * Windows-Client beim Start auf, bevor React startet; im Browser bleibt es bei
 * der Vorgabe.
 */
export function setPlatform(platform: Platform): void {
  current = platform
}

export function getPlatform(): Platform {
  return current
}
