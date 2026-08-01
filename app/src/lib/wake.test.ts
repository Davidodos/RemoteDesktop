import { describe, expect, test } from 'vitest'
import { explainMissingCandidate, findWakeCandidate, rememberSite, siteChanged } from './wake.ts'
import type { AgentInfo, Device } from './types.ts'

const ZUHAUSE = 'kennung-zuhause'
const ELTERN = 'kennung-eltern'

function geraet(id: string, extra: Partial<Device> = {}): Device {
  return {
    id,
    name: id.toUpperCase(),
    host: `${id}.example.ts.net`,
    port: 8443,
    clientId: 'handy-1',
    canWake: false,
    ...extra,
  }
}

/** Der schlafende PC zu Hause — MAC und Standort sind von früher bekannt. */
const PC = geraet('pc', { mac: 'aa:bb:cc:dd:ee:ff', siteId: ZUHAUSE })

describe('findWakeCandidate', () => {
  test('ein Waker mit derselben Kennung wird gefunden', () => {
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(findWakeCandidate(PC, [nas], new Set(['nas']))).toEqual({ device: nas, reason: 'waker' })
  })

  test('ein Waker aus einem fremden Netz wird nicht gefunden', () => {
    // Der Kern der Sache: ein Magic Packet kommt über keinen Router. Ein Waker
    // bei den Eltern nützt dem PC zu Hause nichts.
    const pi = geraet('pi', { canWake: true, waker: true, siteId: ELTERN })

    expect(findWakeCandidate(PC, [pi], new Set(['pi']))).toBeUndefined()
  })

  test('ohne Kandidat gibt es keinen Knopf und keinen Fehler', () => {
    expect(findWakeCandidate(PC, [], new Set())).toBeUndefined()
  })

  test('ein wacher Agent am selben Ort tut es auch', () => {
    const laptop = geraet('laptop', { canWake: true, siteId: ZUHAUSE })

    expect(findWakeCandidate(PC, [laptop], new Set(['laptop']))).toEqual({
      device: laptop,
      reason: 'agent',
    })
  })

  test('der Waker geht dem Agent vor', () => {
    // Er läuft durch; der Laptop ist zufällig gerade an.
    const laptop = geraet('laptop', { canWake: true, siteId: ZUHAUSE })
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(findWakeCandidate(PC, [laptop, nas], new Set(['laptop', 'nas']))?.device.id).toBe('nas')
  })

  test('ein Knoten, der selbst nicht antwortet, weckt niemanden', () => {
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(findWakeCandidate(PC, [nas], new Set())).toBeUndefined()
  })

  test('ein Gerät weckt sich nicht selbst', () => {
    const selbst = { ...PC, canWake: true }

    expect(findWakeCandidate(selbst, [selbst], new Set(['pc']))).toBeUndefined()
  })

  test('ohne bekannte MAC lässt sich niemand wecken', () => {
    const ohneMac = geraet('pc', { siteId: ZUHAUSE })
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(findWakeCandidate(ohneMac, [nas], new Set(['nas']))).toBeUndefined()
  })

  test('ohne bekannten Standort lässt sich niemand wecken', () => {
    // Sonst würde geraten, und ein Waker am falschen Ort sendet ins Leere.
    const ohneStandort = geraet('pc', { mac: 'aa:bb:cc:dd:ee:ff' })
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(findWakeCandidate(ohneStandort, [nas], new Set(['nas']))).toBeUndefined()
  })

  test('ein Knoten ohne Weckfähigkeit zählt nicht', () => {
    const handy = geraet('anderes', { canWake: false, siteId: ZUHAUSE })

    expect(findWakeCandidate(PC, [handy], new Set(['anderes']))).toBeUndefined()
  })
})

describe('explainMissingCandidate', () => {
  test('ohne bekannten Standort steht dort, was zu tun ist', () => {
    expect(explainMissingCandidate(geraet('pc'))).toContain('Einmal verbinden')
  })

  test('mit bekanntem Standort erklärt es das Netz', () => {
    expect(explainMissingCandidate(PC)).toContain('über keinen Router')
  })
})

describe('rememberSite', () => {
  const info = (extra: Partial<AgentInfo> = {}): AgentInfo => ({
    hostname: 'PC',
    monitors: [],
    ...extra,
  })

  test('übernimmt Standort und MAC aus der Auskunft', () => {
    const aktualisiert = rememberSite(geraet('pc'), info({ mac: 'aa:bb', siteId: ZUHAUSE, canWake: true }))

    expect(aktualisiert).toMatchObject({ mac: 'aa:bb', siteId: ZUHAUSE, canWake: true })
  })

  test('ein Ortswechsel wird übernommen', () => {
    // Steht der PC beim nächsten Mal woanders, wird ab da automatisch der
    // Waker dort gefragt.
    const aktualisiert = rememberSite(PC, info({ siteId: ELTERN }))

    expect(aktualisiert.siteId).toBe(ELTERN)
  })

  test('ein älterer Agent ohne die Felder löscht nichts', () => {
    // Eine Kennung von gestern ist besser als keine — sonst verlöre man den
    // Weckknopf, sobald ein Agent zurückgerollt wird.
    expect(rememberSite(PC, info())).toMatchObject({ mac: PC.mac, siteId: ZUHAUSE })
  })
})

describe('siteChanged', () => {
  test('erkennt eine neue Kennung', () => {
    expect(siteChanged(PC, { ...PC, siteId: ELTERN })).toBe(true)
  })

  test('meldet nichts, wenn alles gleich blieb', () => {
    // Sonst schriebe die App bei jedem Verbinden in den Speicher.
    expect(siteChanged(PC, { ...PC })).toBe(false)
  })
})
