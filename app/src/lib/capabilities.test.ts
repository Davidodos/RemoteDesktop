import { describe, expect, it } from 'vitest'
import { can, capabilitiesOf, LEGACY_CAPABILITIES } from './capabilities.ts'
import type { AgentInfo } from './types.ts'

function info(capabilities?: string[]): AgentInfo {
  return { hostname: 'PC', monitors: [], capabilities }
}

describe('capabilitiesOf', () => {
  it('nimmt die Liste des Agents, wenn er eine schickt', () => {
    expect(capabilitiesOf(info(['screen', 'input', 'files']))).toEqual([
      'screen',
      'input',
      'files',
    ])
  })

  it('fällt bei einem Agent ohne das Feld auf die Liste von früher zurück', () => {
    expect(capabilitiesOf(info())).toEqual(LEGACY_CAPABILITIES)
  })

  it('gilt auch, solange die Auskunft noch aussteht', () => {
    expect(capabilitiesOf(undefined)).toEqual(LEGACY_CAPABILITIES)
  })

  it('kennt die Dateien nicht als Altbestand — den Dienst gab es damals nicht', () => {
    expect(LEGACY_CAPABILITIES).not.toContain('files')
  })

  it('verwirft, was diese App nicht kennt', () => {
    expect(capabilitiesOf(info(['screen', 'beamen']))).toEqual(['screen'])
  })

  it('eine leere Liste heißt: dieses Gerät kann nichts', () => {
    expect(capabilitiesOf(info([]))).toEqual([])
  })
})

describe('can', () => {
  it('antwortet für ein Handy nur auf Bild, Eingabe und Dateien', () => {
    const handy = info(['screen', 'input', 'files'])

    expect(can(handy, 'screen')).toBe(true)
    expect(can(handy, 'input')).toBe(true)
    expect(can(handy, 'files')).toBe(true)

    expect(can(handy, 'power')).toBe(false)
    expect(can(handy, 'media')).toBe(false)
    expect(can(handy, 'actions')).toBe(false)
    expect(can(handy, 'wake')).toBe(false)
    expect(can(handy, 'keys')).toBe(false)
  })

  it('ein alter Windows-Agent kann weiterhin alles außer Dateien', () => {
    expect(can(info(), 'power')).toBe(true)
    expect(can(info(), 'files')).toBe(false)
  })
})
