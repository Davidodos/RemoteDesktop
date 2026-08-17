import { describe, expect, it } from 'vitest'
import { describeReport, type ConnectionReport } from './connectionTest.ts'
import type { Device } from './types.ts'

const handy: Device = {
  id: 'a1',
  name: 'Handy',
  host: '192.168.178.30',
  port: 8443,
  clientId: 'pc-1',
  canWake: false,
}

function report(rest: Partial<ConnectionReport> = {}): ConnectionReport {
  return {
    reachable: true,
    hostname: 'Handy',
    capabilities: ['screen', 'input'],
    scopesThere: ['screen', 'input'],
    reverse: { kind: 'granted', scopes: ['screen', 'input', 'media'] },
    ...rest,
  }
}

/**
 * **Der Befund dahinter (17.08.2026):** für drei verschiedene Lagen stand
 * derselbe Satz da — „Zurück steht nichts bereit — neu koppeln." Am echten
 * Gerät hieß das: die Gegenrichtung funktionierte nachweislich, und der Test
 * behauptete das Gegenteil. Wer daraufhin neu koppelt, repariert etwas, das
 * nicht kaputt war.
 */
describe('describeReport', () => {
  it('nennt beide Richtungen mit ihren Rechten', () => {
    expect(describeReport(handy, report())).toBe(
      'Handy: screen, input. Zurück: screen, input, media.',
    )
  })

  it('„neu koppeln" nur, wenn wirklich nachgesehen wurde', () => {
    expect(describeReport(handy, report({ reverse: { kind: 'missing' } }))).toContain(
      'steht hier nicht in der Liste — neu koppeln',
    )
  })

  /**
   * Eine fehlende Kennung ist kein Befund über die Gegenrichtung, sondern
   * einer über diese Geräteliste. „Neu koppeln" wäre hier geraten.
   */
  it('ohne Kennung wird nichts behauptet', () => {
    const satz = describeReport(handy, report({ reverse: { kind: 'unknown' } }))

    expect(satz).toContain('nicht nachsehbar')
    expect(satz).not.toContain('neu koppeln')
  })

  it('eine unlesbare Liste ist eine Störung und kein Befund', () => {
    const satz = describeReport(
      handy,
      report({ reverse: { kind: 'unreadable', failure: 'Das Fenster hat nicht geantwortet.' } }),
    )

    expect(satz).toContain('Das Fenster hat nicht geantwortet.')
    expect(satz).not.toContain('neu koppeln')
  })

  it('eingetragen und ohne Rechte ist etwas anderes als nicht eingetragen', () => {
    expect(describeReport(handy, report({ reverse: { kind: 'granted', scopes: [] } }))).toContain(
      'Zurück: nichts erlaubt.',
    )
  })

  it('unerreichbar nennt den Grund', () => {
    expect(
      describeReport(handy, report({ reachable: false, failure: 'HTTP 401', scopesThere: undefined })),
    ).toContain('Nicht erreichbar: HTTP 401')
  })
})
