import { describe, expect, test } from 'vitest'
import { protocolMismatch } from './protocol.ts'
import { CLIENT_PROTOCOL, type AgentInfo } from './types.ts'

function info(protocol: number | undefined): AgentInfo {
  return { hostname: 'PC', monitors: [], ...(protocol === undefined ? {} : { protocol }) }
}

describe('protocolMismatch', () => {
  test('gleiche Fassung ergibt keine Meldung', () => {
    expect(protocolMismatch(info(CLIENT_PROTOCOL), 'PC')).toBeUndefined()
  })

  test('ein Agent vor Phase 14 meldet nichts und ist trotzdem in Ordnung', () => {
    // Er kennt das Feld nicht. Alles, was es damals gab, funktioniert weiter —
    // eine Warnung wäre hier nur Lärm.
    expect(protocolMismatch(info(undefined), 'PC')).toBeUndefined()
  })

  test('ein älterer Agent wird beim Namen genannt', () => {
    const meldung = protocolMismatch(info(CLIENT_PROTOCOL - 1), 'PC')

    expect(meldung).toContain('PC')
    expect(meldung).toContain('älter')
  })

  test('eine ältere App erfährt, dass sie selbst dran ist', () => {
    expect(protocolMismatch(info(CLIENT_PROTOCOL + 1), 'PC')).toContain('App aktualisieren')
  })
})
