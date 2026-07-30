import { Router } from 'express'
import type { HubConfig } from './config.js'
import { toDeviceView } from './config.js'
import { probe } from './probe.js'
import { sendMagicPacket } from './wol.js'

export function createApiRouter(config: HubConfig): Router {
  const router = Router()

  const findDevice = (id: string) => config.devices.find((device) => device.id === id)

  /**
   * Geräteliste inklusive Agent-Token — die App verbindet damit direkt zum
   * Agent, ohne Umweg über die NAS. Deshalb ist diese Route hinter dem
   * Hub-Token gesperrt.
   */
  router.get('/devices', (_request, response) => {
    response.json({ devices: config.devices.map(toDeviceView) })
  })

  /**
   * Online-Status aller Geräte. Parallel abgefragt, damit ein schlafender
   * Rechner nicht die Antwort für die anderen blockiert.
   */
  router.get('/devices/status', async (_request, response) => {
    const statuses = await Promise.all(
      config.devices.map(async (device) => ({
        id: device.id,
        ...(await probe(device.host, device.port)),
      })),
    )

    response.json({ statuses })
  })

  router.post('/wol/:id', async (request, response) => {
    const device = findDevice(request.params.id)

    if (device === undefined) {
      response.status(404).json({ error: `Unbekanntes Gerät '${request.params.id}'.` })
      return
    }

    if (device.mac === undefined) {
      response.status(400).json({
        error: `Für '${device.name}' ist keine MAC-Adresse hinterlegt — Aufwecken nicht möglich.`,
      })
      return
    }

    try {
      await sendMagicPacket(device.mac, config.broadcastAddress)
      console.info(`Magic Packet an ${device.name} (${device.mac}) gesendet.`)

      // Bewusst kein Warten auf das Hochfahren: das dauert bis zu einer
      // Minute. Die App pollt danach den Status.
      response.json({ status: 'sent', device: device.id })
    } catch (error) {
      console.error(`Wake-on-LAN für ${device.name} fehlgeschlagen:`, error)
      response.status(500).json({
        error: `Magic Packet konnte nicht gesendet werden: ${(error as Error).message}`,
      })
    }
  })

  return router
}
