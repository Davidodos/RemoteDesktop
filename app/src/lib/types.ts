/** Ein Gerät, wie der Hub es ausliefert. */
export interface Device {
  id: string
  name: string
  /** MagicDNS-Name des Rechners. */
  host: string
  port: number
  /** Pre-Shared-Token des Agents — die App verbindet direkt dorthin. */
  token: string
  /** Ob eine MAC hinterlegt ist und der Hub das Gerät wecken kann. */
  canWake: boolean
}

/**
 * Warum ein Gerät nicht erreichbar ist. `dns` heißt: der Hub kennt den Namen
 * nicht — dann liegt es an der NAS und nicht am Rechner.
 */
export type OfflineReason = 'dns' | 'unreachable'

export interface DeviceStatus {
  id: string
  online: boolean
  reason?: OfflineReason
}

/** Ein Monitor, wie der Agent ihn meldet. */
export interface Monitor {
  index: number
  width: number
  height: number
  x: number
  y: number
  primary: boolean
  name: string
}

export interface AgentInfo {
  hostname: string
  monitors: Monitor[]
}

export type PowerAction = 'sleep' | 'shutdown' | 'restart' | 'lock'

export type MediaAction = 'playpause' | 'next' | 'prev' | 'stop' | 'volup' | 'voldown' | 'mute'

export type MouseButton = 'left' | 'right' | 'middle'

/** Verbindungszustand des Eingabe-Sockets, für die Statusanzeige. */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected'

/** Qualitätsstufe des Bildstreams. `auto` regelt der Agent selbst. */
export type QualityMode = 'auto' | 'high' | 'medium' | 'low'

/** Erste Nachricht auf dem Bild-Socket: Format des kommenden Streams. */
export interface ScreenMeta {
  monitor: number
  width: number
  height: number
  fps: number
  /** Wie viele Monitore der Agent insgesamt sieht. */
  count: number
}

/** Sekündliche Kennzahlen des Bildstreams, für die Debug-Anzeige. */
export interface ScreenStats {
  fps: number
  kbps: number
  quality: number
  scale: number
  mode: QualityMode
  /** Nur beim H.264-Stream gesetzt: der Encoder auf dem Rechner. */
  encoder?: string
}

/** Eine laufende Medien-Wiedergabe auf dem Rechner. */
export interface MediaSession {
  /** Windows-Kennung der App — dient zugleich als Adresse für gezielte Befehle. */
  id: string
  app: string
  title: string
  artist: string
  album: string
  /** `playing`, `paused`, `stopped`, … wie Windows es meldet. */
  status: string
  /** Die Sitzung, an die Windows die Medien-Tasten schickt. */
  isCurrent: boolean
  hasThumbnail: boolean
  /** Stand der Wiedergabe zum Zeitpunkt der Meldung, in Sekunden. */
  positionSeconds: number
  /** Gesamtlänge; 0 bei Livestreams und unbekannter Länge. */
  durationSeconds: number
  /** Wie alt die Positionsangabe beim Abrufen schon war. */
  positionAgeSeconds: number
}
