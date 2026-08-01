import { readFileSync } from 'node:fs'
import { createServer as createHttpServer } from 'node:http'
import { createServer as createHttpsServer } from 'node:https'
import express from 'express'
import { requireClient } from './auth.js'
import { ClientStore } from './clients.js'
import { loadConfig } from './config.js'
import { PairingService } from './pairing.js'
import { createApiRouter } from './routes.js'
import { detectSiteId } from './site.js'

async function main(): Promise<void> {
  const config = loadConfig()
  const clients = new ClientStore(config.clientsPath)
  const pairing = new PairingService(clients)

  // Erzwungene Kennung schlägt die ermittelte: ohne host-Netz oder auf einem
  // System ohne /proc bleibt sonst nur „Standort unbekannt", und dann findet
  // der Client diesen Waker nie.
  const siteId = config.siteId ?? (await detectSiteId())

  const app = express()

  app.disable('x-powered-by')

  // Hinter einem Reverse-Proxy stünde sonst dessen Adresse in request.ip, und
  // die lokal-only-Sperre wäre wirkungslos. Der Waker läuft im host-Netz und
  // wird direkt angesprochen — also niemandem glauben.
  app.set('trust proxy', false)

  // Die App wird nicht mehr von hier ausgeliefert, sondern ist eine APK oder
  // ein WebView2-Fenster — jeder Aufruf ist damit Cross-Origin. Ohne diese
  // Freigabe verwirft der Browser die Antworten, und zwar lautlos. Beliebige
  // Herkunft ist vertretbar, weil ausschließlich über das Sitzungstoken
  // autorisiert wird: Cookies gibt es nicht, also kann eine fremde Seite im
  // Browser nichts erreichen, was sie nicht ohnehin dürfte.
  app.use((request, response, next) => {
    response.setHeader('Access-Control-Allow-Origin', '*')
    response.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type')
    response.setHeader('Access-Control-Allow-Methods', 'GET, POST, DELETE, OPTIONS')

    if (request.method === 'OPTIONS') {
      response.status(204).end()
      return
    }

    next()
  })

  app.use(express.json({ limit: '16kb' }))

  app.get('/health', (_request, response) => {
    response.json({ status: 'ok', siteId: siteId ?? null })
  })

  app.use('/api', requireClient(pairing), createApiRouter({ config, pairing, siteId }))

  const server = createServer(app, config.certificatePath, config.keyPath)

  server.listen(config.port, () => {
    const schema = config.certificatePath === undefined ? 'http' : 'https'

    console.info(
      `RemoteDesktop-Waker lauscht auf ${schema}://…:${config.port} ` +
        `(Standort ${siteId ?? 'unbekannt'}, ${clients.list().length} gekoppelte(r) Client(s))`,
    )

    if (config.certificatePath === undefined) {
      console.warn(
        'Ohne Zertifikat (CERTIFICATE_PATH / KEY_PATH aus "tailscale cert") ist der Waker ' +
          'nur im Klartext erreichbar — die App läuft unter https und der Browser lässt ' +
          'eine http-Anfrage von dort nicht durch.',
      )
    }

    if (siteId === undefined) {
      console.warn(
        'Standort-Kennung unbekannt — der Client kann diesen Waker keinem Netz zuordnen. ' +
          'Läuft der Container mit network_mode: host?',
      )
    }

    if (clients.list().length === 0) {
      console.info(
        'Noch kein Client gekoppelt. Code holen: ' +
          `curl -X POST http://localhost:${config.port}/api/pair/code`,
      )
    }
  })
}

/**
 * Mit Zertifikat https, ohne http.
 *
 * Der Waker terminiert selbst, statt einen Reverse-Proxy zu verlangen: er läuft
 * ohnehin im host-Netz und braucht dasselbe `tailscale cert` wie jeder Agent.
 */
function createServer(
  app: express.Express,
  certificatePath: string | undefined,
  keyPath: string | undefined,
): ReturnType<typeof createHttpServer> | ReturnType<typeof createHttpsServer> {
  if (certificatePath === undefined || keyPath === undefined) {
    return createHttpServer(app)
  }

  return createHttpsServer(
    { cert: readFileSync(certificatePath), key: readFileSync(keyPath) },
    app,
  )
}

main().catch((error: unknown) => {
  console.error('Waker konnte nicht starten:', error)
  process.exit(1)
})
