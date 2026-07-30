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
  readonly storage: KeyValueStore
  readonly keystore: SecretStore
  readonly capabilities: Capabilities
  readonly update: UpdateService
  readonly clipboard: ClipboardAccess
  readonly qr: QrScanner
}

export { PlatformError } from './errors.ts'

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
