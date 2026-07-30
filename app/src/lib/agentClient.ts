import type { AgentInfo, Device, MediaAction, MediaSession, PowerAction } from './types.ts'

/**
 * REST-Zugriff auf den Agent eines Geräts.
 *
 * Direktverbindung zum Rechner statt über die NAS — deshalb braucht der Agent
 * ein eigenes Tailscale-Zertifikat (siehe agent/README.md).
 */
export class AgentClient {
  constructor(private readonly device: Device) {}

  get baseUrl(): string {
    return `https://${this.device.host}:${this.device.port}`
  }

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
   * Adresse des Titelbilds. Das Token muss in die URL, weil ein
   * <code>&lt;img&gt;</code> keine eigenen Header mitschicken kann.
   */
  thumbnailUrl(session: string, revision: string): string {
    return (
      `${this.baseUrl}/api/media/thumbnail` +
      `?session=${encodeURIComponent(session)}&token=${encodeURIComponent(this.device.token)}` +
      // Der Titel hängt mit in der Adresse, damit der Browser beim nächsten
      // Stück nicht das Cover des vorigen aus seinem Zwischenspeicher zeigt.
      `&v=${encodeURIComponent(revision)}`
    )
  }

  private async request<T>(
    path: string,
    options: { method?: string; body?: unknown } = {},
  ): Promise<T> {
    const { method = 'GET', body } = options

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
      throw new AgentError(
        `${this.device.name} nicht erreichbar. Läuft der Rechner und ist der Agent gestartet?`,
        { cause },
      )
    }

    if (!response.ok) {
      throw new AgentError(await describeFailure(response, this.device.name))
    }

    return (await response.json()) as T
  }
}

export class AgentError extends Error {
  constructor(message: string, options: { cause?: unknown } = {}) {
    super(message, options)
    this.name = 'AgentError'
  }
}

async function describeFailure(response: Response, deviceName: string): Promise<string> {
  if (response.status === 401) {
    return `${deviceName} hat das Token abgelehnt — stimmt es in devices.json?`
  }

  try {
    const body = (await response.json()) as { error?: string }

    if (typeof body.error === 'string') {
      return body.error
    }
  } catch {
    // Kein JSON — Statuscode genügt.
  }

  return `${deviceName} antwortete mit HTTP ${response.status}.`
}
