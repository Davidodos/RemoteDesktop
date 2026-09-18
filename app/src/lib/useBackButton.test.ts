import { describe, expect, test } from 'vitest'
import { decide } from './useBackButton.ts'
import type { Device } from './types.ts'

const pc: Device = { id: 'pc', name: 'PC', host: 'pc', port: 8443, clientId: 'x', canWake: false }

/** Die Zurück-Taste geht eine Ebene hoch und beendet erst auf der obersten. */
describe('Zurück-Taste', () => {
  test('schließt zuerst das Menü', () => {
    expect(decide({ menuOpen: true, pairing: true, selected: pc, page: 'screen' }, false)).toBe('closeMenu')
  })

  test('bricht dann die Kopplung ab', () => {
    expect(decide({ menuOpen: false, pairing: true, selected: pc, page: 'screen' }, false)).toBe('cancelPairing')
  })

  test('trennt eine Sitzung, statt sie zu verlassen', () => {
    expect(decide({ menuOpen: false, pairing: false, selected: pc, page: 'screen' }, false)).toBe('disconnect')
  })

  test('führt von jeder Seite zur Geräteliste', () => {
    expect(decide({ menuOpen: false, pairing: false, selected: undefined, page: 'settings' }, false)).toBe('devices')
  })

  test('warnt auf der Geräteliste und beendet erst beim zweiten Druck', () => {
    const top = { menuOpen: false, pairing: false, selected: undefined, page: 'devices' as const }

    expect(decide(top, false)).toBe('warn')
    expect(decide(top, true)).toBe('exit')
  })
})
