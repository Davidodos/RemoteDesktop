import { getPlatform } from '../platform/index.ts'

const DEVICES_KEY = 'remotedesktop.devices'
const CLIENT_KEY = 'remotedesktop.clientKey'
const LAST_DEVICE_KEY = 'remotedesktop.lastDevice'
const TRANSPORT_KEY = 'remotedesktop.transport'
const SHORTCUTS_KEY = 'remotedesktop.shortcuts'
const DEFAULT_MONITOR_PREFIX = 'remotedesktop.monitor.'

/**
 * Was die App sich merkt — benannt statt als lose Schlüssel.
 *
 * Wo es tatsächlich landet, entscheidet die Plattformschicht: im Browser der
 * localStorage, unter Android die Preferences. Alles, was ein Geheimnis ist —
 * die Geräte samt Zugangsdaten und der eigene Schlüssel — geht in den
 * Schlüsselspeicher, weil die anderen Plattformen dafür etwas Eigenes anbieten.
 *
 * Das Hub-Token ist mit Phase 14 entfallen: es gab kein einzelnes Geheimnis
 * mehr, das für alle Rechner zugleich gilt.
 */
function read(key: string): string | undefined {
  return getPlatform().storage.get(key)
}

function write(key: string, value: string | undefined): void {
  const store = getPlatform().storage

  if (value === undefined) {
    store.remove(key)
    return
  }

  store.set(key, value)
}

function readSecret(name: string): string | undefined {
  return getPlatform().keystore.get(name)
}

function writeSecret(name: string, value: string | undefined): void {
  const keystore = getPlatform().keystore

  if (value === undefined) {
    keystore.remove(name)
    return
  }

  keystore.set(name, value)
}

export const storage = {
  /**
   * Die Geräte, die dieses Handy selbst kennt, als JSON — ausgewertet in
   * `deviceSources.ts`. Sie enthalten Zugangsdaten, deshalb der
   * Schlüsselspeicher.
   */
  getDevices: (): string | undefined => readSecret(DEVICES_KEY),
  setDevices: (json: string | undefined): void => writeSecret(DEVICES_KEY, json),

  /**
   * Das eigene Schlüsselpaar als JSON — ausgewertet in `pairing.ts`.
   *
   * Ein Paar für alle Rechner: gekoppelt wird bei jedem einzeln, aber die
   * Identität dieses Handys ist überall dieselbe. Geht es verloren, muss neu
   * gekoppelt werden; das ist der Preis dafür, dass es nirgends hochgeladen wird.
   */
  getClientKey: (): string | undefined => readSecret(CLIENT_KEY),
  setClientKey: (json: string | undefined): void => writeSecret(CLIENT_KEY, json),

  getLastDevice: (): string | undefined => read(LAST_DEVICE_KEY),
  setLastDevice: (deviceId: string | undefined): void => write(LAST_DEVICE_KEY, deviceId),

  /**
   * Der zuletzt von Hand gewählte Übertragungsweg. Ein automatischer Rückfall
   * auf JPEG wird bewusst nicht gemerkt — sonst käme H.264 nach einem einzigen
   * Fehlversuch nie wieder zum Zug.
   */
  getTransport: (): string | undefined => read(TRANSPORT_KEY),
  setTransport: (mode: string | undefined): void => write(TRANSPORT_KEY, mode),

  /**
   * Der Monitor, mit dem ein Gerät startet. Je Gerät gespeichert: am PC ist der
   * mittlere der richtige, am Laptop gibt es nur einen.
   */
  getDefaultMonitor: (deviceId: string): number | undefined => {
    const raw = read(DEFAULT_MONITOR_PREFIX + deviceId)
    const index = raw === undefined ? Number.NaN : Number.parseInt(raw, 10)

    return Number.isInteger(index) && index >= 0 ? index : undefined
  },

  setDefaultMonitor: (deviceId: string, index: number): void =>
    write(DEFAULT_MONITOR_PREFIX + deviceId, String(index)),

  /** Die eigenen Tastenkombinationen als JSON — ausgewertet in `shortcuts.ts`. */
  getShortcuts: (): string | undefined => read(SHORTCUTS_KEY),
  setShortcuts: (json: string | undefined): void => write(SHORTCUTS_KEY, json),
}
