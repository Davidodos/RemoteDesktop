import { existsSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import express from 'express'
import { createReleaseRouter } from './agentRelease.js'
import { requireHubToken } from './auth.js'
import { loadConfig } from './config.js'
import { createApiRouter } from './routes.js'

const here = dirname(fileURLToPath(import.meta.url))

const PORT = Number(process.env.HUB_PORT ?? 3080)
const CONFIG_PATH = process.env.DEVICES_PATH ?? resolve(here, '../devices.json')
const STATIC_PATH = process.env.STATIC_PATH ?? resolve(here, '../public')

async function main(): Promise<void> {
  const config = await loadConfig(CONFIG_PATH)

  const app = express()

  app.disable('x-powered-by')
  app.use(express.json({ limit: '64kb' }))

  app.get('/health', (_request, response) => {
    response.json({ status: 'ok', devices: config.devices.length })
  })

  app.use('/api', requireHubToken(config.hubToken), createApiRouter(config))

  // Die Agent-Datei fürs Selbst-Update. Hinter demselben Token wie der Rest —
  // eine frei herunterladbare Binärdatei wäre eine Einladung.
  app.use('/api', requireHubToken(config.hubToken), createReleaseRouter())

  // Die gebaute PWA. Bleibt ohne Token erreichbar — sie ist nur eine leere
  // Hülle, solange niemand das Hub-Token eingegeben hat.
  if (existsSync(STATIC_PATH)) {
    app.use(express.static(STATIC_PATH))

    // SPA-Fallback: alle unbekannten Pfade an die App, damit Deep-Links und
    // ein Reload auf einer Unterseite nicht ins Leere laufen.
    app.get(/^(?!\/api|\/health).*/, (_request, response) => {
      response.sendFile(resolve(STATIC_PATH, 'index.html'))
    })
  } else {
    console.warn(`Kein PWA-Build unter ${STATIC_PATH} — nur die API ist verfügbar.`)
  }

  app.listen(PORT, () => {
    console.info(
      `RemoteDesktop-Hub lauscht auf Port ${PORT} ` +
        `(${config.devices.length} Gerät(e): ${config.devices.map((d) => d.name).join(', ')})`,
    )
  })
}

main().catch((error: unknown) => {
  console.error('Hub konnte nicht starten:', error)
  process.exit(1)
})
