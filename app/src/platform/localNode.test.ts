import { describe, expect, it } from 'vitest'
import { noLocalNode, usableOffer } from './localNode.ts'

/**
 * Ein Angebot zur Gegenkopplung enthält einen gültigen Kopplungscode. Was daran
 * unvollständig ist, wird verworfen statt halb benutzt — sonst scheitert die
 * Gegenkopplung später an einer Stelle, an der niemand mehr weiß, woher sie kam.
 */
describe('usableOffer', () => {
  const gut = {
    host: '192.168.178.31',
    port: 8443,
    code: '123456',
    caFingerprint: 'a'.repeat(64),
    name: 'Pixel',
  }

  it('nimmt ein vollständiges Angebot an', () => {
    expect(usableOffer(gut)).toEqual(gut)
  })

  it('kommt ohne Fingerabdruck und Namen aus', () => {
    expect(usableOffer({ host: 'pc.example.ts.net', port: 8443, code: '000000' })).toEqual({
      host: 'pc.example.ts.net',
      port: 8443,
      code: '000000',
    })
  })

  it('verwirft, was keine sechs Ziffern sind', () => {
    expect(usableOffer({ ...gut, code: '12345' })).toBeUndefined()
    expect(usableOffer({ ...gut, code: 'abcdef' })).toBeUndefined()
  })

  it('verwirft unbrauchbare Adressen und Ports', () => {
    expect(usableOffer({ ...gut, host: '   ' })).toBeUndefined()
    expect(usableOffer({ ...gut, port: 0 })).toBeUndefined()
    expect(usableOffer({ ...gut, port: 70000 })).toBeUndefined()
  })

  it('lässt einen kaputten Fingerabdruck weg, statt alles zu verwerfen', () => {
    // Ohne ihn ist die Gegenkopplung nicht unmöglich — sie braucht dann nur
    // wieder ein Auge. Deswegen das ganze Angebot wegzuwerfen wäre zu viel.
    expect(usableOffer({ ...gut, caFingerprint: 'kaputt' })).toEqual({
      host: gut.host,
      port: gut.port,
      code: gut.code,
      name: gut.name,
    })
  })

  it('verwirft, was gar kein Angebot ist', () => {
    expect(usableOffer(undefined)).toBeUndefined()
    expect(usableOffer(null)).toBeUndefined()
    expect(usableOffer('123456')).toBeUndefined()
    expect(usableOffer({})).toBeUndefined()
  })
})

describe('noLocalNode', () => {
  it('bietet nichts an und findet nichts — ohne zu scheitern', async () => {
    // Der Browser ist keine Gegenstelle. Das ist kein Fehler, sondern der
    // Normalfall: dann bleibt es bei der einen Richtung.
    await expect(noLocalNode.offer()).resolves.toBeUndefined()
    await expect(noLocalNode.take()).resolves.toBeUndefined()
  })
})
