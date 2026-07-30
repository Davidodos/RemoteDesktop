import type { Device } from '../lib/types.ts'
import {
  TransportError,
  type Channel,
  type ChannelHandlers,
  type ControlRequest,
  type Transport,
} from './index.ts'

/**
 * Der gebaute Weg: HTTPS und WSS direkt an den Agent auf `host:8443`.
 *
 * Direktverbindung zum Rechner statt über die NAS — deshalb braucht der Agent
 * ein eigenes Tailscale-Zertifikat (siehe agent/README.md).
 */
export function directTransport(device: Device): Transport {
  return new DirectTransport(device)
}

class DirectTransport implements Transport {
  constructor(private readonly device: Device) {}

  private get baseUrl(): string {
    return `https://${this.device.host}:${this.device.port}`
  }

  private get socketUrl(): string {
    return `wss://${this.device.host}:${this.device.port}`
  }

  async control<T>(request: ControlRequest): Promise<T> {
    const { path, method = 'GET', body } = request

    let response: Response

    try {
      response = await fetch(`${this.baseUrl}${path}`, {
        method,
        headers: {
          Authorization: `Bearer ${this.device.token}`,
          ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      })
    } catch (cause) {
      throw new TransportError('Verbindung zum Agent nicht zustande gekommen.', { cause })
    }

    if (!response.ok) {
      throw new TransportError(`HTTP ${response.status}`, {
        status: response.status,
        serverMessage: await readServerError(response),
      })
    }

    return (await readBody(response)) as T
  }

  resourceUrl(path: string, query: Record<string, string> = {}): string {
    return withToken(this.baseUrl + path, query, this.device.token)
  }

  inputChannel(handlers: ChannelHandlers): Channel {
    const url = withToken(this.socketUrl + '/ws/input', {}, this.device.token)

    return new SocketChannel(url, handlers, false)
  }

  screenStream(monitor: number, handlers: ChannelHandlers): Channel {
    const url = withToken(
      this.socketUrl + '/ws/screen',
      { monitor: String(monitor) },
      this.device.token,
    )

    return new SocketChannel(url, handlers, true)
  }
}

/**
 * Hängt die Abfrage samt Token an.
 *
 * Browser können weder bei `<img>` noch bei WebSockets eigene Header setzen —
 * deshalb steht das Token im Query-String. Die Verbindung ist TLS-verschlüsselt.
 */
function withToken(url: string, query: Record<string, string>, token: string): string {
  const parts = Object.entries(query).map(
    ([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`,
  )

  parts.push(`token=${encodeURIComponent(token)}`)

  return `${url}?${parts.join('&')}`
}

/** Hülle um einen WebSocket, die nur das nach außen gibt, was ein Kanal kann. */
class SocketChannel implements Channel {
  private readonly socket: WebSocket

  constructor(url: string, handlers: ChannelHandlers, binary: boolean) {
    const socket = new WebSocket(url)

    if (binary) {
      socket.binaryType = 'arraybuffer'
    }

    this.socket = socket

    socket.addEventListener('open', () => handlers.onOpen?.())
    socket.addEventListener('close', () => handlers.onClose?.())
    socket.addEventListener('error', () => handlers.onError?.())

    socket.addEventListener('message', (event) => {
      const data: unknown = event.data

      if (typeof data === 'string') {
        handlers.onText?.(data)
        return
      }

      handlers.onBinary?.(data as ArrayBuffer)
    })
  }

  get isOpen(): boolean {
    return this.socket.readyState === WebSocket.OPEN
  }

  send(payload: string): void {
    if (!this.isOpen) {
      return
    }

    this.socket.send(payload)
  }

  close(): void {
    this.socket.close()
  }
}

/** Holt die Klartextmeldung des Agents; ohne sie bleibt nur der Statuscode. */
async function readServerError(response: Response): Promise<string | undefined> {
  try {
    const body = (await response.json()) as { error?: string }

    return typeof body.error === 'string' ? body.error : undefined
  } catch {
    return undefined
  }
}

/**
 * Antworten ohne Inhalt gibt es auch — ein `DELETE` etwa muss nichts sagen.
 * Die geben `undefined` zurück, statt an einem leeren JSON-Text zu scheitern.
 */
async function readBody(response: Response): Promise<unknown> {
  const text = await response.text()

  if (text.length === 0) {
    return undefined
  }

  try {
    return JSON.parse(text)
  } catch (cause) {
    throw new TransportError('Antwort des Agents war kein JSON.', { cause, status: response.status })
  }
}
