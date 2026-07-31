import { PlatformError } from './errors.ts'
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
 * Die Umsetzung für den Browser — die PWA, wie sie heute läuft.
 *
 * Hier steht nichts Neues: localStorage für alles Gespeicherte, keine Kamera,
 * kein Selbst-Update. Sie hält fest, was der Browser tatsächlich hergibt,
 * damit die Oberfläche nicht raten muss.
 */

/**
 * Zugriffe sind gekapselt, weil localStorage im privaten Modus mancher Browser
 * wirft — ein Absturz beim Start wäre die Folge.
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
    // Ohne Persistenz ist die App weiter benutzbar, nur muss das Token nach
    // einem Neustart erneut eingegeben werden.
  }
}

function erase(key: string): void {
  try {
    localStorage.removeItem(key)
  } catch {
    // Siehe write().
  }
}

const webStore: KeyValueStore = { get: read, set: write, remove: erase }

/**
 * Im Browser gibt es keinen geschützten Schlüsselspeicher — dieselbe Ablage
 * wie für Einstellungen. Android und Windows trennen das später wirklich.
 */
const webKeystore: SecretStore = { get: read, set: write, remove: erase }

/** Getter statt fester Werte: `navigator` steht je nach Umgebung erst später. */
const webCapabilities: Capabilities = {
  // Der Browser kann zwar die Kamera öffnen, die PWA bringt aber keinen
  // QR-Decoder mit. Gekoppelt wird deshalb über den getippten Code.
  camera: false,

  get clipboard(): boolean {
    return typeof navigator !== 'undefined' && navigator.clipboard !== undefined
  },

  // Pointer Lock gibt es in Desktop-Browsern, die PWA ist aber für Finger
  // gebaut und führt den Zeiger selbst nach. Genutzt wird es erst im
  // WebView2-Fenster.
  pointerLock: false,

  // Ein Browser-Tab im Hintergrund wird gedrosselt, der Stream pausiert.
  backgroundSession: false,

  // Die PWA erneuert sich über ihren Service Worker; ein eigener Update-Knopf
  // wäre ein zweiter, widersprüchlicher Weg.
  selfUpdate: false,

  // Am Handy liefert `keydown` keine verlässlichen `code`-Werte, und die
  // Systemtastatur verdeckt die halbe Oberfläche. Die eigene
  // Bildschirmtastatur bleibt hier der Weg.
  physicalKeyboard: false,
}

const webClipboard: ClipboardAccess = {
  async readText(): Promise<string> {
    if (!webCapabilities.clipboard) {
      throw new PlatformError('Dieser Browser gibt die Zwischenablage nicht heraus.')
    }

    return await navigator.clipboard.readText()
  },

  async writeText(text: string): Promise<void> {
    if (!webCapabilities.clipboard) {
      throw new PlatformError('Dieser Browser lässt kein Schreiben in die Zwischenablage zu.')
    }

    await navigator.clipboard.writeText(text)
  },
}

const webUpdate: UpdateService = {
  check(): Promise<UpdateInfo | undefined> {
    // Nicht „Fehler", sondern „nichts zu tun": der Service Worker hat die
    // neue Fassung längst geholt, wenn es eine gibt.
    return Promise.resolve(undefined)
  },

  install(): Promise<void> {
    return Promise.reject(
      new PlatformError('Die PWA aktualisiert sich selbst — hier gibt es nichts zu installieren.'),
    )
  },
}

const webQr: QrScanner = {
  scan(): Promise<string> {
    return Promise.reject(new PlatformError('Im Browser gibt es keinen QR-Scanner.'))
  },
}

export const webPlatform: Platform = {
  name: 'web',
  // Der Browser verrät den Rechnernamen nicht — und soll es auch nicht.
  machineName: undefined,
  storage: webStore,
  keystore: webKeystore,
  capabilities: webCapabilities,
  update: webUpdate,
  clipboard: webClipboard,
  qr: webQr,
}
