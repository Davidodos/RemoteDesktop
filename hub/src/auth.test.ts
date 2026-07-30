import { describe, expect, it } from 'vitest'
import { extractBearer, tokensMatch } from './auth.js'

describe('extractBearer', () => {
  it('liest das Token aus dem Header', () => {
    expect(extractBearer('Bearer geheim123')).toBe('geheim123')
  })

  it('ist unabhängig von der Schreibweise des Schemas', () => {
    expect(extractBearer('bearer geheim123')).toBe('geheim123')
  })

  it.each([undefined, '', 'geheim123', 'Basic geheim123', 'Bearer'])(
    'liefert undefined bei: %s',
    (header) => {
      expect(extractBearer(header)).toBeUndefined()
    },
  )
})

describe('tokensMatch', () => {
  it('erkennt identische Tokens', () => {
    expect(tokensMatch('a'.repeat(40), 'a'.repeat(40))).toBe(true)
  })

  it('lehnt abweichende Tokens gleicher Länge ab', () => {
    expect(tokensMatch(`${'a'.repeat(39)}b`, 'a'.repeat(40))).toBe(false)
  })

  it('lehnt Tokens unterschiedlicher Länge ab, ohne zu werfen', () => {
    // timingSafeEqual wirft bei ungleicher Länge — das muss abgefangen sein,
    // sonst legt ein zu kurzes Token die Anfrage mit einem 500er lahm.
    expect(tokensMatch('kurz', 'a'.repeat(40))).toBe(false)
  })

  it('lehnt ein leeres Token ab', () => {
    expect(tokensMatch('', 'a'.repeat(40))).toBe(false)
  })
})
