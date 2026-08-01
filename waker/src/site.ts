import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'

/**
 * Die Standort-Kennung: `siteId = sha256(gatewayMac)`.
 *
 * Sie beantwortet die einzige Frage, die beim Wecken zählt — steht dieser Waker
 * im selben Netz wie der schlafende Rechner? Gleiches LAN heißt gleiches
 * Gateway, unabhängig davon, welche IP der DHCP gerade vergeben hat. Die
 * Rechenweise ist dieselbe wie im Agent (`agent/Services/SiteIdentity.cs`);
 * weicht eine der beiden Seiten ab, findet der Client nie einen Waker.
 */
export function siteIdFromGatewayMac(mac: string | undefined): string | undefined {
  const normalized = normalizeMac(mac)

  if (normalized === undefined) {
    return undefined
  }

  return createHash('sha256').update(normalized, 'utf8').digest('hex')
}

/**
 * Kleinbuchstaben mit Doppelpunkten, oder `undefined` für alles, was keine MAC
 * ist. Die Nulladresse zählt nicht: sie melden Schnittstellen ohne eigene
 * Hardware-Adresse, und als Standort-Kennung wäre sie die eine, die überall
 * gleich ist.
 */
export function normalizeMac(mac: string | undefined): string | undefined {
  if (mac === undefined) {
    return undefined
  }

  const hex = mac.replace(/[:.\-\s]/g, '').toLowerCase()

  if (!/^[0-9a-f]{12}$/.test(hex) || hex === '000000000000') {
    return undefined
  }

  return (hex.match(/.{2}/g) ?? []).join(':')
}

/**
 * Die Adresse des Standard-Gateways aus `/proc/net/route`.
 *
 * Die Zieladresse `00000000` ist die Standardroute. Adressen stehen dort als
 * Little-Endian-Hex — `0102A8C0` ist `192.168.2.1`.
 */
export function parseDefaultGateway(procNetRoute: string): string | undefined {
  for (const line of procNetRoute.split('\n').slice(1)) {
    const [, destination, gateway] = line.trim().split(/\s+/)

    if (destination !== '00000000' || gateway === undefined || gateway === '00000000') {
      continue
    }

    const bytes = gateway.match(/.{2}/g)

    if (bytes === null || bytes.length !== 4) {
      continue
    }

    return bytes
      .reverse()
      .map((byte) => Number.parseInt(byte, 16))
      .join('.')
  }

  return undefined
}

/** Die MAC zu einer IP aus `/proc/net/arp` — die ARP-Tabelle des Kernels. */
export function parseArpEntry(procNetArp: string, address: string): string | undefined {
  for (const line of procNetArp.split('\n').slice(1)) {
    const columns = line.trim().split(/\s+/)

    if (columns[0] === address) {
      return normalizeMac(columns[3])
    }
  }

  return undefined
}

/**
 * Ermittelt die Standort-Kennung aus der ARP-Tabelle des Systems.
 *
 * Voraussetzung ist `network_mode: host` — im Docker-Netz stünde hier das
 * Gateway der Bridge, und dann hätte jeder Container am selben Standort eine
 * andere Kennung als der PC daneben. Dasselbe `network_mode: host`, das der
 * Broadcast ohnehin verlangt.
 */
export async function detectSiteId(): Promise<string | undefined> {
  try {
    const gateway = parseDefaultGateway(await readFile('/proc/net/route', 'utf8'))

    if (gateway === undefined) {
      return undefined
    }

    return siteIdFromGatewayMac(parseArpEntry(await readFile('/proc/net/arp', 'utf8'), gateway))
  } catch {
    // Kein Linux, kein host-Netz, keine Rechte — dann gibt es eben keine
    // Kennung, und der Waker meldet sich als „Standort unbekannt". Ein
    // Startabbruch wäre die falsche Antwort: WOL von Hand geht weiterhin.
    return undefined
  }
}
