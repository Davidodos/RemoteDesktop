import { describe, expect, test } from 'vitest'
import { deviceLabel } from './deviceNames.ts'
import type { Device } from './types.ts'

const PC: Device = {
  id: 'abc',
  name: 'DESKTOP-4711',
  host: 'pc.tailnet-1234.ts.net',
  port: 8443,
  clientId: 'handy-1',
  canWake: true,
}

describe('wie ein Rechner in dieser App heißt', () => {
  test('ohne eigenen Namen bleibt es der des Rechners', () => {
    expect(deviceLabel(PC)).toBe('DESKTOP-4711')
  })

  test('der eigene Name gewinnt', () => {
    // Er ist der, den jemand ausgesucht hat — und er steht nur auf diesem Gerät.
    expect(deviceLabel({ ...PC, alias: 'Arbeitsrechner' })).toBe('Arbeitsrechner')
  })

  test('ein leerer eigener Name zählt nicht', () => {
    // Sonst stünde in der Liste ein Gerät ohne Beschriftung.
    expect(deviceLabel({ ...PC, alias: '   ' })).toBe('DESKTOP-4711')
  })
})
