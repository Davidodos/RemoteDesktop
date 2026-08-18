import { directTransport } from '../transport/direct.ts'
import { TransportError, type Transport } from '../transport/index.ts'
import type {
  AgentActionSummary,
  AgentInfo,
  Device,
  MediaAction,
  MediaSession,
  PowerAction,
} from './types.ts'

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

  /**
   * Sich bei diesem Gerät selbst austragen.
   *
   * Der eine Weg, auf dem ein „Entfernen" **beide** Seiten trifft:
   * `/api/clients/{id}` ist nur am Gerät selbst erreichbar und soll das
   * bleiben. Hier trägt sich niemand einen anderen aus, sondern nur sich
   * selbst — wer die Kennung nennt, ist der Sitzungstoken.
   */
  async unpair(): Promise<void> {
    await this.request('/api/unpair', { method: 'DELETE' })
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

  /**
   * Was dieser Rechner auf Zuruf tun darf.
   *
   * Die Liste kommt vom Zielgerät und nicht aus dem Speicher des Handys: was
   * eine Aktion bedeutet, steht ausschließlich dort. Der Client schickt beim
   * Auslösen nur die Kennung — nie eine Kommandozeile.
   */
  async getActions(): Promise<AgentActionSummary[]> {
    const { actions } = await this.request<{ actions: AgentActionSummary[] }>('/api/actions')
    return actions
  }

  async invokeAction(id: string): Promise<void> {
    await this.request(`/api/actions/${encodeURIComponent(id)}/invoke`, { method: 'POST' })
  }

  /**
   * Lässt diesen Knoten ein Magic Packet an die genannte MAC senden.
   *
   * Der Knoten ist nicht das Ziel, sondern der Bote: er steht im Netz des
   * schlafenden Rechners. Welcher das ist, entscheidet `lib/wake.ts`.
   */
  async wake(mac: string): Promise<void> {
    await this.request('/api/wol', { method: 'POST', body: { mac } })
  }

  /**
   * Stößt die Update-Prüfung an. Findet der Agent etwas, tauscht er sich aus
   * und startet neu — die Antwort kommt vorher, danach gäbe es keine mehr.
   */
  async update(): Promise<UpdateReport> {
    return await this.request<UpdateReport>('/api/update', { method: 'POST' })
  }

  /**
   * Das **ganze** Update: Agent, Fenster und Oberfläche über den Installer.
   *
   * <p>
   * Der Weg, auf dem ein Rechner sich von einem gekoppelten Gerät aus erneuern
   * lässt. {@link update} tauscht nur die Programmdatei des Agents — ändert sich
   * die Oberfläche, und das ist der häufigere Fall, bliebe sie auf dem Stand von
   * vorher. Windows fragt dabei nichts nach: der Agent läuft ohnehin mit den
   * nötigen Rechten (siehe `agent/Services/InstallerUpdate.cs`).
   * </p>
   *
   * <p>
   * Der Rechner ist danach etwa eine Minute lang nicht erreichbar. Die Antwort
   * kommt vorher — was danach passiert, sieht man daran, dass er wiederkommt.
   * </p>
   */
  async updateApp(): Promise<UpdateReport> {
    return await this.request<UpdateReport>('/api/update/app', { method: 'POST' })
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
    options: { method?: 'POST' | 'DELETE'; body?: unknown } = {},
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
      return (
        `${this.device.name} antwortet nicht. Läuft der Rechner, und ist Tailscale ` +
        'auf beiden Geräten an? Schläft er, hilft der Weckknopf in der Geräteliste.'
      )
    }

    if (cause.status === 401) {
      return this.device.clientId === undefined
        ? `${this.device.name} hat den Zugang abgelehnt. Dieser Eintrag stammt noch aus ` +
          'einer Zeit vor der Kopplung — einmal neu koppeln, dann ist er in Ordnung.'
        : `${this.device.name} kennt dieses Gerät nicht mehr. Am Rechner „Geräte koppeln…“ ` +
          'öffnen und den Code noch einmal scannen.'
    }

    if (cause.status === 403) {
      return cause.serverMessage ?? `${this.device.name} verweigert diese Aktion.`
    }

    return cause.serverMessage ?? `${this.device.name} antwortete mit HTTP ${cause.status}.`
  }
}

/** Was aus `POST /api/update` zurückkommt. */
export interface UpdateReport {
  /** `installing`, `uptodate`, `disabled`, `rejected`, … */
  status: string
  version?: string
  /** Ein Satz, den die App unverändert anzeigen kann. */
  message: string
}

export class AgentError extends Error {
  constructor(message: string, options: { cause?: unknown } = {}) {
    super(message, options)
    this.name = 'AgentError'
  }
}
