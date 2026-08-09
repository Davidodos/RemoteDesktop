import { describe, expect, it } from 'vitest'
import { LEGACY_CAPABILITIES, type Capability } from '../lib/capabilities.ts'
import { pageAvailable } from './Sidebar.tsx'

/** Was ein Handy meldet (Phase 28–30). */
const HANDY: readonly Capability[] = ['screen', 'input', 'files']

describe('pageAvailable', () => {
  it('lässt am Windows-Rechner alles stehen', () => {
    for (const page of ['screen', 'mouse', 'keyboard', 'power', 'media', 'actions', 'shortcuts'] as const) {
      expect(pageAvailable(page, LEGACY_CAPABILITIES)).toBe(true)
    }
  })

  it('gibt am Handy Bild, Maus und Tastatur frei', () => {
    expect(pageAvailable('screen', HANDY)).toBe(true)
    expect(pageAvailable('mouse', HANDY)).toBe(true)
    expect(pageAvailable('keyboard', HANDY)).toBe(true)
  })

  it('lässt am Handy weg, was es nicht kann', () => {
    expect(pageAvailable('power', HANDY)).toBe(false)
    expect(pageAvailable('media', HANDY)).toBe(false)
    expect(pageAvailable('actions', HANDY)).toBe(false)
  })

  it('nimmt die Shortcuts mit, wo keine echten Tasten ankommen', () => {
    expect(pageAvailable('shortcuts', HANDY)).toBe(false)
    expect(pageAvailable('shortcuts', LEGACY_CAPABILITIES)).toBe(true)
  })

  it('hält Geräte und Einstellungen immer offen — sie betreffen die App', () => {
    expect(pageAvailable('devices', [])).toBe(true)
    expect(pageAvailable('settings', [])).toBe(true)
  })
})
