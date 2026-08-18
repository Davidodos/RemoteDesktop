import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { NOTICE_LIFETIME_MS, Notices } from './notice.ts'

/**
 * Eine Meldung, die stehen bleibt, bis jemand sie wegtippt, klingt nach
 * Sorgfalt und ist das Gegenteil: sie beschreibt einen Augenblick, und wenn der
 * vorbei ist, behauptet sie ein Problem, das es nicht mehr gibt.
 */
describe('Notices', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  /** Sammelt, was die Ansicht zu sehen bekäme. */
  function beobachtet(): { stände: (string | undefined)[]; notices: Notices } {
    const stände: (string | undefined)[] = []

    return { stände, notices: new Notices((message) => stände.push(message)) }
  }

  it('meldet, was gemeldet wurde', () => {
    const { notices, stände } = beobachtet()

    notices.report('Kein Bild.')

    expect(notices.message).toBe('Kein Bild.')
    expect(stände).toEqual(['Kein Bild.'])
  })

  it('geht nach der Frist von allein', () => {
    const { notices, stände } = beobachtet()

    notices.report('Kein Bild.')
    vi.advanceTimersByTime(NOTICE_LIFETIME_MS + 1)

    expect(notices.message).toBeUndefined()
    expect(stände).toEqual(['Kein Bild.', undefined])
  })

  /**
   * Sonst verschwände die neueste zuerst — die alte hat ihre Frist ja schon
   * halb aufgebraucht, und ihr Zeitgeber weiß nichts von der Ablösung.
   */
  it('eine zweite Meldung setzt die Frist zurück', () => {
    const { notices } = beobachtet()

    notices.report('Erste.')
    vi.advanceTimersByTime(NOTICE_LIFETIME_MS - 100)
    notices.report('Zweite.')
    vi.advanceTimersByTime(200)

    expect(notices.message).toBe('Zweite.')
  })

  /** Der Weg für „das beschreibt nichts mehr" — Verbindung steht, Gerät weg. */
  it('lässt sich vorzeitig auflösen', () => {
    const { notices } = beobachtet()

    notices.report('Kein Bild.')
    notices.clear()

    expect(notices.message).toBeUndefined()
  })

  /**
   * Ein Zeitgeber, der nach dem Auflösen noch feuert, löschte eine Meldung, die
   * längst eine andere ist.
   */
  it('ein aufgelöster Zeitgeber feuert nicht nach', () => {
    const { notices } = beobachtet()

    notices.report('Erste.')
    notices.clear()
    notices.report('Zweite.')
    vi.advanceTimersByTime(NOTICE_LIFETIME_MS - 100)

    expect(notices.message).toBe('Zweite.')
  })

  /**
   * Auflösen ohne Meldung sagt der Ansicht nichts. Sonst zeichnete jeder
   * Verbindungswechsel neu, obwohl sich nichts geändert hat.
   */
  it('auflösen ohne Meldung meldet nichts', () => {
    const { notices, stände } = beobachtet()

    notices.clear()

    expect(stände).toEqual([])
  })
})
