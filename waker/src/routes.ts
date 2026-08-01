import { Router } from 'express'
import type { WakerConfig } from './config.js'
import type { PairingService } from './pairing.js'
import { normalizeMac } from './site.js'
import { sendMagicPacket } from './wol.js'

/** Wie oft dieser Waker höchstens in einer Minute sendet — kein Paket-Verstärker. */
const MAX_WAKES_PER_WINDOW = 10
const WAKE_WINDOW_MS = 60 * 1000

interface Options {
  config: WakerConfig
  pairing: PairingService
  /** Der Standort dieses Wakers. `undefined`, wenn er sich nicht ermitteln ließ. */
  siteId: string | undefined
  /** Für Tests: Uhr und Sendeweg lassen sich ersetzen. */
  now?: () => number
  send?: (mac: string, broadcastAddress: string) => Promise<void>
}

export function createApiRouter({
  config,
  pairing,
  siteId,
  now = Date.now,
  send = sendMagicPacket,
}: Options): Router {
  const router = Router()
  const recent: number[] = []

  /**
   * Die Selbstauskunft. Sie ist der ganze Grund, warum der Client ohne
   * Konfiguration auskommt: er fragt jeden bekannten Knoten, wer er ist, und
   * nimmt zum Wecken den mit derselben Standort-Kennung wie das Ziel.
   */
  router.get('/info', (_request, response) => {
    response.json({ role: 'waker', siteId, canWake: true })
  })

  router.post('/wol', (request, response) => {
    const mac = normalizeMac((request.body as { mac?: unknown } | undefined)?.mac as string)

    if (mac === undefined) {
      response.status(400).json({ error: 'Keine gültige MAC-Adresse.' })
      return
    }

    if (!reserve()) {
      response.status(429).json({ error: 'Zu viele Weckversuche. Bitte kurz warten.' })
      return
    }

    void send(mac, config.broadcastAddress).then(
      () => {
        console.info(`Magic Packet an ${mac} gesendet.`)

        // Bewusst kein Warten auf das Hochfahren: das dauert bis zu einer
        // Minute. Der Client fragt danach selbst nach.
        response.json({ status: 'sent', mac })
      },
      (error: unknown) => {
        console.error(`Wake-on-LAN für ${mac} fehlgeschlagen:`, error)
        response.status(500).json({ error: 'Magic Packet konnte nicht gesendet werden.' })
      },
    )
  })

  // ---- Kopplung ----------------------------------------------------------

  router.post('/pair/code', (_request, response) => {
    const code = pairing.issueCode()

    // Der Code steht bewusst auch im Log: das ist auf einem Server ohne
    // Bildschirm die einzige Stelle, an der ihn jemand ablesen kann.
    console.info(`Kopplungscode ${code} erzeugt, gültig 5 Minuten.`)

    response.json({ code, expiresInSeconds: 300 })
  })

  router.post('/pair', (request, response) => {
    const body = (request.body ?? {}) as { code?: string; label?: string; publicKey?: string }

    const { outcome, client } = pairing.pair(body.code ?? '', body.label ?? '', body.publicKey ?? '')

    if (outcome !== 'ok' || client === undefined) {
      response.status(400).json({ error: describePairFailure(outcome) })
      return
    }

    response.json({ clientId: client.id, role: 'waker', siteId, canWake: true })
  })

  router.post('/session/challenge', (request, response) => {
    const clientId = ((request.body ?? {}) as { clientId?: string }).clientId ?? ''
    const nonce = pairing.challenge(clientId)

    // Auch ein unbekannter Client bekommt 401 und nicht 404: dass eine Kennung
    // existiert, ist selbst schon eine Auskunft.
    if (nonce === undefined) {
      response.status(401).json({ error: 'Nicht gekoppelt.' })
      return
    }

    response.json({ nonce, expiresInSeconds: 60 })
  })

  router.post('/session', (request, response) => {
    const body = (request.body ?? {}) as { clientId?: string; nonce?: string; signature?: string }

    const { outcome, token } = pairing.openSession(
      body.clientId ?? '',
      body.nonce ?? '',
      body.signature ?? '',
    )

    // Alle Fehlschläge sehen gleich aus. Wer probiert, soll nicht erfahren, ob
    // die Kennung stimmte und nur die Unterschrift nicht passte.
    if (outcome !== 'ok' || token === undefined) {
      response.status(401).json({ error: 'Anmeldung fehlgeschlagen.' })
      return
    }

    response.json({ token, expiresInSeconds: 43200 })
  })

  router.delete('/clients/:id', (request, response) => {
    if (pairing.revoke(request.params.id)) {
      response.json({ revoked: request.params.id })
      return
    }

    response.status(404).json({ error: 'Unbekannter Client.' })
  })

  return router

  /** Gleitendes Fenster, damit der Waker nicht als Paket-Verstärker taugt. */
  function reserve(): boolean {
    const moment = now()

    while (recent.length > 0 && moment - (recent[0] ?? 0) > WAKE_WINDOW_MS) {
      recent.shift()
    }

    if (recent.length >= MAX_WAKES_PER_WINDOW) {
      return false
    }

    recent.push(moment)
    return true
  }
}

function describePairFailure(outcome: string): string {
  switch (outcome) {
    case 'bad-code':
      return 'Code falsch oder abgelaufen.'
    case 'bad-label':
      return 'Der Name des Geräts fehlt oder ist zu lang.'
    default:
      return 'Der öffentliche Schlüssel ist kein ECDSA-P-256-Schlüssel.'
  }
}
