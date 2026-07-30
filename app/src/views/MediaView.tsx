import { useCallback, useEffect, useMemo, useState } from 'react'
import type { AgentClient } from '../lib/agentClient.ts'
import { currentPosition, formatTime, progressRatio } from '../lib/mediaProgress.ts'
import type { MediaAction, MediaSession } from '../lib/types.ts'
import {
  type IconComponent,
  NextIcon,
  PlayPauseIcon,
  PrevIcon,
  VolumeDownIcon,
  VolumeMuteIcon,
  VolumeUpIcon,
} from './icons.tsx'

/**
 * Takt, in dem nachgesehen wird, was läuft. Schnell genug, dass ein
 * Titelwechsel nicht auffällt, langsam genug, dass es keine Rolle spielt.
 */
const POLL_INTERVAL_MS = 4000

/** Takt, in dem die Fortschrittsleiste zwischen zwei Abfragen weiterrückt. */
const PROGRESS_TICK_MS = 500

/** Wie Windows den Wiedergabestatus nennt, und was davon in der App steht. */
const STATUS_LABELS: Record<string, string> = {
  playing: 'läuft',
  paused: 'pausiert',
  stopped: 'gestoppt',
  changing: 'lädt…',
  opened: 'bereit',
  closed: 'beendet',
}

interface MediaButton {
  action: MediaAction
  label: string
  icon: IconComponent
  repeat?: number
}

const MEDIA_BUTTONS: MediaButton[] = [
  { action: 'prev', label: 'Zurück', icon: PrevIcon },
  { action: 'playpause', label: 'Abspielen oder pausieren', icon: PlayPauseIcon },
  { action: 'next', label: 'Weiter', icon: NextIcon },
  { action: 'voldown', label: 'Leiser', icon: VolumeDownIcon, repeat: 2 },
  { action: 'mute', label: 'Stumm', icon: VolumeMuteIcon },
  { action: 'volup', label: 'Lauter', icon: VolumeUpIcon, repeat: 2 },
]

interface Props {
  agent: AgentClient
  deviceName: string
  /** Über dem Bildschirmbild wird nur das Nötigste gezeigt. */
  compact?: boolean
  onError: (message: string) => void
}

/** Was gerade läuft, und die Steuerung dafür. */
export function MediaView({
  agent,
  deviceName,
  compact = false,
  onError,
}: Props): React.JSX.Element {
  const [sessions, setSessions] = useState<MediaSession[]>([])
  const [selected, setSelected] = useState<string | undefined>(undefined)

  /** Wann die aktuellen Angaben abgerufen wurden — Bezugspunkt der Leiste. */
  const [fetchedAt, setFetchedAt] = useState(() => performance.now())

  /** Zählt hoch, damit die Leiste zwischen zwei Abfragen weiterläuft. */
  const [tick, setTick] = useState(0)

  /**
   * Nachsehen, was läuft. Fehler landen bewusst nicht im Fehlerbanner: die
   * Abfrage wiederholt sich ohnehin, und ein kurzer Aussetzer beim Titelwechsel
   * wäre kein Grund für eine Meldung über den halben Bildschirm.
   */
  const refresh = useCallback((): void => {
    agent
      .getMediaSessions()
      .then((found) => {
        setSessions(found)
        setFetchedAt(performance.now())
        setSelected((current) =>
          current !== undefined && found.some((session) => session.id === current)
            ? current
            : (found.find((session) => session.isCurrent)?.id ?? found[0]?.id),
        )
      })
      .catch(() => setSessions([]))
  }, [agent])

  useEffect(() => {
    refresh()

    const timer = window.setInterval(() => {
      if (!document.hidden) {
        refresh()
      }
    }, POLL_INTERVAL_MS)

    return () => window.clearInterval(timer)
  }, [refresh])

  // Die Leiste läuft zwischen den Abfragen aus eigener Kraft weiter. Läuft
  // nichts, wird auch nicht gezählt — ein Timer, der im Hintergrund nur
  // Standbilder neu zeichnet, kostet nur Akku.
  useEffect(() => {
    if (!sessions.some((session) => session.status === 'playing')) {
      return
    }

    const timer = window.setInterval(() => {
      if (!document.hidden) {
        setTick((value) => value + 1)
      }
    }, PROGRESS_TICK_MS)

    return () => window.clearInterval(timer)
  }, [sessions])

  const run = (action: MediaAction, repeat?: number): void => {
    navigator.vibrate?.(15)

    agent
      .media(action, repeat, selected)
      // Nach einer Aktion kurz warten, bis die App auf dem Rechner den neuen
      // Zustand gemeldet hat — sonst zeigt die Anzeige noch den alten.
      .then(() => window.setTimeout(refresh, 400))
      .catch((error: unknown) =>
        onError(error instanceof Error ? error.message : String(error)),
      )
  }

  // tick steht bewusst in der Abhängigkeit: er ist der Auslöser dafür, dass
  // hier überhaupt neu gerechnet wird.
  const elapsed = useMemo(() => performance.now() - fetchedAt, [fetchedAt, tick])

  // Über dem Bild ist kein Platz für mehrere Karten — dort zählt nur die, die
  // auch gesteuert wird.
  const shown = compact
    ? sessions.filter((session) => session.id === selected)
    : sessions

  return (
    <div className="media-view">
      <div className="key-group">
        {!compact && (
          <span className="key-group-label">
            {sessions.length > 1 ? 'Läuft gerade — tippen wählt aus' : 'Läuft gerade'}
          </span>
        )}

        {sessions.length === 0 ? (
          <p className="now-playing-empty">Auf {deviceName} läuft gerade nichts.</p>
        ) : (
          shown.map((session) => (
            <button
              key={session.id}
              type="button"
              className={session.id === selected ? 'now-playing selected' : 'now-playing'}
              onClick={() => setSelected(session.id)}
            >
              {session.hasThumbnail ? (
                <img
                  className="now-playing-cover"
                  src={agent.thumbnailUrl(session.id, session.title)}
                  alt=""
                />
              ) : (
                <span className="now-playing-cover placeholder-cover">♪</span>
              )}

              <span className="now-playing-text">
                <span className="now-playing-title">
                  {session.title.length > 0 ? session.title : 'Ohne Titel'}
                </span>
                <span className="now-playing-artist">
                  {session.artist.length > 0 ? session.artist : session.album}
                </span>
                <span className="now-playing-app">
                  {session.app} · {STATUS_LABELS[session.status] ?? session.status}
                </span>

                {session.durationSeconds > 0 && (
                  <span className="progress">
                    <span className="progress-track">
                      <span
                        className="progress-fill"
                        style={{ width: `${progressRatio(session, elapsed) * 100}%` }}
                      />
                    </span>
                    <span className="progress-time">
                      {formatTime(currentPosition(session, elapsed))} /{' '}
                      {formatTime(session.durationSeconds)}
                    </span>
                  </span>
                )}
              </span>
            </button>
          ))
        )}
      </div>

      <div className="key-group">
        {!compact && <span className="key-group-label">Steuerung</span>}
        <div className="media-grid">
          {MEDIA_BUTTONS.map(({ action, label, icon: Glyph, repeat }) => (
            <button
              key={action}
              type="button"
              className="media-button"
              aria-label={label}
              onClick={() => run(action, repeat)}
            >
              <Glyph />
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
