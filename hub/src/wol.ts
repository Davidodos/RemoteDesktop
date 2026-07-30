import { createSocket } from 'node:dgram'

/** Übliche WOL-Ports. Router und NICs hören mal auf dem einen, mal auf dem anderen. */
const WOL_PORTS = [7, 9] as const

const SYNC_STREAM_LENGTH = 6
const MAC_REPEAT_COUNT = 16
const MAC_BYTE_LENGTH = 6

/**
 * Baut ein Magic Packet: sechs Bytes 0xFF, gefolgt von der MAC-Adresse
 * sechzehnmal hintereinander.
 *
 * Bewusst selbst implementiert statt als Abhängigkeit — es sind zwölf Zeilen,
 * und der Hub soll so wenig fremden Code wie möglich enthalten.
 */
export function buildMagicPacket(mac: string): Buffer {
  const bytes = parseMac(mac)
  const packet = Buffer.alloc(SYNC_STREAM_LENGTH + MAC_REPEAT_COUNT * MAC_BYTE_LENGTH)

  packet.fill(0xff, 0, SYNC_STREAM_LENGTH)

  for (let repeat = 0; repeat < MAC_REPEAT_COUNT; repeat++) {
    bytes.copy(packet, SYNC_STREAM_LENGTH + repeat * MAC_BYTE_LENGTH)
  }

  return packet
}

export function parseMac(mac: string): Buffer {
  const cleaned = mac.replace(/[:-]/g, '')

  if (!/^[0-9a-fA-F]{12}$/.test(cleaned)) {
    throw new Error(`Ungültige MAC-Adresse: ${mac}`)
  }

  return Buffer.from(cleaned, 'hex')
}

/**
 * Schickt das Magic Packet als Broadcast.
 *
 * Der Container braucht dafür `network_mode: host` — sonst landet der
 * Broadcast im Docker-Netz und erreicht den PC nie.
 */
export async function sendMagicPacket(mac: string, broadcastAddress: string): Promise<void> {
  const packet = buildMagicPacket(mac)
  const socket = createSocket('udp4')

  try {
    await new Promise<void>((resolve, reject) => {
      socket.once('error', reject)
      socket.bind(() => {
        socket.setBroadcast(true)
        resolve()
      })
    })

    // An beide Ports senden. Ein einzelnes verlorenes UDP-Paket bedeutet
    // sonst, dass der Rechner einfach nicht aufwacht.
    await Promise.all(
      WOL_PORTS.map(
        (port) =>
          new Promise<void>((resolve, reject) => {
            socket.send(packet, port, broadcastAddress, (error) => {
              if (error) {
                reject(error)
                return
              }
              resolve()
            })
          }),
      ),
    )
  } finally {
    socket.close()
  }
}
