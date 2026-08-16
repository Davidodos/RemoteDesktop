import { describe, expect, it } from 'vitest'
import { noLocalNode, usableProfile } from './localNode.ts'

/**
 * Ein Steckbrief kommt ungeprüft aus einem fremden Rumpf — was hier durchgeht,
 * steht danach in einer Geräteliste. Unvollständiges wird verworfen statt halb
 * benutzt: sonst scheitert die Verbindung später an einer Stelle, an der niemand
 * mehr weiß, woher der Eintrag kam.
 */
describe('usableProfile', () => {
  const gut = {
    host: '192.168.178.31',
    port: 8443,
    name: 'Pixel',
    caFingerprint: 'a'.repeat(64),
    agentFingerprint: 'b'.repeat(16),
  }

  it('nimmt einen vollständigen Steckbrief an', () => {
    expect(usableProfile(gut)).toEqual(gut)
  })

  it('nimmt den Ausweis der Gegenseite mit — das ist die ganze Gegenrichtung', () => {
    expect(usableProfile({ ...gut, clientKey: ' MFk+ ' })).toEqual({
      ...gut,
      clientKey: 'MFk+',
    })
  })

  it('kommt ohne Fingerabdrücke aus', () => {
    expect(usableProfile({ host: 'pc.example.ts.net', port: 8443, name: 'PC' })).toEqual({
      host: 'pc.example.ts.net',
      port: 8443,
      name: 'PC',
    })
  })

  it('setzt die Adresse ein, wo der Name fehlt', () => {
    // Ein leerer Eintrag in der Liste ließe sich später niemandem zuordnen.
    expect(usableProfile({ host: 'pc.example', port: 8443, name: '  ' })?.name).toBe(
      'pc.example',
    )
  })

  it('verwirft unbrauchbare Adressen und Ports', () => {
    expect(usableProfile({ ...gut, host: '   ' })).toBeUndefined()
    expect(usableProfile({ ...gut, port: 0 })).toBeUndefined()
    expect(usableProfile({ ...gut, port: 70000 })).toBeUndefined()
    expect(usableProfile({ ...gut, port: '8443' })).toBeUndefined()
  })

  it('lässt einen kaputten Fingerabdruck weg, statt alles zu verwerfen', () => {
    // Ohne ihn ist das Gerät nicht unerreichbar — es braucht dann nur wieder
    // ein Auge für das Zertifikat. Deswegen den ganzen Steckbrief wegzuwerfen
    // wäre zu viel.
    expect(usableProfile({ ...gut, caFingerprint: 'kaputt' })).toEqual({
      host: gut.host,
      port: gut.port,
      name: gut.name,
      agentFingerprint: gut.agentFingerprint,
    })
  })

  it('verwirft, was gar kein Steckbrief ist', () => {
    expect(usableProfile(undefined)).toBeUndefined()
    expect(usableProfile(null)).toBeUndefined()
    expect(usableProfile('192.168.178.31')).toBeUndefined()
    expect(usableProfile({})).toBeUndefined()
  })
})

describe('noLocalNode', () => {
  it('beschreibt nichts und findet nichts — ohne zu scheitern', async () => {
    // Der Browser ist keine Gegenstelle. Das ist kein Fehler, sondern der
    // Normalfall: dann bleibt es bei der einen Richtung.
    await expect(noLocalNode.profile()).resolves.toBeUndefined()
    await expect(noLocalNode.peers()).resolves.toEqual([])
    await expect(noLocalNode.grant('egal', 'egal')).resolves.toBeUndefined()
    await expect(noLocalNode.key()).resolves.toBeUndefined()
  })
})
