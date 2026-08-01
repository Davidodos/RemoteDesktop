import type { Server } from 'node:http'
import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import express from 'express'
import { afterEach, beforeEach, describe, expect, test } from 'vitest'
import { ClientStore } from './clients.js'
import { loadConfig } from './config.js'
import { PairingService } from './pairing.js'
import { createApiRouter } from './routes.js'

/**
 * Der Waker über einen echten Server — ohne Kopplungssperre davor, damit hier
 * die Routen geprüft werden und nicht noch einmal die Middleware (das tut
 * `auth.test.ts`).
 */
describe('Waker-Routen', () => {
  let directory: string
  let server: Server
  let base: string
  let gesendet: { mac: string; broadcast: string }[]
  let uhr: number

  beforeEach(async () => {
    directory = mkdtempSync(join(tmpdir(), 'waker-routes-'))
    gesendet = []
    uhr = 0

    const config = loadConfig({
      BROADCAST_ADDRESS: '192.168.178.255',
      CLIENTS_PATH: join(directory, 'clients.json'),
    } as NodeJS.ProcessEnv)

    const app = express()
    app.use(express.json())
    app.use(
      '/api',
      createApiRouter({
        config,
        pairing: new PairingService(new ClientStore(config.clientsPath)),
        siteId: 'kennung-zuhause',
        now: () => uhr,
        send: (mac, broadcast) => {
          gesendet.push({ mac, broadcast })
          return Promise.resolve()
        },
      }),
    )

    server = await new Promise<Server>((resolve) => {
      const listening = app.listen(0, '127.0.0.1', () => resolve(listening))
    })

    const address = server.address()
    base = `http://127.0.0.1:${typeof address === 'object' && address !== null ? address.port : 0}`
  })

  afterEach(async () => {
    await new Promise((resolve) => server.close(resolve))
    rmSync(directory, { recursive: true, force: true })
  })

  const wecken = (mac: unknown): Promise<Response> =>
    fetch(`${base}/api/wol`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ mac }),
    })

  test('die Selbstauskunft nennt Standort und Fähigkeit', async () => {
    const antwort = await (await fetch(`${base}/api/info`)).json()

    expect(antwort).toEqual({ role: 'waker', siteId: 'kennung-zuhause', canWake: true })
  })

  test('die MAC aus der Anfrage geht hinaus', async () => {
    const antwort = await wecken('AA:BB:CC:DD:EE:FF')

    expect(antwort.status).toBe(200)
    expect(gesendet).toEqual([{ mac: 'aa:bb:cc:dd:ee:ff', broadcast: '192.168.178.255' }])
  })

  test('eine unbrauchbare MAC ergibt 400 und kein Paket', async () => {
    expect((await wecken('der-pc')).status).toBe(400)
    expect((await wecken(undefined)).status).toBe(400)
    expect(gesendet).toEqual([])
  })

  test('nach zehn Versuchen in einer Minute ist Schluss', async () => {
    for (let versuch = 0; versuch < 10; versuch++) {
      expect((await wecken('aa:bb:cc:dd:ee:ff')).status).toBe(200)
    }

    expect((await wecken('aa:bb:cc:dd:ee:ff')).status).toBe(429)

    uhr += 61_000

    expect((await wecken('aa:bb:cc:dd:ee:ff')).status).toBe(200)
  })

  /**
   * Der Kern des Umbaus: der Waker führt keine Geräte mehr. Käme hier je wieder
   * eine Liste, wäre die Token-Bündelung des alten Hubs zurück — er lieferte die
   * Agent-Tokens **aller** Geräte an jeden aus, der das Hub-Token kannte.
   */
  test.each(['/api/devices', '/api/devices/status', '/api/wol/pc'])(
    '%s gibt es nicht mehr',
    async (pfad) => {
      expect((await fetch(`${base}${pfad}`)).status).toBe(404)
    },
  )

  test('gekoppelt wird über Code, Challenge und Unterschrift', async () => {
    const { code } = (await (
      await fetch(`${base}/api/pair/code`, { method: 'POST' })
    ).json()) as { code: string }

    expect(code).toMatch(/^\d{6}$/)

    const antwort = await fetch(`${base}/api/pair`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code, label: 'Handy', publicKey: 'kein Schlüssel' }),
    })

    // Der Code stimmt, der Schlüssel nicht — geprüft wird also beides.
    expect(antwort.status).toBe(400)
  })
})
