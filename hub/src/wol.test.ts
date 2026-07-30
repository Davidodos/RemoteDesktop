import { describe, expect, it } from 'vitest'
import { buildMagicPacket, parseMac } from './wol.js'

describe('parseMac', () => {
  it('akzeptiert Doppelpunkt-Schreibweise', () => {
    // Arrange & Act
    const bytes = parseMac('AA:BB:CC:DD:EE:FF')

    // Assert
    expect([...bytes]).toEqual([0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff])
  })

  it('akzeptiert Bindestrich-Schreibweise', () => {
    expect([...parseMac('aa-bb-cc-dd-ee-ff')]).toEqual([0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff])
  })

  it('ist unabhängig von Groß- und Kleinschreibung', () => {
    expect(parseMac('aA:bB:cC:dD:eE:fF')).toEqual(parseMac('AA:BB:CC:DD:EE:FF'))
  })

  it.each(['', 'AA:BB:CC:DD:EE', 'AA:BB:CC:DD:EE:FF:00', 'ZZ:BB:CC:DD:EE:FF', 'keine mac'])(
    'lehnt ungültige Eingabe ab: %s',
    (invalid) => {
      expect(() => parseMac(invalid)).toThrow()
    },
  )
})

describe('buildMagicPacket', () => {
  it('ist 102 Bytes lang', () => {
    // 6 Sync-Bytes + 16 Wiederholungen à 6 Byte.
    expect(buildMagicPacket('AA:BB:CC:DD:EE:FF')).toHaveLength(102)
  })

  it('beginnt mit sechs Bytes 0xFF', () => {
    // Arrange & Act
    const packet = buildMagicPacket('AA:BB:CC:DD:EE:FF')

    // Assert
    expect([...packet.subarray(0, 6)]).toEqual([0xff, 0xff, 0xff, 0xff, 0xff, 0xff])
  })

  it('wiederholt die MAC sechzehnmal', () => {
    // Arrange
    const mac = '01:23:45:67:89:AB'
    const expected = [0x01, 0x23, 0x45, 0x67, 0x89, 0xab]

    // Act
    const packet = buildMagicPacket(mac)

    // Assert
    for (let repeat = 0; repeat < 16; repeat++) {
      const offset = 6 + repeat * 6
      expect([...packet.subarray(offset, offset + 6)]).toEqual(expected)
    }
  })

  it('erzeugt für dieselbe MAC in beiden Schreibweisen dasselbe Paket', () => {
    expect(buildMagicPacket('AA:BB:CC:DD:EE:FF')).toEqual(buildMagicPacket('aa-bb-cc-dd-ee-ff'))
  })
})
