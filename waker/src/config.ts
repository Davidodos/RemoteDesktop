import { resolve } from 'node:path'

/**
 * Der Waker hat **keine Gerätekonfiguration**.
 *
 * Bis Phase 13 stand hier eine `devices.json` mit Namen, MACs und den
 * Agent-Tokens aller Rechner — sie musste bei jedem neuen Gerät nachgepflegt
 * werden, und wer das Hub-Token kannte, bekam die Tokens aller Agents auf
 * einmal. Beides ist weg: die MAC steht in der Anfrage, und wer fragen darf,
 * entscheidet die Kopplung (`clients.json`).
 *
 * Übrig bleiben vier Angaben, die alle aus der Umgebung kommen — ein zweiter
 * Standort heißt damit: Container starten, einmal koppeln, fertig.
 */
export interface WakerConfig {
  port: number
  /** Wohin das Magic Packet geht. Der LAN-Broadcast, nicht der des Docker-Netzes. */
  broadcastAddress: string
  /** Wo die gekoppelten Clients liegen. Wird angelegt, wenn es die Datei nicht gibt. */
  clientsPath: string
  /**
   * Erzwungene Standort-Kennung, falls die Gateway-MAC nicht zu ermitteln ist.
   * Normalerweise leer — sie ergibt sich aus dem Netz (siehe `site.ts`).
   */
  siteId: string | undefined
  /**
   * Zertifikat und Schlüssel aus `tailscale cert`. Fehlen sie, lauscht der
   * Waker im Klartext — brauchbar für einen Test auf der Maschine selbst, aber
   * nicht für die App: die läuft unter `https://` und der Browser lässt eine
   * `http://`-Anfrage von dort nicht durch.
   */
  certificatePath: string | undefined
  keyPath: string | undefined
}

const DEFAULT_PORT = 3080

export function loadConfig(env: NodeJS.ProcessEnv = process.env): WakerConfig {
  const raw = env['WAKER_PORT'] ?? String(DEFAULT_PORT)
  const port = Number(raw)

  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error(`WAKER_PORT ist kein gültiger Port: ${raw}`)
  }

  const siteId = env['SITE_ID']

  return {
    port,
    broadcastAddress: env['BROADCAST_ADDRESS'] ?? '255.255.255.255',
    clientsPath: resolve(env['CLIENTS_PATH'] ?? '/config/clients.json'),
    siteId: siteId === undefined || siteId.length === 0 ? undefined : siteId,
    certificatePath: blankToUndefined(env['CERTIFICATE_PATH']),
    keyPath: blankToUndefined(env['KEY_PATH']),
  }
}

function blankToUndefined(value: string | undefined): string | undefined {
  return value === undefined || value.length === 0 ? undefined : value
}
