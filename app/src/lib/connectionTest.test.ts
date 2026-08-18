import { describe, expect, it } from 'vitest'
import {
  describeReport,
  missingScopes,
  type ConnectionReport,
} from './connectionTest.ts'
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
  /**
   * **Der zweite Befund (18.08.2026):** hier stand die vollständige Liste der
   * Rechte, die dieses Gerät drüben hat. Sie war jedes Mal richtig und jedes Mal
   * nutzlos: um zu wissen, ob etwas fehlt, musste man die Sollmenge im Kopf
   * haben — und die hängt davon ab, was für ein Gerät dort steht. Jetzt steht
   * dort entweder, dass nichts fehlt, oder was fehlt.
   */
  it('sagt bei vollständigen Rechten, dass nichts fehlt', () => {
    expect(describeReport(handy, report())).toBe(
      'Handy: alle Rechte verfügbar. Zurück: eingetragen (Bild, Eingabe, Medien).',
    )
  })

  it('nennt nur die fehlenden Rechte', () => {
    expect(describeReport(handy, report({ scopesThere: ['screen'] }))).toContain(
      'es fehlt Eingabe',
    )
  })

  /**
   * Ein Handy hat keine Medien, keine Energieverwaltung und keine Aktionen. Sie
   * als fehlend zu führen wäre eine Mängelliste über Dinge, die es nie gab.
   */
  it('führt nur als fehlend, was diese Art Gerät überhaupt anbietet', () => {
    expect(missingScopes(['screen', 'input'], ['screen', 'input'])).toEqual([])
    expect(missingScopes(['screen', 'input'], ['input'])).toEqual(['screen'])
  })

  /** Ein Agent ohne Fähigkeitsliste ist ein Windows-Agent von vor V4. */
  it('ohne Fähigkeitsliste gilt die Sollmenge eines Rechners', () => {
    expect(missingScopes([], ['screen', 'input'])).toEqual([
      'media',
      'power',
      'actions',
      'wake',
    ])
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
    const satz = describeReport(handy, report({ reverse: { kind: 'granted', scopes: [] } }))

    expect(satz).toContain('ist eingetragen, darf aber nichts')
    expect(satz).not.toContain('neu koppeln')
  })

  it('unerreichbar nennt den Grund', () => {
    expect(
      describeReport(handy, report({ reachable: false, failure: 'HTTP 401', scopesThere: undefined })),
    ).toContain('Nicht erreichbar: HTTP 401')
  })
})
