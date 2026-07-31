import { PlatformError } from './errors.ts'
import type {
  Capabilities,
  ClipboardAccess,
  KeyValueStore,
  Platform,
  QrScanner,
  UpdateInfo,
  UpdateService,
} from './index.ts'
import type { SessionKeepAlive } from './session.ts'

/**
 * Die Umsetzung für die Android-APK aus `clients/android/`.
 *
 * Darin läuft dieselbe React-App wie im Browser, nur in einer WebView mit
 * Zugang zum System. Drei Dinge kommen dadurch dazu, die der PWA verwehrt sind:
 * ein Vordergrunddienst, der die Sitzung im Hintergrund offenhält, die Kamera
 * für die QR-Kopplung und ein Speicher, der beim Löschen der Browserdaten nicht
 * mitverschwindet.
 */

/** Die vier Plugin-Methoden, die diese App benutzt — mehr nicht. */
interface PreferencesPlugin {
  keys(): Promise<{ keys: string[] }>
  get(options: { key: string }): Promise<{ value: string | null }>
  set(options: { key: string; value: string }): Promise<void>
  remove(options: { key: string }): Promise<void>
}

interface ClipboardPlugin {
  read(): Promise<{ value: string }>
  write(options: { string: string }): Promise<void>
}

interface BarcodeScannerPlugin {
  scanBarcode(options: Record<string, unknown>): Promise<{ ScanResult: string }>
}

/** Das eigene Plugin aus `clients/android/android/app/src/main/java/…`. */
interface SessionServicePlugin {
  start(options: { device: string }): Promise<void>
  stop(): Promise<void>
}

export interface CapacitorPlugins {
  preferences: PreferencesPlugin
  clipboard: ClipboardPlugin
  barcode: BarcodeScannerPlugin
  session: SessionServicePlugin
}

/**
 * Was die Brücke von Capacitor an das Fenster hängt.
 *
 * `registerPlugin` kommt dabei nicht von der Brücke, sondern erst aus
 * `@capacitor/core` — die Brücke liefert nur die Kopfdaten der nativen Plugins.
 * Deshalb wird das Paket unten nachgeladen und nicht hier vorausgesetzt.
 */
interface CapacitorBridge {
  isNativePlatform?: () => boolean
  registerPlugin?: <T>(name: string) => T
}

declare global {
  interface Window {
    Capacitor?: CapacitorBridge
  }
}

/**
 * Ob die App gerade als APK läuft.
 *
 * Im Browser gibt es `window.Capacitor` nicht — die Brücke wird nur von der
 * nativen Hülle in die Seite injiziert. Genau deshalb wird `@capacitor/core`
 * unten dynamisch geladen: ein fester Import stünde sonst auch im PWA-Bündel
 * und definierte den Namen von sich aus.
 */
export function isCapacitor(): boolean {
  return typeof window !== 'undefined' && window.Capacitor?.isNativePlatform?.() === true
}

/**
 * Preferences liegen hinter einer asynchronen Brücke, der Speicher der App ist
 * aber synchron — und das aus gutem Grund: `storage.getDevices()` wird beim
 * ersten Rendern gelesen, ein `await` dort steckte jede Ansicht an.
 *
 * Aufgelöst wird das mit einem Abzug im Arbeitsspeicher: einmal beim Start alles
 * lesen, danach synchron daraus antworten und Schreibvorgänge nebenher
 * durchreichen. Das trägt, weil dieser Speicher genau einen Schreiber hat.
 */
function cachedStore(plugins: CapacitorPlugins, cache: Map<string, string>): KeyValueStore {
  return {
    get: (key) => cache.get(key),

    set: (key, value) => {
      cache.set(key, value)
      persist(plugins.preferences.set({ key, value }))
    },

    remove: (key) => {
      cache.delete(key)
      persist(plugins.preferences.remove({ key }))
    },
  }
}

/**
 * Ein gescheitertes Schreiben beendet die App nicht — sie bleibt bedienbar, nur
 * überlebt die Änderung den nächsten Start nicht. Unbehandelt wäre daraus eine
 * abgewiesene Zusage geworden, die Android als Absturz meldet.
 */
function persist(pending: Promise<unknown>): void {
  void pending.catch(() => undefined)
}

const capabilities: Capabilities = {
  // Der eigentliche Zugewinn gegenüber der PWA: gekoppelt wird über den
  // QR-Code, nicht über sechs abgetippte Ziffern.
  camera: true,

  clipboard: true,

  // Ein Finger ist kein Zeiger, der sich einfangen ließe. Die App führt den
  // Mauszeiger weiter selbst nach.
  pointerLock: false,

  // Dafür gibt es den Vordergrunddienst — das ist der Grund für die APK.
  backgroundSession: true,

  // Kommt mit `PackageInstaller` und den GitHub-Releases in Phase 14.
  selfUpdate: false,

  // Am Handy meldet `keydown` nur 229, und die Systemtastatur verdeckt die
  // halbe Oberfläche. Es bleibt bei der eigenen Bildschirmtastatur.
  physicalKeyboard: false,
}

function clipboardAccess(plugins: CapacitorPlugins): ClipboardAccess {
  return {
    async readText(): Promise<string> {
      const { value } = await plugins.clipboard.read()
      return value
    },

    async writeText(text: string): Promise<void> {
      await plugins.clipboard.write({ string: text })
    },
  }
}

