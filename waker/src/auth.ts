import type { NextFunction, Request, Response } from 'express'
import type { PairingService } from './pairing.js'

/**
 * Endpunkte, die von außen gar nicht erreichbar sein dürfen: den Kopplungscode
 * anzeigen und Clients widerrufen. Beides setzt voraus, dass jemand Zugriff auf
 * die NAS hat — wer den hat, könnte den Container ohnehin anhalten. Über das
 * Netz wäre es dagegen genau der Weg, den die Kopplung verhindern soll.
 */
const LOCAL_ONLY = ['/api/pair/code', '/api/clients']

/**
 * Endpunkte, die ohne Berechtigung auskommen, weil sie selbst die Berechtigung
 * erzeugen. Sie sind einzeln aufgezählt und nicht per Präfix freigegeben — ein
 * neuer Endpunkt unter `/api/pair/…` soll nicht versehentlich mit offenstehen.
 */
const WITHOUT_CREDENTIAL = ['/health', '/api/info', '/api/pair', '/api/session/challenge', '/api/session']

export function extractBearer(header: string | undefined): string | undefined {
  if (header === undefined) {
    return undefined
  }

  const match = /^Bearer\s+(.+)$/i.exec(header.trim())
  return match?.[1]
}

/**
 * Sperrt alles, was nicht ausdrücklich freigegeben ist.
 *
 * Als Sperre für den gesamten Baum statt pro Route — eine vergessene
 * Absicherung an einer neuen Route wäre sonst ein offener Broadcast-Sender.
 */
export function requireClient(pairing: PairingService) {
  return (request: Request, response: Response, next: NextFunction): void => {
    const path = normalize(request.path)

    if (matches(path, LOCAL_ONLY)) {
      if (isLocal(request.ip)) {
        next()
        return
      }

      response.status(403).json({ error: 'Dieser Aufruf ist nur auf dem Waker selbst möglich.' })
      return
    }

    if (matches(path, WITHOUT_CREDENTIAL)) {
      next()
      return
    }

    const token = extractBearer(request.header('authorization')) ?? readQueryToken(request)

    if (pairing.resolveSession(token) === undefined) {
      console.warn(`Abgelehnt: ${request.method} ${request.path} von ${String(request.ip)}`)
      response.status(401).json({ error: 'Nicht angemeldet.' })
      return
    }

    next()
  }
}

/**
 * Der Waker wird über MagicDNS angesprochen; lokal heißt hier tatsächlich die
 * Loopback-Adresse. Node meldet 127.0.0.1 auf einem Dual-Stack-Socket als
 * `::ffff:127.0.0.1` — ohne die Rückabbildung hielte der Waker die eigene
 * Maschine für fremd und gäbe keinen Kopplungscode heraus.
 */
function isLocal(address: string | undefined): boolean {
  if (address === undefined) {
    return false
  }

  const plain = address.startsWith('::ffff:') ? address.slice('::ffff:'.length) : address

  return plain === '127.0.0.1' || plain === '::1'
}

/** Ein Query-Token gibt es nur, weil WebSockets keine eigenen Header setzen können. */
function readQueryToken(request: Request): string | undefined {
  const value = request.query['token']

  return typeof value === 'string' ? value : undefined
}

/** Ohne abschließenden Schrägstrich vergleichen, sonst greift `/api/clients/` nicht. */
function normalize(path: string): string {
  return path.length > 1 && path.endsWith('/') ? path.slice(0, -1) : path
}

/** Vergleicht auf Segmentgrenzen — `/api/pairing` ist nicht `/api/pair`. */
function matches(path: string, known: readonly string[]): boolean {
  return known.some((entry) => path === entry || path.startsWith(`${entry}/`))
}
