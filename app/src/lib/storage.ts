const TOKEN_KEY = 'remotedesktop.hubToken'
const LAST_DEVICE_KEY = 'remotedesktop.lastDevice'
const TRANSPORT_KEY = 'remotedesktop.transport'
const SHORTCUTS_KEY = 'remotedesktop.shortcuts'
const DEFAULT_MONITOR_PREFIX = 'remotedesktop.monitor.'

/**
 * Kleine Hülle um localStorage.
 *
 * Zugriffe sind gekapselt, weil localStorage im privaten Modus mancher
 * Browser wirft — ein Absturz beim Start wäre die Folge.
 */
function read(key: string): string | undefined {
  try {
    return localStorage.getItem(key) ?? undefined
  } catch {
    return undefined
  }
}

function write(key: string, value: string | undefined): void {
  try {
    if (value === undefined) {
      localStorage.removeItem(key)
      return
    }

    localStorage.setItem(key, value)
  } catch {
    // Ohne Persistenz ist die App weiter benutzbar, nur muss das Token nach
    // einem Neustart erneut eingegeben werden.
  }
}

export const storage = {
  getHubToken: (): string | undefined => read(TOKEN_KEY),
  setHubToken: (token: string | undefined): void => write(TOKEN_KEY, token),
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
