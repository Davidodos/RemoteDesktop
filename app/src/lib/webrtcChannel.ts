import type { Device, ScreenStats } from './types.ts'

/** Wie lange auf das Sammeln der ICE-Kandidaten gewartet wird. */
const ICE_TIMEOUT_MS = 2000

/** Und wie lange darauf, dass die Verbindung wirklich steht. */
const CONNECT_TIMEOUT_MS = 8000

/** Takt der Statistik-Abfrage. */
const STATS_INTERVAL_MS = 1000

/** So lange darf eine verbundene Sitzung nichts liefern, bevor sie als tot gilt. */
const SILENCE_LIMIT_S = 5

interface WebRtcCallbacks {
  onStats: (stats: ScreenStats) => void
  /** Verbindung ist weggebrochen — der Aufrufer schaltet dann auf JPEG zurück. */
  onLost: () => void
}

/**
 * Der H.264-Stream über WebRTC.
 *
 * Anders als beim JPEG-Weg dekodiert hier der Browser in Hardware, und das
 * Bild kommt als echter Videostrom an. Klappt irgendetwas davon nicht — kein
 * ffmpeg auf dem Rechner, kein Encoder, keine Verbindung — meldet die Klasse
 * das zurück, statt es zu verschleiern: die App fällt dann auf den
 * JPEG-Stream zurück, der überall läuft.
 */
export class WebRtcChannel {
  private peer: RTCPeerConnection | undefined
  private statsTimer: number | undefined
  private sessionId: string | undefined
  private lastBytes = 0
  private lastStatsAt = 0
  private silentSeconds = 0
  private encoder: string | undefined

  constructor(
    private readonly device: Device,
    private readonly callbacks: WebRtcCallbacks,
  ) {}

  get isConnected(): boolean {
    return this.peer?.connectionState === 'connected'
  }

  /**
   * Baut die Verbindung auf und hängt den Videostrom an das Element.
   * Gibt zurück, ob H.264 zustande gekommen ist.
   */
  async connect(video: HTMLVideoElement, monitor: number, fps: number): Promise<boolean> {
    this.close()

    // Keine STUN- oder TURN-Server: beide Enden hängen im selben Tailnet.
    const peer = new RTCPeerConnection({ iceServers: [] })
    this.peer = peer

    peer.addTransceiver('video', { direction: 'recvonly' })

    peer.addEventListener('track', (event) => {
      video.srcObject = event.streams[0] ?? new MediaStream([event.track])
    })

    peer.addEventListener('connectionstatechange', () => {
      if (peer.connectionState === 'failed' || peer.connectionState === 'disconnected') {
        this.callbacks.onLost()
      }
    })

    const offer = await peer.createOffer()
    await peer.setLocalDescription(offer)
    await waitForIceGathering(peer)

    const answer = await this.postOffer(peer.localDescription?.sdp ?? offer.sdp ?? '', monitor, fps)

    if (answer === undefined) {
      this.close()
      return false
    }

    this.sessionId = answer.id
    this.encoder = answer.encoder

    await peer.setRemoteDescription({ type: 'answer', sdp: answer.sdp })

    if (!(await this.waitForConnection(peer))) {
      this.close()
      return false
    }

    this.startStats()

    return true
  }

  /**
   * Wechselt den Monitor innerhalb der bestehenden Verbindung — der Videostrom
   * bleibt, nur die Quelle dahinter wechselt.
   */
  async switchMonitor(monitor: number): Promise<boolean> {
    if (this.sessionId === undefined) {
      return false
    }

    const response = await fetch(
      `https://${this.device.host}:${this.device.port}/api/webrtc/${this.sessionId}/monitor`,
      {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${this.device.token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ monitor }),
      },
    ).catch(() => undefined)

    if (response?.ok !== true) {
      return false
    }

    const body = (await response.json()) as { encoder?: string }
    this.encoder = body.encoder

