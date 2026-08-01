/**
 * Ein Gerät, das die App steuern kann — gekoppelt oder aus dem Hub.
 *
 * Es gibt genau zwei Arten, sich auszuweisen, und jedes Gerät hat eine davon:
 * die Kopplung aus Phase 10 (`clientId`, dazu der eigene Schlüssel im
 * Schlüsselspeicher) oder das alte geteilte `token`. Der alte Weg bleibt bis
 * Phase 12 — sonst sperrt man sich vom eigenen PC aus.
 */
export interface Device {
  id: string
  name: string
  /** MagicDNS-Name des Rechners. */
  host: string
  port: number
  /** Pre-Shared-Token des Agents, solange das Gerät nicht gekoppelt ist. */
  token?: string
  /** Kennung, die der Agent bei der Kopplung vergeben hat. */
  clientId?: string
  /**
   * Fingerabdruck des Agent-Schlüssels aus der Kopplung. Bleibt gleich, auch
   * wenn der Rechner umbenannt wird — daran erkennt die App ihn wieder.
   */
  fingerprint?: string
  /**
   * Ob dieser Knoten seinerseits andere wecken kann. Jeder Agent kann das, ein
   * Waker kann sonst nichts. Nicht zu verwechseln mit „lässt sich wecken" —
   * dafür braucht es {@link Device.mac} und {@link Device.siteId}.
   */
  canWake: boolean
  /**
   * Die MAC dieses Rechners, gemerkt aus `/api/info`, solange er wach war.
   * Sie gehört ins Magic Packet — ohne sie kann ihn niemand wecken.
   */
  mac?: string
  /**
   * In welchem Netz der Rechner zuletzt stand (`sha256` der Gateway-MAC).
   * Geweckt werden kann er nur von einem Knoten mit derselben Kennung: ein
   * Magic Packet kommt über keinen Router.
   */
  siteId?: string
  /** Ein Knoten, der nur wecken kann — die NAS, ein Pi am zweiten Standort. */
  waker?: boolean
}

/**
 * Warum ein Gerät nicht erreichbar ist. `dns` heißt: der Name ließ sich nicht
 * auflösen — dann fehlt das Tailscale-DNS und es liegt nicht am Rechner.
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
  /** Fassung des Agents, für die Anzeige. Fehlt bei Agents vor Phase 14. */
  version?: string
  /**
   * Die Sprache, die der Agent spricht. Weicht sie von {@link CLIENT_PROTOCOL}
   * ab, sagt die App klar, welche Seite zu alt ist — sonst scheitert sie später
   * an einer Nachricht, die die Gegenseite nicht kennt, und das sieht nach
   * einem kaputten Rechner aus.
   */
  protocol?: number
  /** Standort-Kennung und MAC, damit dieser Rechner später geweckt werden kann. */
  siteId?: string
  mac?: string
  /** Ob dieser Rechner seinerseits Nachbarn wecken kann. */
  canWake?: boolean
}

/**
 * Die Protokollfassung, die dieser Client spricht. Gegenstück zu
 * `AgentVersion.Protocol` im Agent — beide werden zusammen erhöht, und nur
 * dann, wenn eine Änderung die alte Seite nicht mehr versteht.
 */
export const CLIENT_PROTOCOL = 1

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

/**
 * Eine Aktion, die der Zielrechner anbietet — so, wie `GET /api/actions` sie
 * meldet.
 *
 * Was dahintersteckt, steht ausschließlich in der `actions.json` auf jenem
 * Rechner. Hier ist absichtlich weder ein Pfad noch ein Argument dabei: wer
 * einen Knopf bauen will, braucht das nicht, und wer die Liste abfragen darf,
 * muss nicht auch erfahren, welche Software dort liegt und wo.
 */
export interface AgentActionSummary {
  id: string
  label: string
  /** Name eines Symbols, oder `undefined` für die Vorgabe. */
  icon?: string
  /** `process`, `script`, `keys`, `url` oder `sequence`. */
  type: string
  /** Verlangt eine Rückfrage, bevor ausgelöst wird. */
  confirm: boolean
}
