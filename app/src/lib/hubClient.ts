import type { Device, DeviceStatus } from './types.ts'

/**
 * Zugriff auf den Hub auf der NAS.
 *
 * Die App wird vom Hub selbst ausgeliefert, deshalb reichen relative Pfade —
 * kein Hostname im Code, der beim Umzug der NAS falsch würde.
 */
export class HubClient {
  constructor(private readonly token: string) {}

  async getDevices(): Promise<Device[]> {
    const { devices } = await this.request<{ devices: Device[] }>('/api/devices')
    return devices
  }

  async getStatuses(): Promise<DeviceStatus[]> {
    const { statuses } = await this.request<{ statuses: DeviceStatus[] }>('/api/devices/status')
    return statuses
  }

  async wake(deviceId: string): Promise<void> {
    await this.request(`/api/wol/${encodeURIComponent(deviceId)}`, { method: 'POST' })
  }

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    let response: Response

    try {
      response = await fetch(path, {
        ...init,
        headers: { ...init.headers, Authorization: `Bearer ${this.token}` },
      })
    } catch (cause) {
      throw new HubError('Hub nicht erreichbar. Läuft Tailscale?', { cause })
    }

    if (response.status === 401) {
      throw new HubError('Hub-Token abgelehnt.', { unauthorized: true })
    }

    if (!response.ok) {
      const message = await extractError(response)
      throw new HubError(message)
    }

    return (await response.json()) as T
  }
}

export class HubError extends Error {
  readonly unauthorized: boolean

  constructor(message: string, options: { cause?: unknown; unauthorized?: boolean } = {}) {
    super(message, { cause: options.cause })
    this.name = 'HubError'
    this.unauthorized = options.unauthorized ?? false
  }
}

/** Holt die Fehlermeldung des Servers, fällt auf den Statuscode zurück. */
async function extractError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string }

    if (typeof body.error === 'string') {
      return body.error
    }
  } catch {
    // Antwort war kein JSON — dann eben der Statuscode.
  }

  return `Hub antwortete mit HTTP ${response.status}.`
}
