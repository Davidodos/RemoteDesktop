import { describe, expect, it } from 'vitest'
import { touchInputFor, type Anschlag } from './touchTyping.ts'

function press(key: string, held: Partial<Omit<Anschlag, 'key'>> = {}): Anschlag {
  return { key, ctrlKey: false, altKey: false, metaKey: false, ...held }
}

/**
 * Der Fall, der das Ganze ausgelöst hat: am Handy stand bei jedem Buchstaben
 * „„e" gibt es auf einem Handy nicht", obwohl es die Taste offensichtlich gibt.
 * Sie gibt es — nur ist sie dort kein Anschlag, sondern Text.
 */
describe('touchInputFor', () => {
  it('ein Buchstabe ist Text und kein Tastendruck', () => {
    expect(touchInputFor(press('e'))).toEqual({ kind: 'text', text: 'e' })
  })

  it('die Leertaste auch', () => {
    expect(touchInputFor(press(' '))).toEqual({ kind: 'text', text: ' ' })
  })

  it('Umlaute und Zeichen mit Alt Gr kommen fertig aus dem Browser', () => {
    expect(touchInputFor(press('ä'))).toEqual({ kind: 'text', text: 'ä' })
    expect(touchInputFor(press('@', { altKey: true }))).toEqual({ kind: 'text', text: '@' })
  })

  it('die wenigen Tasten mit Entsprechung gehen als Taste hinaus', () => {
    expect(touchInputFor(press('Enter'))).toEqual({ kind: 'key', key: 'enter' })
    expect(touchInputFor(press('Backspace'))).toEqual({ kind: 'key', key: 'backspace' })
    expect(touchInputFor(press('Escape'))).toEqual({ kind: 'key', key: 'escape' })
  })

  /**
   * Verschluckt und nicht geschickt: dass ein Handy kein F5 hat, ist klar, und
   * eine Meldung darüber verdeckt nur die eine, auf die es ankommt.
   */
  it('was drüben nichts bedeutet, geht gar nicht erst hinaus', () => {
    expect(touchInputFor(press('F5'))).toBeUndefined()
    expect(touchInputFor(press('ArrowLeft'))).toBeUndefined()
    expect(touchInputFor(press('Shift'))).toBeUndefined()
  })

  it('eine Tastenkombination ist kein Text', () => {
    expect(touchInputFor(press('c', { ctrlKey: true }))).toBeUndefined()
  })
})
