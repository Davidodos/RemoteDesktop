import { PlatformError } from './errors.ts'
import { noSessionKeepAlive } from './session.ts'
import { noHost } from './index.ts'
import { noSurfaces } from './surfaces.ts'
import type {
  Capabilities,
  ClipboardAccess,
  KeyValueStore,
  Platform,
  QrScanner,
  SecretStore,
  TrustService,
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
    chrome?: {
      webview?: {
        postMessage: (message: string) => void
        addEventListener: (type: string, listener: (event: { data: unknown }) => void) => void
      }
    }
  }
}

/**
 * Eine Frage an das Fenster und die Antwort darauf.
 *
 * WebView2 kennt nur Nachrichten in eine Richtung. Ein Gegenstück mit Kennung
 * ist der kürzeste Weg zu einem Aufruf, auf den man warten kann — und länger
 * als eine Kennung, ein Wartender und ein Zeitlimit wird er auch nicht.
 */
const pending = new Map<string, (result: { payload?: unknown; error?: string }) => void>()

/** Länger als das wartet niemand auf ein Zertifikat aus dem Heimnetz. */
const BRIDGE_TIMEOUT_MS = 10_000

let listening = false

function ask<T>(request: Record<string, unknown>): Promise<T> {
  const bridge = window.chrome?.webview

  if (bridge === undefined) {
    return Promise.reject(new PlatformError('Das Fenster hört gerade nicht zu.'))
  }

  if (!listening) {
    listening = true

    bridge.addEventListener('message', (event: { data: unknown }) => {
      const answer = parseAnswer(event.data)

      if (answer === undefined) {
        return
      }

      pending.get(answer.id)?.(answer.payload)
      pending.delete(answer.id)
    })
  }

  const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`

  return new Promise<T>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pending.delete(id)
      reject(new PlatformError('Das Fenster hat nicht geantwortet.'))
    }, BRIDGE_TIMEOUT_MS)

    pending.set(id, (result) => {
      window.clearTimeout(timer)

      if (typeof result === 'object' && result !== null && 'error' in result) {
        reject(new PlatformError(String((result as { error: unknown }).error)))
        return
      }

      resolve(result as T)
    })

    bridge.postMessage(JSON.stringify({ id, ...request }))
  })
}

function parseAnswer(data: unknown): { id: string; payload: never } | undefined {
  const raw = typeof data === 'string' ? data : undefined

  if (raw === undefined || !raw.startsWith('{')) {
    return undefined
  }

  try {
    const parsed = JSON.parse(raw) as { id?: unknown; payload?: unknown }

    return typeof parsed.id === 'string'
      ? { id: parsed.id, payload: parsed.payload as never }
      : undefined
  } catch {
    return undefined
  }
}

/**
 * Einem selbst ausgestellten Zertifikat vertrauen — im Fenster erledigt das
 * die Wirtsanwendung.
 *
 * <p>
 * **Warum nicht die Seite selbst:** sie läuft unter `https`, die Datei liegt
 * unter `http://…:8442/ca.crt`. Chromium verwirft das als aktiven Mixed
 * Content, noch bevor eine Verbindung zustande kommt — und die Ausnahme sieht
 * aus wie ein Gerät, das nicht antwortet. Genau diese Meldung stand am echten
 * Gerät, während die Gegenstelle lief und antwortete.
 * </p>
 *
 * <p>
 * Bestätigtes landet **nicht** im Zertifikatspeicher von Windows: es gilt für
 * dieses Fenster und für nichts sonst. Ein Handy, das im Heimnetz seinen
 * Bildschirm freigibt, soll nicht nebenbei zur Stelle werden, der jeder
 * Browser auf diesem Rechner glaubt.
 * </p>
 */
const windowTrust: TrustService = {
  available: true,

  fetchAuthority: (host: string) =>
    ask<{ base64: string; fingerprint: string }>({ kind: 'trust-fetch', host }),

  install: async (_certificate: string, fingerprint: string) => {
    await ask({ kind: 'trust-install', fingerprint })

    return 'dialog'
  },
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

  installed(): Promise<string | undefined> {
    return Promise.resolve(undefined)
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
    // Kacheln im Startmenü wären das Windows-Gegenstück; sie gehören nicht zu
    // dieser Phase, und niemand hat danach gefragt.
    surfaces: noSurfaces,
    trust: windowTrust,
    // Das Fenster ist die Fernbedienung; steuerbar macht diesen Rechner der
    // Agent daneben, nicht die Oberfläche.
    host: noHost,
  }
}
