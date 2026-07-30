import { describe, expect, test } from 'vitest'
import { currentPosition, formatTime, progressRatio } from './mediaProgress.ts'
import type { MediaSession } from './types.ts'

function session(overrides: Partial<MediaSession> = {}): MediaSession {
  return {
    id: 'Spotify.exe',
    app: 'Spotify',
    title: 'Ein Lied',
    artist: 'Jemand',
    album: '',
    status: 'playing',
    isCurrent: true,
    hasThumbnail: false,
    positionSeconds: 60,
    durationSeconds: 240,
    positionAgeSeconds: 0,
    ...overrides,
  }
}

describe('Position hochrechnen', () => {
  test('ohne verstrichene Zeit gilt die gemeldete Position', () => {
    expect(currentPosition(session(), 0)).toBe(60)
  })

  test('während der Wiedergabe läuft sie weiter', () => {
    // Act — zwei Sekunden nach dem Abruf.
    const position = currentPosition(session(), 2000)

    // Assert
    expect(position).toBe(62)
  })

  test('das Alter der Meldung wird mitgerechnet', () => {
    // Arrange — Windows hatte die Position schon vor 3 s zuletzt gemeldet.
    const position = currentPosition(session({ positionAgeSeconds: 3 }), 1000)

    // Assert — sonst liefe die Leiste dauerhaft hinterher.
    expect(position).toBe(64)
  })

  test('pausiert bleibt sie stehen', () => {
    // Act
    const position = currentPosition(session({ status: 'paused', positionAgeSeconds: 5 }), 10_000)

    // Assert
    expect(position).toBe(60)
  })

  test('am Ende ist Schluss', () => {
    // Act — deutlich länger gewartet als das Stück dauert.
    const position = currentPosition(session(), 600_000)

    // Assert
    expect(position).toBe(240)
  })

  test('ohne bekannte Länge wird nicht geklemmt', () => {
    // Arrange — Livestream.
    const position = currentPosition(session({ durationSeconds: 0 }), 60_000)

    // Assert
    expect(position).toBe(120)
  })
})

describe('Anteil für die Leiste', () => {
  test('die Hälfte ergibt 0,5', () => {
    expect(progressRatio(session({ positionSeconds: 120 }), 0)).toBe(0.5)
  })

  test('ohne Länge gibt es keinen Anteil', () => {
    // Assert — die App zeichnet dann gar keine Leiste.
    expect(progressRatio(session({ durationSeconds: 0 }), 0)).toBe(0)
  })

  test('der Anteil überschreitet nie eins', () => {
    expect(progressRatio(session(), 999_000)).toBe(1)
  })
})

describe('Zeiten formatieren', () => {
  test('Minuten und Sekunden', () => {
    expect(formatTime(62)).toBe('1:02')
  })

  test('unter einer Minute', () => {
    expect(formatTime(9)).toBe('0:09')
  })

  test('ab einer Stunde kommt die Stunde dazu', () => {
    expect(formatTime(3661)).toBe('1:01:01')
  })

  test('Nachkommastellen werden abgeschnitten', () => {
    expect(formatTime(59.9)).toBe('0:59')
  })

  test('unsinnige Werte ergeben null', () => {
    // Assert — sonst stünde bei einem Aussetzer „NaN:NaN" in der App.
    expect(formatTime(Number.NaN)).toBe('0:00')
    expect(formatTime(-5)).toBe('0:00')
  })
})
