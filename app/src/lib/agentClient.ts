import { directTransport } from '../transport/direct.ts'
import { TransportError, type Transport } from '../transport/index.ts'
import type { AgentInfo, Device, MediaAction, MediaSession, PowerAction } from './types.ts'

/**
 * Zugriff auf den Agent eines Geräts.
 *
 * Worüber die Anfragen laufen, steht hier nicht mehr — das weiß der Transport
 * (`transport/direct.ts`). Diese Klasse kennt nur noch die Endpunkte des
 * Agents und die Meldungen, die der Nutzer im Fehlerfall lesen soll.
 */
export class AgentClient {
  constructor(
    private readonly device: Device,
    private readonly transport: Transport = directTransport(device),
  ) {}

  async getInfo(): Promise<AgentInfo> {
    return this.request<AgentInfo>('/api/info')
  }

  async power(action: PowerAction): Promise<void> {
    await this.request('/api/power', { method: 'POST', body: { action } })
  }

  /**
   * `repeat` ist nur bei Lautstärke sinnvoll — der Agent begrenzt auf 10.
   *
   * Ist eine Sitzung angegeben, spricht der Agent genau diese App an, statt
   * die Medien-Taste ins Blaue zu drücken.
   */
  async media(action: MediaAction, repeat = 1, session?: string): Promise<void> {
    await this.request('/api/media', { method: 'POST', body: { action, repeat, session } })
  }

  /** Was auf dem Rechner gerade läuft. Leere Liste heißt: nichts. */
  async getMediaSessions(): Promise<MediaSession[]> {
    const { sessions } = await this.request<{ sessions: MediaSession[] }>('/api/media/sessions')
    return sessions
  }

  /**
   * Adresse des Titelbilds. Die Berechtigung muss in die Adresse, weil ein
   * <code>&lt;img&gt;</code> keine eigenen Header mitschicken kann.
   */
  thumbnailUrl(session: string, revision: string): string {
    return this.transport.resourceUrl('/api/media/thumbnail', {
      session,
      // Der Titel hängt mit in der Adresse, damit der Browser beim nächsten
      // Stück nicht das Cover des vorigen aus seinem Zwischenspeicher zeigt.
      v: revision,
    })
  }

  private async request<T>(
    path: string,
    options: { method?: 'POST'; body?: unknown } = {},
  ): Promise<T> {
    const { method = 'GET', body } = options

    try {
      return await this.transport.control<T>({ path, method, body })
    } catch (cause) {
      throw new AgentError(this.describeFailure(cause), { cause })
    }
  }

  /** Aus dem wortkargen Transportfehler wird hier ein lesbarer Satz. */
  private describeFailure(cause: unknown): string {
    if (!(cause instanceof TransportError)) {
      return cause instanceof Error ? cause.message : String(cause)
    }

    if (cause.status === undefined) {
      return `${this.device.name} nicht erreichbar. Läuft der Rechner und ist der Agent gestartet?`
    }

    if (cause.status === 401) {
      return `${this.device.name} hat das Token abgelehnt — stimmt es in devices.json?`
    }

    return cause.serverMessage ?? `${this.device.name} antwortete mit HTTP ${cause.status}.`
  }
}

export class AgentError extends Error {
  constructor(message: string, options: { cause?: unknown } = {}) {
    super(message, options)
    this.name = 'AgentError'
  }
}
