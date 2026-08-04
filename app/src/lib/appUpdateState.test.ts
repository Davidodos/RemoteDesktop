import { describe, expect, test } from 'vitest'
import { describeAppUpdate, type AppUpdateState } from './appUpdateState.ts'

describe('describeAppUpdate', () => {
  test('das Angebot nennt die Fassung und einen Knopf', () => {
    const labels = describeAppUpdate({ kind: 'offer', version: '1.2.0' })

    expect(labels.text).toContain('1.2.0')
    expect(labels.action).toBeDefined()
    expect(labels.visible).toBe(true)
  })

  test('solange nichts anliegt, bleibt der Bereich unsichtbar', () => {
    // „Alles in Ordnung" kostet Platz und Aufmerksamkeit für eine Nachricht,
    // die niemand braucht — und beim Start stünde sie bei jedem Öffnen da.
    expect(describeAppUpdate({ kind: 'checking' }).visible).toBe(false)
    expect(describeAppUpdate({ kind: 'current' }).visible).toBe(false)
  })

  test('während der Installation gibt es nichts zu drücken', () => {
    const labels = describeAppUpdate({ kind: 'installing' })

    expect(labels.action).toBeUndefined()
    expect(labels.visible).toBe(true)
  })

  test('ein Fehlschlag ist sichtbar und wiederholbar', () => {
    // Still zu scheitern wäre das Schlimmste: dann wartet jemand auf ein
    // Update, das nie kommt, und weiß nicht, warum.
    const labels = describeAppUpdate({ kind: 'failed', message: 'GitHub antwortete mit 403.' })

    expect(labels.text).toBe('GitHub antwortete mit 403.')
    expect(labels.action).toBeDefined()
    expect(labels.visible).toBe(true)
  })

  test('jeder Zustand hat einen Satz', () => {
    const alle: AppUpdateState[] = [
      { kind: 'checking' },
      { kind: 'current' },
      { kind: 'offer', version: '1.0.0' },
      { kind: 'installing' },
      { kind: 'failed', message: 'x' },
    ]

    for (const state of alle) {
      expect(describeAppUpdate(state).text.length).toBeGreaterThan(0)
    }
  })
})
