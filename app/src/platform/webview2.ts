import { PlatformError } from './errors.ts'
import { noSessionKeepAlive } from './session.ts'
import type {
  Capabilities,
  ClipboardAccess,
  KeyValueStore,
  Platform,
  QrScanner,
  SecretStore,
  UpdateInfo,
  UpdateService,
} from './index.ts'

/**
 * Die Umsetzung für das Windows-Fenster aus `desktop/`.
 *
 * Darin läuft dieselbe React-App wie im Browser, nur in einem WebView2. Am
 * Desktop wird die Bedienung dadurch nicht schwerer, sondern einfacher: echte
 * Tastatur, Zwischenablage mit Fensterfokus und Pointer Lock statt eines
 * selbst geführten Zeiger-Overlays.
 */

/**
 * Was das Wirtsprogramm vor dem Laden der Seite hinterlegt.
 *
 * Übergeben wird es als einfaches Objekt und nicht über eine Brücke mit
 * Rückfragen: die App braucht die Angaben sofort beim ersten Rendern, und ein
 * asynchroner Aufruf hätte jede Stelle angesteckt, die sie liest.
 */
export interface WebView2Host {
  /** `Environment.MachineName` des Rechners, auf dem das Fenster läuft. */
  machineName: string
}

declare global {
  interface Window {
    remoteDesktopHost?: WebView2Host
  }
}

/** Ob die App gerade im Windows-Fenster läuft. */
export function isWebView2(): boolean {
  return typeof window !== 'undefined' && window.remoteDesktopHost !== undefined
}

/**
 * Im Fenster gibt es keinen zweiten Nutzer und keinen privaten Modus —
 * localStorage steht verlässlich zur Verfügung. Die Zugriffe sind trotzdem
 * gekapselt, damit ein voller Speicher nicht die App beendet.
 */
function read(key: string): string | undefined {
  try {
    return localStorage.getItem(key) ?? undefined
  } catch {
    return undefined
  }
}

function write(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Ohne Persistenz bleibt die App bedienbar; nach einem Neustart muss neu
    // gekoppelt werden.
  }
}

function erase(key: string): void {
  try {
    localStorage.removeItem(key)
  } catch {
    // Siehe write().
  }
}

const store: KeyValueStore = { get: read, set: write, remove: erase }

/**
 * Windows hätte mit der DPAPI einen echten Schlüsselspeicher. Ihn zu nutzen
 * hieße, den privaten Geräteschlüssel über eine Brücke ins Wirtsprogramm und
 * zurück zu reichen — das kommt, wenn es einen Grund dafür gibt. Bis dahin
 * liegt er wie im Browser im localStorage des Fensters, und der ist an das
 * Windows-Benutzerprofil gebunden.
 */
const keystore: SecretStore = { get: read, set: write, remove: erase }

const capabilities: Capabilities = {
  // Am Rechner sitzt man davor — gekoppelt wird über den Code, den derselbe
  // Rechner anzeigt. Ein QR-Scanner wäre hier ein Umweg über sich selbst.
  camera: false,

  // Chromium gibt die Zwischenablage frei, solange das Fenster den Fokus hat.
  clipboard: true,

  // Der eigentliche Gewinn am Desktop: echte relative Mausbewegung, ohne dass
  // die App die Zeigerposition selbst nachführen muss.
  pointerLock: true,

  // Das Fenster wird nicht gedrosselt, wenn es in den Hintergrund gerät — der
  // Stream läuft weiter.
  backgroundSession: true,

  // Kommt mit den GitHub-Releases in Phase 14. Bis dahin wäre ein Knopf, der
  // nichts findet, schlimmer als keiner.
  selfUpdate: false,

  physicalKeyboard: true,
}

const clipboard: ClipboardAccess = {
  async readText(): Promise<string> {
    return await navigator.clipboard.readText()
  },

  async writeText(text: string): Promise<void> {
    await navigator.clipboard.writeText(text)
  },
}

const update: UpdateService = {
  check(): Promise<UpdateInfo | undefined> {
    return Promise.resolve(undefined)
  },

  install(): Promise<void> {
    return Promise.reject(
      new PlatformError('Das Windows-Fenster aktualisiert sich noch nicht selbst.'),
    )
  },
}

const qr: QrScanner = {
  scan(): Promise<string> {
    return Promise.reject(new PlatformError('Das Windows-Fenster hat keine Kamera.'))
  },
}

/**
 * Baut die Plattform für dieses Fenster.
 *
 * Ohne die Angaben des Wirtsprogramms gibt es sie nicht — dann läuft die App
 * eben nicht im Fenster, und `web.ts` ist zuständig.
 */
export function webview2Platform(host: WebView2Host): Platform {
  return {
    name: 'webview2',
    machineName: host.machineName,
    storage: store,
    keystore,
    capabilities,
    update,
    clipboard,
    qr,
    // Das Fenster wird nicht gedrosselt — es gibt nichts offenzuhalten.
    session: noSessionKeepAlive,
  }
}
