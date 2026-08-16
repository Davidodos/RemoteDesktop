import type { SurfaceBoard } from '../lib/surfaceBoard.ts'
import { findLatestApk, isDifferentVersion } from './appUpdate.ts'
import { PlatformError } from './errors.ts'
import type { SurfaceBoardPublisher } from './surfaces.ts'
// Werte direkt aus den definierenden Modulen — siehe web.ts.
import { noHost } from './host.ts'
import { noLocalNode, usableProfile } from './localNode.ts'
import { noTrust } from './trust.ts'
import type {
  ConnectionRequest,
  HostClient,
  HostPairingCode,
  HostService,
  HostStatus,
  LocalNode,
  Capabilities,
  ClipboardAccess,
  KeyValueStore,
  Platform,
  QrScanner,
  TrustService,
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

/** Übergibt ein geprüftes Zertifikat an den Systemdialog von Android. */
interface TrustPlugin {
  fetch(options: { host: string; port: number }): Promise<{ base64: string; fingerprint: string }>
  install(options: { certificate: string; fingerprint: string }): Promise<{ mode?: string }>
}

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

/** Das zweite eigene Plugin: Fassung ablesen und eine APK installieren. */
interface AppUpdatePlugin {
  current(): Promise<{ version: string }>
  install(options: { url: string }): Promise<void>
}

/**
 * Das dritte eigene Plugin: der Steckbrief für Widget, Tile und App-Kürzel.
 *
 * Er geht als Text hinüber und nicht als Objekt. Die Brücke könnte auch ein
 * Objekt, aber drüben wird er ohnehin als Ganzes abgelegt — und ein Text, der
 * einmal geschrieben und einmal gelesen wird, kann unterwegs nichts verlieren.
 */
interface SurfacesPlugin {
  publish(options: { board: string }): Promise<void>
}

export interface CapacitorPlugins {
  preferences: PreferencesPlugin
  clipboard: ClipboardPlugin
  barcode: BarcodeScannerPlugin
  session: SessionServicePlugin
  /**
   * Freiwillig: eine APK, die noch ohne dieses Plugin gebaut wurde, soll
   * weiterlaufen und dann eben sagen, dass sie sich nicht selbst aktualisieren
   * kann — statt beim Start an einem fehlenden Plugin zu scheitern.
   */
  appUpdate?: AppUpdatePlugin
  /** Ebenfalls freiwillig, aus demselben Grund wie {@link CapacitorPlugins.appUpdate}. */
  surfaces?: SurfacesPlugin
  /** Ebenso freiwillig: eine ältere APK kennt das Plugin nicht. */
  trust?: TrustPlugin
  /** Ebenso — dieses Plugin kam erst mit V4 dazu. */
  host?: HostPlugin
}

/**
 * Das fünfte eigene Plugin: dieses Handy als Ziel.
 *
 * Der Kopplungscode kommt über die Brücke und nicht über HTTP. Der Endpunkt
 * dafür ist absichtlich nur vom Gerät selbst erreichbar — und „vom Gerät
 * selbst" ist genau das hier.
 */
interface HostPlugin {
  status(): Promise<HostStatus>
  start(): Promise<HostStatus>
  stop(): Promise<HostStatus>
  pairingCode(): Promise<HostPairingCode>
  enableScreen(): Promise<HostStatus>
  disableScreen(): Promise<HostStatus>
  openInputSettings(): Promise<void>
  profile(): Promise<{ profile?: unknown }>
  peers(): Promise<{ peers?: unknown }>
  forgetPeers(options: { ids: string[] }): Promise<void>
  grant(options: { publicKey: string; label: string }): Promise<void>
  localClientKey(): Promise<{ publicKey?: string; privateKey?: string }>
  clients(): Promise<{ clients: HostClient[] }>
  revoke(options: { id: string }): Promise<void>
  answerConnection(options: { id: string; allow: boolean }): Promise<void>
  addListener(
    event: string,
    listener: (data: { id?: string; label?: string }) => void,
  ): Promise<{ remove: () => Promise<void> }>
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

  // Ein Knopf und ein Systemdialog: außerhalb von Google Play zeigt Android
  // **immer** eine Rückfrage, bevor eine APK installiert wird. Stiller geht es
  // nicht, und das ist auch gut so.
  selfUpdate: true,

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

/**
 * Selbst-Update der APK über dieselben GitHub-Releases wie der Agent.
 *
 * Es bleibt bei einem Knopf **und** einem Systemdialog — außerhalb von Google
 * Play lässt Android nichts anderes zu, und die App braucht dafür
 * `REQUEST_INSTALL_PACKAGES`. Was den Vorgang absichert, ist nicht eine eigene
 * Prüfung, sondern die Signatur: Android lässt eine APK nur über eine
 * installierte drüber, wenn sie mit demselben Schlüssel unterschrieben ist.
 */
function updateService(plugins: CapacitorPlugins): UpdateService {
  return {
    async check(): Promise<UpdateInfo | undefined> {
      if (plugins.appUpdate === undefined) {
        return undefined
      }

      const angebot = await findLatestApk(async (url) => {
        const response = await fetch(url, { cache: 'no-store' })

        if (!response.ok) {
          throw new PlatformError(`GitHub antwortete mit HTTP ${response.status}.`)
        }

        return await response.json()
      })

      if (angebot === undefined) {
        return undefined
      }

      const installiert = await plugins.appUpdate
        .current()
        .then(({ version }) => version)
        // Ohne die eigene Fassung wird angeboten statt geschwiegen: eine
        // angebotene Aktualisierung ist harmloser als eine verschwiegene.
        .catch(() => undefined)

      return isDifferentVersion(angebot.version, installiert) ? angebot : undefined
    },

    async install(update: UpdateInfo): Promise<void> {
      if (plugins.appUpdate === undefined) {
        throw new PlatformError('Diese Fassung der App kann sich nicht selbst aktualisieren.')
      }

      await plugins.appUpdate.install({ url: update.url })
    },

    async installed(): Promise<string | undefined> {
      if (plugins.appUpdate === undefined) {
        return undefined
      }

      // Ein Fehlschlag ist hier eine fehlende Auskunft und kein Fehler: die
      // Einstellungsseite schreibt dann eben nichts hin.
      return await plugins.appUpdate
        .current()
        .then(({ version }) => version)
        .catch(() => undefined)
    },
  }
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

/**
 * Reicht den Steckbrief an die nativen Flächen weiter.
 *
 * Ein Fehler beim Übergeben wird geschluckt: die Flächen sind eine Zugabe, und
 * wer gerade den Rechner steuert, soll deswegen kein Fehlerband sehen. Ohne
 * Plugin — eine ältere APK — passiert schlicht nichts.
 */
function surfaceBoardPublisher(plugins: CapacitorPlugins): SurfaceBoardPublisher {
  return {
    async publish(board: SurfaceBoard | undefined): Promise<void> {
      if (plugins.surfaces === undefined) {
        return
      }

      // Der leere Text ist das Zeichen zum Abräumen. Ein `undefined` durch die
      // Brücke zu schicken hieße, sich auf deren Umgang mit fehlenden Feldern
      // zu verlassen — der ist je nach Plattform ein anderer.
      await plugins.surfaces
        .publish({ board: board === undefined ? '' : JSON.stringify(board) })
        .catch(() => undefined)
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
    update: updateService(plugins),
    clipboard: clipboardAccess(plugins),
    qr: qrScanner(plugins),
    session: sessionKeepAlive(plugins),
    surfaces: surfaceBoardPublisher(plugins),
    trust: certificateTrust(plugins),
    host: hostService(plugins),
    node: localNode(plugins),
  }
}

/**
 * Dieses Handy als Gegenstelle.
 *
 * Ob der Host gerade läuft, spielt hier keine Rolle: der Steckbrief beschreibt,
 * wie dieses Handy erreichbar wäre, und ein Eintrag in einer Datei wirkt,
 * sobald der Server startet. Genau daran scheiterte der Vorgänger.
 */
function localNode(plugins: CapacitorPlugins): LocalNode {
  const plugin = plugins.host

  // Eine ältere APK kennt die vier Methoden nicht. Dann bleibt es bei der einen
  // Richtung, statt an einem Aufruf ins Leere zu scheitern.
  if (plugin === undefined || typeof plugin.profile !== 'function') {
    return noLocalNode
  }

  return {
    // Die Runtime lebt im Prozess dieser App; die Datei wird immer geführt.
    ready: () => Promise.resolve(true),
    profile: async () => usableProfile((await plugin.profile()).profile),

    peers: async () => {
      const { peers } = await plugin.peers()

      return Array.isArray(peers)
        ? peers.flatMap((entry) => {
            const profile = usableProfile(entry)

            return profile === undefined ? [] : [profile]
          })
        : []
    },

    forget: (ids) => plugin.forgetPeers({ ids }),
    grant: (publicKey, label) => plugin.grant({ publicKey, label }),

    /**
     * Der Ausweis dieses Handys liegt bei den übrigen Schlüsseln des Hosts und
     * nicht im Speicher der Weboberfläche. Eine ältere APK kennt den Weg nicht;
     * dann legt die App selbst einen an, wie im Browser.
     */
    key: async () => {
      if (typeof plugin.localClientKey !== 'function') {
        return undefined
      }

      const { publicKey, privateKey } = await plugin.localClientKey()

      return typeof publicKey === 'string' && typeof privateKey === 'string'
        ? { publicKey, privateKey }
        : undefined
    },
  }
}

/**
 * Dieses Handy steuerbar machen.
 *
 * Fehlt das Plugin, bleibt es bei `noHost` — eine APK, die noch ohne gebaut
 * wurde, soll weiterlaufen und die Freigabeseite gar nicht erst anbieten,
 * statt an einem Aufruf ins Leere zu scheitern.
 */
function hostService(plugins: CapacitorPlugins): HostService {
  const plugin = plugins.host

  if (plugin === undefined) {
    return noHost
  }

  return {
    available: true,
    status: () => plugin.status(),
    start: () => plugin.start(),
    stop: () => plugin.stop(),
    pairingCode: () => plugin.pairingCode(),
    enableScreen: () => plugin.enableScreen(),
    disableScreen: () => plugin.disableScreen(),
    openInputSettings: () => plugin.openInputSettings(),
    onRequests: (listener) => watchConnections(plugin, listener),
    answer: (id, allow) => plugin.answerConnection({ id, allow }),
    clients: async () => (await plugin.clients()).clients,
    revoke: (id) => plugin.revoke({ id }),
  }
}

/**
 * Die offenen Rückfragen zusammenhalten.
 *
 * <p>
 * Die native Seite meldet zwei Ereignisse — eine Frage kommt, eine Frage ist
 * erledigt —, und die Liste dazwischen liegt hier. Sie in der Ansicht zu führen
 * hieße, sie bei jedem Seitenwechsel zu verlieren: die Karte muss überall
 * erscheinen, denn die Gegenseite wartet.
 * </p>
 *
 * <p>
 * Eine ältere APK kennt die Ereignisse nicht. Dann bleibt die Liste leer, und
 * die Anmeldung läuft wie bisher durch — bestätigt wird erst, wo auch jemand
 * fragen kann.
 * </p>
 */
function watchConnections(
  plugin: HostPlugin,
  listener: (requests: ConnectionRequest[]) => void,
): () => void {
  if (typeof plugin.addListener !== 'function') {
    return () => undefined
  }

  let open: ConnectionRequest[] = []
  let alive = true

  const handles: Promise<{ remove: () => Promise<void> }>[] = [
    plugin.addListener('connectionRequest', (data) => {
      if (!alive || typeof data.id !== 'string' || data.id.length === 0) {
        return
      }

      open = [
        ...open.filter((request) => request.id !== data.id),
        { id: data.id, label: data.label ?? 'Ein gekoppeltes Gerät' },
      ]

      listener(open)
    }),

    plugin.addListener('connectionSettled', (data) => {
      if (!alive) {
        return
      }

      open = open.filter((request) => request.id !== data.id)
      listener(open)
    }),
  ]

  return () => {
    alive = false

    for (const handle of handles) {
      void handle.then((entry) => entry.remove()).catch(() => undefined)
    }
  }
}

/**
 * Zertifikate landen im Speicher des Systems, nicht in dem der App: nur so
 * gelten sie auch für die WebSocket-Verbindungen, über die Bild und Eingabe
 * laufen. Android fragt dabei selbst nach — die App kann das weder umgehen noch
 * heimlich tun, und das ist richtig so.
 */
function certificateTrust(plugins: CapacitorPlugins): TrustService {
  const plugin = plugins.trust

  if (plugin === undefined) {
    // Eine APK von vor V3. Sie soll weiterlaufen und dann eben sagen, dass es
    // hier nicht geht — statt beim Start an einem fehlenden Plugin zu
    // scheitern.
    return noTrust
  }

  return {
    available: true,

    /*
     * Der Abruf läuft nativ und nicht aus der Seite heraus: die App liegt unter
     * `https://localhost`, die Datei unter `http://…:8442/ca.crt`. Chromium
     * verwirft das als aktiven Mixed Content, bevor irgendetwas über das Netz
     * geht — und die Ausnahme sieht aus wie eine Gegenstelle, die nicht
     * antwortet. Genau diese Meldung stand am echten Gerät.
     *
     * Eine APK ohne diese Plugin-Methode fällt auf den Abruf aus der Seite
     * zurück; der scheitert dann wie bisher, aber mit demselben Text wie
     * vorher und nicht mit einem neuen Fehler.
     */
    fetchAuthority:
      typeof plugin.fetch === 'function'
        ? (host, port) => plugin.fetch({ host, port })
        : undefined,

    install: async (certificateBase64, fingerprint) => {
      const { mode } = await plugin.install({ certificate: certificateBase64, fingerprint })

      // Alles außer dem ausdrücklichen Dialog behandeln wir als „geh in die
      // Einstellungen": eine ältere APK meldet gar nichts, und dann ist der
      // Hinweis das Nützlichere von beidem.
      return mode === 'dialog' ? 'dialog' : 'settings'
    },
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
    appUpdate: registerPlugin<AppUpdatePlugin>('AppUpdate'),
    surfaces: registerPlugin<SurfacesPlugin>('Surfaces'),
    trust: registerPlugin<TrustPlugin>('CertificateTrust'),
    host: registerPlugin<HostPlugin>('Host'),
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
