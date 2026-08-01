import { describe, expect, test } from 'vitest'
import { normalizeMac, parseArpEntry, parseDefaultGateway, siteIdFromGatewayMac } from './site.js'

/**
 * Die Standort-Kennung muss auf beiden Seiten dieselbe sein — hier und in
 * `agent/Services/SiteIdentity.cs`. Weicht eine ab, findet der Client nie einen
 * Waker und der Weckknopf bliebe grundlos aus.
 */
describe('normalizeMac', () => {
  test.each(['AA:BB:CC:DD:EE:FF', 'aa-bb-cc-dd-ee-ff', 'AABBCCDDEEFF', 'aabb.ccdd.eeff'])(
    'bringt %s auf eine vergleichbare Form',
    (mac) => {
      expect(normalizeMac(mac)).toBe('aa:bb:cc:dd:ee:ff')
    },
  )

  test.each([undefined, '', 'aa:bb:cc:dd:ee', 'zz:bb:cc:dd:ee:ff'])(
    'verwirft %s statt es zurechtzubiegen',
    (mac) => {
      expect(normalizeMac(mac)).toBeUndefined()
    },
  )

  test('die Nulladresse zählt nicht als MAC', () => {
    // Sie melden Schnittstellen ohne eigene Adresse. Als Kennung wäre sie die
    // eine, die überall gleich ist — genau der Fehler, den es zu vermeiden gilt.
    expect(normalizeMac('00:00:00:00:00:00')).toBeUndefined()
  })
})

describe('siteIdFromGatewayMac', () => {
  test('gleiches Gateway ergibt dieselbe Kennung', () => {
    expect(siteIdFromGatewayMac('AA:BB:CC:DD:EE:FF')).toBe(siteIdFromGatewayMac('aabbccddeeff'))
  })

  test('ein anderes Gateway ergibt eine andere Kennung', () => {
    expect(siteIdFromGatewayMac('aa:bb:cc:dd:ee:ff')).not.toBe(
      siteIdFromGatewayMac('aa:bb:cc:dd:ee:00'),
    )
  })

  test('die Kennung verrät die MAC nicht', () => {
    const siteId = siteIdFromGatewayMac('aa:bb:cc:dd:ee:ff')

    expect(siteId).toHaveLength(64)
    expect(siteId).not.toContain('aabbccddeeff')
  })

  /**
   * Der Wert muss Byte für Byte dem entsprechen, was der Agent rechnet:
   * sha256 über die normalisierte MAC als UTF-8-Text. Ein fest eingetragener
   * Vergleichswert schlägt an, sobald eine der beiden Seiten etwas ändert.
   */
  test('rechnet genauso wie der Agent', () => {
    expect(siteIdFromGatewayMac('aa:bb:cc:dd:ee:ff')).toBe(
      'c1582e87c802221899199e286ead9a7ed13eb3b5e3827be6cc149fb82a9e04f7',
    )
  })

  test('ohne brauchbare MAC gibt es keine Kennung', () => {
    expect(siteIdFromGatewayMac(undefined)).toBeUndefined()
    expect(siteIdFromGatewayMac('kein Gateway')).toBeUndefined()
  })
})

describe('parseDefaultGateway', () => {
  const route = [
    'Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask',
    'eth0\t0000A8C0\t00000000\t0001\t0\t0\t0\t00FFFFFF',
    'eth0\t00000000\t0102A8C0\t0003\t0\t0\t0\t00000000',
  ].join('\n')

  test('findet die Standardroute und dreht die Bytes um', () => {
    // /proc/net/route schreibt Adressen little-endian: 0102A8C0 ist 192.168.2.1.
    expect(parseDefaultGateway(route)).toBe('192.168.2.1')
  })

  test('ohne Standardroute gibt es kein Gateway', () => {
    expect(
      parseDefaultGateway('Iface\tDestination\tGateway\neth0\t0000A8C0\t00000000\t0001'),
    ).toBeUndefined()
  })

  test('eine leere Datei wirft nicht', () => {
    expect(parseDefaultGateway('')).toBeUndefined()
  })
})

describe('parseArpEntry', () => {
  const arp = [
    'IP address       HW type     Flags       HW address            Mask     Device',
    '192.168.2.1      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0',
    '192.168.2.33     0x1         0x0         00:00:00:00:00:00     *        eth0',
  ].join('\n')

  test('findet die MAC zur gesuchten Adresse', () => {
    expect(parseArpEntry(arp, '192.168.2.1')).toBe('aa:bb:cc:dd:ee:ff')
  })

  test('ein unvollständiger Eintrag zählt nicht', () => {
    // Flags 0x0 heißt „noch keine Antwort" — die Nulladresse ist keine MAC.
    expect(parseArpEntry(arp, '192.168.2.33')).toBeUndefined()
  })

  test('eine unbekannte Adresse ergibt nichts', () => {
    expect(parseArpEntry(arp, '10.0.0.1')).toBeUndefined()
  })
})
