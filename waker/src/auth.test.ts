import type { Request, Response } from 'express'
import { describe, expect, test, vi } from 'vitest'
import { extractBearer, requireClient } from './auth.js'
import type { PairingService } from './pairing.js'

/** Ein Waker, bei dem genau ein Token gilt. */
function pairingWith(gueltig: string): PairingService {
  return {
    resolveSession: (token: string | undefined) => (token === gueltig ? 'handy-1' : undefined),
  } as unknown as PairingService
}

interface Aufruf {
  status: number | undefined
  weitergereicht: boolean
}

function anfragen(
  path: string,
  options: { token?: string; queryToken?: string; ip?: string } = {},
): Aufruf {
  const result: Aufruf = { status: undefined, weitergereicht: false }

  const request = {
    path,
    method: 'POST',
    ip: options.ip ?? '203.0.113.9',
    query: options.queryToken === undefined ? {} : { token: options.queryToken },
    header: (name: string) =>
      name.toLowerCase() === 'authorization' && options.token !== undefined
        ? `Bearer ${options.token}`
        : undefined,
  } as unknown as Request

  const response = {
    status: (code: number) => {
      result.status = code
      return { json: vi.fn() } as unknown as Response
    },
  } as unknown as Response

  requireClient(pairingWith('gutes-token'))(request, response, () => {
    result.weitergereicht = true
  })

  return result
}

describe('extractBearer', () => {
  test('liest das Token aus dem Header', () => {
    expect(extractBearer('Bearer abc123')).toBe('abc123')
    expect(extractBearer('bearer abc123')).toBe('abc123')
  })

  test('ohne Header gibt es kein Token', () => {
    expect(extractBearer(undefined)).toBeUndefined()
    expect(extractBearer('Basic abc123')).toBeUndefined()
  })
})

describe('requireClient', () => {
  test('ein gültiges Sitzungstoken kommt durch', () => {
    expect(anfragen('/api/wol', { token: 'gutes-token' }).weitergereicht).toBe(true)
  })

  test('ohne Token gibt es 401', () => {
    const aufruf = anfragen('/api/wol')

    expect(aufruf.weitergereicht).toBe(false)
    expect(aufruf.status).toBe(401)
  })

  test('ein falsches Token gibt es 401', () => {
    expect(anfragen('/api/wol', { token: 'geraten' }).status).toBe(401)
  })

  test('das Token darf auch in der Abfrage stehen', () => {
    // WebSockets und <img> können keine eigenen Header setzen — deshalb der Weg.
    expect(anfragen('/api/wol', { queryToken: 'gutes-token' }).weitergereicht).toBe(true)
  })

  test.each(['/api/info', '/api/pair', '/api/session', '/api/session/challenge'])(
    '%s kommt ohne Ausweis durch, weil es selbst einen erzeugt',
    (path) => {
      expect(anfragen(path).weitergereicht).toBe(true)
    },
  )

  test('ein neuer Endpunkt unter /api/pairing steht nicht mit offen', () => {
    // Die Freigabe vergleicht auf Segmentgrenzen. Ein Präfixvergleich hätte
    // /api/pairing-alles mit durchgelassen.
    expect(anfragen('/api/pairing-alles').status).toBe(401)
  })

  test.each(['/api/pair/code', '/api/clients/handy-1'])(
    '%s ist über das Netz gesperrt',
    (path) => {
      const aufruf = anfragen(path, { token: 'gutes-token' })

      // Über das Netz erreichbar wäre genau der Weg, den die Kopplung
      // verhindern soll — auch für einen bereits gekoppelten Client.
      expect(aufruf.weitergereicht).toBe(false)
      expect(aufruf.status).toBe(403)
    },
  )

  test.each(['127.0.0.1', '::1', '::ffff:127.0.0.1'])(
    'vom Waker selbst (%s) gibt es den Kopplungscode',
    (ip) => {
      expect(anfragen('/api/pair/code', { ip }).weitergereicht).toBe(true)
    },
  )
})
