import { describe, expect, test } from 'vitest'
import { cleanName, MAX_NAME_LENGTH } from './ownName.ts'

/**
 * Der eigene Gerätename steht in jeder fremden Geräteliste. Was hier
 * durchrutscht, sieht später jemand auf einem anderen Gerät — und kann es dort
 * nicht mehr erklären.
 *
 * Dieselben Regeln wie in `setup/DeviceNameFile.cs` und `host/HostPreference.kt`.
 * Läuft eine der drei Fassungen auseinander, nimmt eine Seite einen Namen an,
 * den die andere verwirft.
 */
describe('cleanName', () => {
  test('Randflächen fallen weg', () => {
    expect(cleanName('  Wohnzimmer-PC  ')).toBe('Wohnzimmer-PC')
  })

  test('Steuerzeichen fallen weg', () => {
    // Ein Zeilenumbruch zerlegte die Datei am Rechner in zwei Zeilen — gelesen
    // wurde danach die erste, und der Rest war stillschweigend weg.
    expect(cleanName('Lap\ntop')).toBe('Laptop')
    expect(cleanName('Handy​')).toBe('Handy')
  })

  test('nur Leerzeichen ergibt keinen Namen', () => {
    expect(cleanName('   ')).toBe('')
    expect(cleanName('')).toBe('')
  })

  test('zu lang wird gekürzt', () => {
    // Länger nimmt die Gegenseite ihn nicht an (`DeviceProfile.MAX_NAME`).
    expect(cleanName('a'.repeat(MAX_NAME_LENGTH + 20))).toHaveLength(MAX_NAME_LENGTH)
  })

  test('nach dem Kürzen bleibt kein Leerzeichen am Ende stehen', () => {
    const name = `${'a'.repeat(MAX_NAME_LENGTH - 1)} bcd`

    expect(cleanName(name)).toBe('a'.repeat(MAX_NAME_LENGTH - 1))
  })
})
