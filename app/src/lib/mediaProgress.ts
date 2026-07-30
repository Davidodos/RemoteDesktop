import type { MediaSession } from './types.ts'

/**
 * Wo das Stück gerade steht, hochgerechnet auf jetzt.
 *
 * Windows schreibt die Position nicht laufend fort, sondern meldet sie nur bei
 * Änderungen und sagt dazu, wie alt die Angabe ist. Beim Abrufen kommt also
 * schon ein leicht veralteter Wert an; danach läuft die Wiedergabe weiter,
 * während die App bis zur nächsten Abfrage wartet. Beides wird hier
 * dazugerechnet — sonst ruckelte die Leiste im Abfragetakt vor sich hin.
 *
 * @param elapsedMs Zeit seit dem Abrufen dieser Sitzung.
 */
export function currentPosition(session: MediaSession, elapsedMs: number): number {
  const base = session.positionSeconds

  if (session.status !== 'playing') {
    return clamp(base, session.durationSeconds)
  }

  return clamp(base + session.positionAgeSeconds + elapsedMs / 1000, session.durationSeconds)
}

/** Anteil 0..1 für die Breite der Leiste. Ohne bekannte Länge: 0. */
export function progressRatio(session: MediaSession, elapsedMs: number): number {
  if (session.durationSeconds <= 0) {
    return 0
  }

  return currentPosition(session, elapsedMs) / session.durationSeconds
}

/** Sekunden als `m:ss`, ab einer Stunde als `h:mm:ss`. */
export function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) {
    return '0:00'
  }

  const total = Math.floor(seconds)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const rest = total % 60

  const padded = `${minutes.toString().padStart(hours > 0 ? 2 : 1, '0')}:${pad(rest)}`

  return hours > 0 ? `${hours}:${padded}` : padded
}

function pad(value: number): string {
  return value.toString().padStart(2, '0')
}

function clamp(value: number, duration: number): number {
  const lower = Math.max(value, 0)

  return duration > 0 ? Math.min(lower, duration) : lower
}
