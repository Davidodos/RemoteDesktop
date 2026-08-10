import { webPlatform } from './web.ts'
import type { HostService } from './host.ts'
import type { LocalNode } from './localNode.ts'
import type { SessionKeepAlive } from './session.ts'
import type { SurfaceBoardPublisher } from './surfaces.ts'

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

/**
 * Einer Zertifizierungsstelle vertrauen, die sich ein Agent selbst ausgestellt
 * hat.
 *
 * Das kann keine Weboberfläche selbst — es ist eine Angelegenheit des Geräts,
 * nicht der Seite. Android bringt dafür einen Systemdialog mit, im Browser
 * bleibt nur die Warnung, die man einmal wegklickt. Deshalb steht hier eine
 * Schnittstelle und keine Umsetzung: die Oberfläche fragt vorher, ob es geht,
 * und bietet den Knopf sonst gar nicht erst an.
 */
export interface TrustService {
  /** Ob dieses Gerät überhaupt einen Weg dafür hat. */
  readonly available: boolean

  /**
   * Holt die Zertifizierungsstelle der Gegenstelle — **nativ**, nicht aus der
   * Seite heraus.
   *
   * <p>
   * **Der Befund dahinter:** die App lief unter `https` (Capacitor auf
   * `https://localhost`, das Fenster auf einem virtuellen Host), und der Abruf
   * ging an `http://<adresse>:8442/ca.crt`. Chromium verwirft das als aktiven
   * Mixed Content, bevor irgendetwas über das Netz geht — die Ausnahme sieht
   * genauso aus wie ein Rechner, der nicht antwortet. Am Gerät stand deshalb
   * „<IP> antwortet nicht", während der Agent lief und antwortete.
   * </p>
   *
   * <p>
   * Nativ gibt es diese Sperre nicht: dort ist es eine gewöhnliche
   * HTTP-Anfrage. `undefined` heißt, dass die Umgebung das nicht kann — dann
   * bleibt der Abruf aus der Seite heraus, der im gewöhnlichen Browser auch
   * funktioniert.
   * </p>
   */
  readonly fetchAuthority?: (host: string, port: number) => Promise<TrustedAuthority>

  /**
   * Übergibt das geprüfte Zertifikat dem System. Was danach passiert, gehört
   * dem System — es fragt selbst nach und kann abgelehnt werden.
   *
   * @param certificateBase64 Das Zertifikat.
   * @param fingerprint Der erwartete Fingerabdruck aus der Kopplung. Er geht
   *   mit, obwohl `lib/certificateTrust.ts` bereits verglichen hat: die
   *   Weboberfläche ist austauschbar, und eine Prüfung, die nur an einer Stelle
   *   steht, ist eine, die beim nächsten Umbau verschwindet.
   */
  install(certificateBase64: string, fingerprint: string): Promise<TrustOutcome>
}

/**
 * Wie weit das System gekommen ist.
 *
 * `dialog` heißt: Android hat seinen Bestätigungsdialog gezeigt, danach ist es
 * erledigt. `settings` heißt: es lässt das seit Android 11 nicht mehr aus einer
 * App heraus zu — die Datei liegt jetzt in den Downloads, und die
 * Systemeinstellungen sind offen. Der Unterschied gehört auf den Bildschirm:
 * beim zweiten Fall passiert sonst scheinbar nichts.
 */
export type TrustOutcome = 'dialog' | 'settings'

/** Ein geholtes Zertifikat samt seinem Fingerabdruck. */
export interface TrustedAuthority {
  /** Das Zertifikat als Base64 (DER). */
  base64: string
  /** `sha256` darüber, kleingeschrieben und ohne Trennzeichen. */
  fingerprint: string
}

/** Für Umgebungen, die es nicht können — der Browser vor allem. */
export const noTrust: TrustService = {
  available: false,
  install: (): Promise<TrustOutcome> =>
    Promise.reject(
      new Error(
        'Auf diesem Gerät lässt sich das Zertifikat nicht aus der App heraus bestätigen.',
      ),
    ),
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
  noLocalNode,
  usableOffer,
  type BackPairing,
  type LocalNode,
} from './localNode.ts'
export {
  noHost,
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