const update: UpdateService = {
  check(): Promise<UpdateInfo | undefined> {
    return Promise.resolve(undefined)
  },

  install(): Promise<void> {
    return Promise.reject(
      new PlatformError('Die App holt sich Updates noch nicht selbst — Phase 14 bringt das.'),
    )
  },
}

/**
 * Was `@capacitor/barcode-scanner` seinem nativen Teil mitgibt.
 *
 * Die Werte werden hier ausgeschrieben, weil das Plugin nur über seinen Namen
 * angesprochen wird und nicht über den JS-Aufsatz, der sie sonst ergänzte. Die
 * Zahlen stammen aus dessen Aufzählungen: 0 ist `QR_CODE`, 1 die rückwärtige
 * Kamera, 3 die Ausrichtung `ADAPTIVE`.
 */
const SCAN_OPTIONS = {
  hint: 0,
  scanInstructions: 'QR-Code am Rechner scannen',
  scanButton: false,
  scanText: ' ',
  cameraDirection: 1,
  scanOrientation: 3,
}

function qrScanner(plugins: CapacitorPlugins): QrScanner {
  return {
    async scan(): Promise<string> {
      let result: { ScanResult: string }

      try {
        result = await plugins.barcode.scanBarcode(SCAN_OPTIONS)
      } catch (cause) {
        // Der Abbruch über die Zurück-Taste kommt hier ebenso an wie eine
        // verweigerte Kamera-Erlaubnis. Die Rohmeldung des Plugins ist eine
        // Fehlernummer und hilft niemandem.
        throw new PlatformError('Der QR-Code wurde nicht gelesen.', { cause })
      }

      if (result.ScanResult.length === 0) {
        throw new PlatformError('Der QR-Code wurde nicht gelesen.')
      }

      return result.ScanResult
    },
  }
}

function sessionKeepAlive(plugins: CapacitorPlugins): SessionKeepAlive {
  return {
    async begin(deviceName: string): Promise<void> {
      await plugins.session.start({ device: deviceName })
    },

    async end(): Promise<void> {
      await plugins.session.stop()
    },
  }
}

/**
 * Baut die Plattform aus den Plugins und dem bereits gelesenen Speicher.
 *
 * Beides wird hereingereicht statt hier beschafft, damit die Umsetzung ohne
 * Android geprüft werden kann — die Brücke gibt es in keinem Testlauf.
 */
export function capacitorPlatform(
  plugins: CapacitorPlugins,
  cache: Map<string, string>,
): Platform {
  const store = cachedStore(plugins, cache)

  return {
    name: 'capacitor',
    // Ein Handy ist nie ein Agent — es gibt nichts, wovor die
    // Selbstverbindungssperre schützen müsste.
    machineName: undefined,
    storage: store,
    /*
     * Android hätte mit dem Keystore und `EncryptedSharedPreferences` einen
     * echten Schlüsselspeicher. Er käme mit einem weiteren Plugin und einer
     * zweiten Ablage, aus der beim Wechsel des Sperrbildschirms Schlüssel
     * verschwinden können. Bis dahin liegt der private Geräteschlüssel in den
     * Preferences — die sind app-privat und für andere Apps ohne Root
     * unlesbar, anders als der localStorage eines Browsers.
     */
    keystore: store,
    capabilities,
    update,
    clipboard: clipboardAccess(plugins),
    qr: qrScanner(plugins),
    session: sessionKeepAlive(plugins),
  }
}

/**
 * Lädt die Brücke nach, meldet die Plugins an und liest den Speicher einmal
 * vollständig ein. Das ruft `main.tsx` vor dem ersten Rendern auf.
 */
export async function loadCapacitorPlatform(): Promise<Platform> {
  const plugins = await registerCapacitorPlugins()

  return capacitorPlatform(plugins, await readAllPreferences(plugins))
}

async function registerCapacitorPlugins(): Promise<CapacitorPlugins> {
  // Erst dieser Import setzt `registerPlugin` auf die Brücke. Er steht hier
  // unten statt oben in der Datei, damit `@capacitor/core` nicht im Bündel der
  // PWA landet, die es nie benutzt.
  const { registerPlugin } = await import('@capacitor/core')

  return {
    preferences: registerPlugin<PreferencesPlugin>('Preferences'),
    clipboard: registerPlugin<ClipboardPlugin>('Clipboard'),
    barcode: registerPlugin<BarcodeScannerPlugin>('CapacitorBarcodeScanner'),
    session: registerPlugin<SessionServicePlugin>('SessionService'),
  }
}

/**
 * Der Abzug für den synchronen Speicher. Scheitert das Lesen, startet die App
 * mit leerem Speicher — dann muss neu gekoppelt werden, was ärgerlich, aber
 * behebbar ist. Ein Abbruch beim Start wäre es nicht.
 */
export async function readAllPreferences(plugins: CapacitorPlugins): Promise<Map<string, string>> {
  const cache = new Map<string, string>()

  try {
    const { keys } = await plugins.preferences.keys()

    const entries = await Promise.all(
      keys.map(async (key) => [key, (await plugins.preferences.get({ key })).value] as const),
    )

    for (const [key, value] of entries) {
      if (value !== null) {
        cache.set(key, value)
      }
    }
  } catch {
    // Siehe oben.
  }

  return cache
}
