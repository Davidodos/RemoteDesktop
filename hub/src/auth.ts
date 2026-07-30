import { timingSafeEqual } from 'node:crypto'
import type { NextFunction, Request, Response } from 'express'

/**
 * Vergleicht zwei Tokens ohne früh abzubrechen.
 *
 * `timingSafeEqual` verlangt gleiche Länge, deshalb die Vorprüfung — die
 * Token-Länge zu verraten ist unkritisch, der Inhalt nicht.
 */
export function tokensMatch(presented: string, expected: string): boolean {
  const a = Buffer.from(presented, 'utf8')
  const b = Buffer.from(expected, 'utf8')

  if (a.length !== b.length) {
    return false
  }

  return timingSafeEqual(a, b)
}

export function extractBearer(header: string | undefined): string | undefined {
  if (header === undefined) {
    return undefined
  }

  const match = /^Bearer\s+(.+)$/i.exec(header.trim())
  return match?.[1]
}

/**
 * Sperrt alles außer `/health`. Als Middleware für den gesamten Baum statt
 * pro Route — eine vergessene Absicherung an einer neuen Route wäre sonst ein
 * offenes Tor zu den Agent-Tokens.
 */
export function requireHubToken(expected: string) {
  return (request: Request, response: Response, next: NextFunction): void => {
    if (request.path === '/health') {
      next()
      return
    }

    const presented = extractBearer(request.header('authorization'))

    if (presented === undefined || !tokensMatch(presented, expected)) {
      console.warn(`Abgelehnt: ${request.method} ${request.path} von ${request.ip}`)
      response.status(401).json({ error: 'Ungültiges Token.' })
      return
    }

    next()
  }
}