    return true
  }

  close(): void {
    if (this.statsTimer !== undefined) {
      clearInterval(this.statsTimer)
      this.statsTimer = undefined
    }

    if (this.sessionId !== undefined) {
      // Der Agent räumt seinen ffmpeg-Prozess sonst erst auf, wenn die
      // Verbindung von selbst zerfällt — das dauert.
      void fetch(
        `https://${this.device.host}:${this.device.port}/api/webrtc/${this.sessionId}`,
        { method: 'DELETE', headers: { Authorization: `Bearer ${this.device.token}` } },
      ).catch(() => undefined)

      this.sessionId = undefined
    }

    this.peer?.close()
    this.peer = undefined
    this.lastBytes = 0
    this.silentSeconds = 0
  }

  private async postOffer(
    sdp: string,
    monitor: number,
    fps: number,
  ): Promise<{ id: string; sdp: string; encoder?: string } | undefined> {
    const response = await fetch(
      `https://${this.device.host}:${this.device.port}/api/webrtc/offer`,
      {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${this.device.token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ sdp, monitor, fps }),
      },
    ).catch(() => undefined)

    if (response?.ok !== true) {
      return undefined
    }

    return (await response.json()) as { id: string; sdp: string; encoder?: string }
  }

  private async waitForConnection(peer: RTCPeerConnection): Promise<boolean> {
    if (peer.connectionState === 'connected') {
      return true
    }

    return new Promise<boolean>((resolve) => {
      const timer = window.setTimeout(() => finish(false), CONNECT_TIMEOUT_MS)

      const finish = (success: boolean): void => {
        clearTimeout(timer)
        peer.removeEventListener('connectionstatechange', onChange)
        resolve(success)
      }

      const onChange = (): void => {
        if (peer.connectionState === 'connected') {
          finish(true)
        } else if (peer.connectionState === 'failed' || peer.connectionState === 'closed') {
          finish(false)
        }
      }

      peer.addEventListener('connectionstatechange', onChange)
    })
  }

  /** Bildrate und Datenrate kommen hier vom Browser statt vom Agent. */
  private startStats(): void {
    this.lastStatsAt = performance.now()

    this.statsTimer = window.setInterval(() => {
      void this.peer?.getStats().then((report) => {
        report.forEach((entry) => {
          if (entry.type !== 'inbound-rtp' || entry.kind !== 'video') {
            return
          }

          const now = performance.now()
          const seconds = Math.max((now - this.lastStatsAt) / 1000, 0.001)
          const bytes = (entry.bytesReceived as number) ?? 0

          // Eine stehende Verbindung ohne ankommende Daten ist der unangenehmste
          // Fall: alles meldet „verbunden", das Bild bleibt trotzdem schwarz.
          // Passiert, wenn der Encoder auf dem Rechner nach dem Verbindungsaufbau
          // aussteigt.
          this.silentSeconds = bytes > this.lastBytes ? 0 : this.silentSeconds + seconds

          if (this.silentSeconds > SILENCE_LIMIT_S) {
            this.callbacks.onLost()
            return
          }

          this.callbacks.onStats({
            fps: Math.round(((entry.framesPerSecond as number) ?? 0) * 10) / 10,
            kbps: Math.round(((bytes - this.lastBytes) * 8) / 1000 / seconds),
            quality: 0,
            scale: 1,
            mode: 'auto',
            encoder: this.encoder,
          })

          this.lastBytes = bytes
          this.lastStatsAt = now
        })
      })
    }, STATS_INTERVAL_MS)
  }
}

/**
 * Wartet, bis der Browser seine Kandidaten gesammelt hat.
 *
 * Der Agent nimmt sie nur gebündelt im Angebot entgegen. Bleibt das Sammeln
 * hängen, geht es nach kurzer Zeit trotzdem weiter — im Tailnet reicht der
 * erste Kandidat ohnehin.
 */
function waitForIceGathering(peer: RTCPeerConnection): Promise<void> {
  if (peer.iceGatheringState === 'complete') {
    return Promise.resolve()
  }

  return new Promise<void>((resolve) => {
    const timer = window.setTimeout(finish, ICE_TIMEOUT_MS)

    function finish(): void {
      clearTimeout(timer)
      peer.removeEventListener('icegatheringstatechange', onChange)
      resolve()
    }

    function onChange(): void {
      if (peer.iceGatheringState === 'complete') {
        finish()
      }
    }

    peer.addEventListener('icegatheringstatechange', onChange)
  })
}
