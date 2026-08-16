import { clientPrivateKey } from '../lib/clientKey.ts'
import type { Device } from '../lib/types.ts'
import {
  pairedCredentials,
  staticCredentials,
  type Credentials,
  type SessionExchange,
} from './credentials.ts'
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
export function directTransport(device: Device, credentials = credentialsFor(device)): Transport {
  return new DirectTransport(device, credentials)
}

/**
 * Wählt den Ausweis anhand dessen, was am Gerät hinterlegt ist. Gekoppelt
 * gewinnt: steht beides da, ist das Token nur ein Überbleibsel aus der Zeit
 * vor der Kopplung.
 *
 * <p>
 * **Entschieden wird allein an `clientId`** — daran, dass dieses Gerät
 * gekoppelt *ist*. Vorher hing die Entscheidung zusätzlich daran, ob im
 * Speicher der App ein privater Schlüssel liegt, und das war seit 31h die
 * falsche Frage: der Ausweis gehört seither der Gegenstelle dieses Geräts
 * (`clientkey.txt` am Handy, `{app}\data\clientkey.json` am Rechner), und die
 * antwortet nur asynchron. Solange noch ein Rest aus der Zeit davor im
 * Speicher lag, fiel es nicht auf. Nach einer wirklich sauberen
 * Neuinstallation fiel jede Anfrage auf `staticCredentials('')` zurück — ein
 * leeres Bearer-Token —, und der Agent notierte für jede einzelne „Abgelehnt
 * (Nicht angemeldet.)".
 * </p>
 */
export function credentialsFor(device: Device): Credentials {
  if (device.clientId !== undefined) {
    return pairedCredentials(device.clientId, clientPrivateKey, sessionExchange(device))
  }

  return staticCredentials(device.token ?? '')
}

class DirectTransport implements Transport {
  constructor(
    private readonly device: Device,
    private readonly credentials: Credentials,
  ) {}

  private get baseUrl(): string {
    return `https://${this.device.host}:${this.device.port}`
  }

  private get socketUrl(): string {
    return `wss://${this.device.host}:${this.device.port}`
  }

  async control<T>(request: ControlRequest): Promise<T> {
    const token = this.credentials.peek() ?? (await this.credentials.obtain())

    try {
      return await this.send<T>(request, token)
    } catch (failure) {
      // Genau ein zweiter Anlauf: nach zwölf Stunden ist die Sitzung abgelaufen,
      // und das darf der Nutzer nicht als Fehler zu sehen bekommen. Bei einem
      // dauerhaft abgelehnten Ausweis würde eine Schleife daraus.
      if (!(failure instanceof TransportError) || failure.status !== 401) {
        throw failure
      }

      this.credentials.invalidate()

      return await this.send<T>(request, await this.credentials.obtain())
    }
  }

  private async send<T>(request: ControlRequest, token: string): Promise<T> {
    const { path, method = 'GET', body } = request

    let response: Response

    try {
      response = await fetch(`${this.baseUrl}${path}`, {
        method,
        headers: {
          Authorization: `Bearer ${token}`,
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

  /**
   * Ein `<img>` kann nicht warten, bis eine Anmeldung durch ist — die Adresse
   * muss sofort dastehen. Das geht, weil vor jedem solchen Bild bereits eine
   * gewöhnliche Anfrage gelaufen ist (die Liste, zu der das Bild gehört) und
   * das Sitzungstoken damit vorliegt.
   */
  resourceUrl(path: string, query: Record<string, string> = {}): string {
    return withToken(this.baseUrl + path, query, this.credentials.peek() ?? '')
  }

  /**
   * Nach einem Neustart des Agents ist das Sitzungstoken weg — es lag nur in
   * seinem Arbeitsspeicher. Wer das merkt, sagt es hier.
   */
  reauthenticate(): void {
    this.credentials.invalidate()
  }

  inputChannel(handlers: ChannelHandlers): Channel {
    return this.openChannel('/ws/input', {}, handlers, false)
  }

  screenStream(monitor: number, handlers: ChannelHandlers): Channel {
    return this.openChannel('/ws/screen', { monitor: String(monitor) }, handlers, true)
  }

  /**
   * Öffnet sofort, wenn der Ausweis schon vorliegt, und sonst nach der
   * Anmeldung. Bis dahin ist der Kanal geschlossen und verwirft, was gesendet
   * wird — dasselbe Verhalten wie in den ersten Millisekunden jeder
   * WebSocket-Verbindung.
   */
  private openChannel(
    path: string,
    query: Record<string, string>,
    handlers: ChannelHandlers,
    binary: boolean,
  ): Channel {
    const channel = new SocketChannel(handlers, binary)
    const address = (token: string): string => withToken(this.socketUrl + path, query, token)
    const token = this.credentials.peek()

    if (token !== undefined) {
      channel.attach(address(token))
      return channel
    }

    this.credentials.obtain().then(
      (fresh) => channel.attach(address(fresh)),
      () => handlers.onError?.(),
    )

    return channel
  }
}

/** Die beiden Anmelde-Endpunkte des Agents. Sie brauchen selbst keinen Ausweis. */
function sessionExchange(device: Device): SessionExchange {
  const base = `https://${device.host}:${device.port}`

  return {
    challenge: async (clientId) => {
      const { nonce } = await postJson<{ nonce: string }>(`${base}/api/session/challenge`, {
        clientId,
      })

      return nonce
    },

    open: async (clientId, nonce, signature) => {
      const { token } = await postJson<{ token: string }>(`${base}/api/session`, {
        clientId,
        nonce,
        signature,
      })

      return token
    },
  }
}

/**
 * Meldet einen gekoppelten Client an einem Agent an, der ihn noch gar nicht
 * kennt — deshalb ohne Ausweis und ohne den Transport, der ja einen bräuchte.
 */
export async function postJson<T>(url: string, body: unknown): Promise<T> {
  let response: Response

  try {
    response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
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
  private socket: WebSocket | undefined
  private closed = false

  constructor(
    private readonly handlers: ChannelHandlers,
    private readonly binary: boolean,
  ) {}

  /** Baut die Verbindung auf, sobald die Adresse samt Ausweis feststeht. */
  attach(url: string): void {
    // Wer zwischen Anmeldung und Verbindung schon wieder weggewischt hat, soll
    // keinen Socket bekommen, der niemand mehr zuhört.
    if (this.closed) {
      return
    }

    const socket = new WebSocket(url)

    if (this.binary) {
      socket.binaryType = 'arraybuffer'
    }

    this.socket = socket

    socket.addEventListener('open', () => this.handlers.onOpen?.())
    socket.addEventListener('close', () => this.handlers.onClose?.())
    socket.addEventListener('error', () => this.handlers.onError?.())

    socket.addEventListener('message', (event) => {
      const data: unknown = event.data

      if (typeof data === 'string') {
        this.handlers.onText?.(data)
        return
      }

      this.handlers.onBinary?.(data as ArrayBuffer)
    })
  }

  get isOpen(): boolean {
    // Die Prüfung auf den Socket steht ausdrücklich davor: solange die
    // Anmeldung läuft, gibt es keinen, und ein Vergleich gegen `undefined`
    // soll nicht zufällig zutreffen können.
    return this.socket !== undefined && this.socket.readyState === WebSocket.OPEN
  }

  send(payload: string): void {
    if (!this.isOpen) {
      return
    }

    this.socket?.send(payload)
  }

  close(): void {
    this.closed = true
    this.socket?.close()
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
