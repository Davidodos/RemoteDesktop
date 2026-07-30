import { createServer, type Server } from 'node:net'
import { afterAll, beforeAll, describe, expect, test } from 'vitest'
import { isReachable, probe } from './probe.js'

let server: Server
let port: number

beforeAll(async () => {
  server = createServer()

  await new Promise<void>((resolve) => {
    server.listen(0, '127.0.0.1', () => {
      const address = server.address()
      port = typeof address === 'object' && address !== null ? address.port : 0
      resolve()
    })
  })
})

afterAll(() => {
  server.close()
})

describe('Erreichbarkeit prüfen', () => {
  test('ein lauschender Agent gilt als online', async () => {
    // Act
    const result = await probe('127.0.0.1', port)

    // Assert
    expect(result).toEqual({ online: true })
  })

  test('ein geschlossener Port gilt als offline', async () => {
    // Arrange — Port 1 ist auf keinem Rechner belegt.
    const result = await probe('127.0.0.1', 1)

    // Assert
    expect(result.online).toBe(false)
    expect(result.reason).toBe('unreachable')
  })

  test('ein unbekannter Name wird als Namensproblem gemeldet', async () => {
    // Arrange — genau das passiert, wenn dem Hub der Tailscale-DNS fehlt.
    const result = await probe('gibtesnicht.invalid', 8443)

    // Assert — ohne diese Unterscheidung sieht ein DNS-Fehler auf der NAS aus
    // wie ein ausgeschalteter PC.
    expect(result.online).toBe(false)
    expect(result.reason).toBe('dns')
  })

  test('die Kurzform liefert weiterhin nur ja oder nein', async () => {
    // Act + Assert
    expect(await isReachable('127.0.0.1', port)).toBe(true)
    expect(await isReachable('gibtesnicht.invalid', 8443)).toBe(false)
  })
})
