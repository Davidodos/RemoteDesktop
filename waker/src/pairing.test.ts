import { generateKeyPairSync, sign } from 'node:crypto'
import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, test } from 'vitest'
import { ClientStore } from './clients.js'
import { CHALLENGE_LIFETIME_MS, CODE_LIFETIME_MS, PairingService } from './pairing.js'

/**
 * Ein Client mit eigenem Schlüsselpaar — im Test das, was im Betrieb das Handy
 * ist. Unterschrieben wird im Format der WebCrypto-API des Browsers (r und s
 * hintereinander), damit der Test dieselbe Prüfung durchläuft wie die App.
 */
function testClient(): { publicKey: string; sign: (nonce: string) => string } {
  const pair = generateKeyPairSync('ec', { namedCurve: 'prime256v1' })

  return {
    publicKey: pair.publicKey.export({ format: 'der', type: 'spki' }).toString('base64'),
    sign: (nonce) =>
      sign('sha256', Buffer.from(nonce, 'base64'), {
        key: pair.privateKey,
        dsaEncoding: 'ieee-p1363',
      }).toString('base64'),
  }
}

describe('PairingService', () => {
  let directory: string
  let clock: number
  let pairing: PairingService

  beforeEach(() => {
    directory = mkdtempSync(join(tmpdir(), 'waker-'))
    clock = Date.parse('2026-08-01T12:00:00Z')
    pairing = new PairingService(new ClientStore(join(directory, 'clients.json')), () => clock)
  })

  afterEach(() => {
    rmSync(directory, { recursive: true, force: true })
  })

  test('mit richtigem Code wird gekoppelt', () => {
    const client = testClient()
    const result = pairing.pair(pairing.issueCode(), 'Handy', client.publicKey)

    expect(result.outcome).toBe('ok')
    expect(result.client?.label).toBe('Handy')
  })

  test('ein Code funktioniert kein zweites Mal', () => {
    const code = pairing.issueCode()

    expect(pairing.pair(code, 'Handy', testClient().publicKey).outcome).toBe('ok')
    expect(pairing.pair(code, 'Zweites', testClient().publicKey).outcome).toBe('bad-code')
  })

  test('nach fünf Minuten gilt der Code nicht mehr', () => {
    const code = pairing.issueCode()
    clock += CODE_LIFETIME_MS + 1

    expect(pairing.pair(code, 'Handy', testClient().publicKey).outcome).toBe('bad-code')
  })

  test('ein falscher Schlüssel wird abgelehnt', () => {
    expect(pairing.pair(pairing.issueCode(), 'Handy', 'kein Schlüssel').outcome).toBe('bad-key')
  })

  test('ein Name muss dabei sein', () => {
    expect(pairing.pair(pairing.issueCode(), '   ', testClient().publicKey).outcome).toBe(
      'bad-label',
    )
  })

  test('eine richtige Unterschrift öffnet eine Sitzung', () => {
    const client = testClient()
    const { client: paired } = pairing.pair(pairing.issueCode(), 'Handy', client.publicKey)

    const nonce = pairing.challenge(paired!.id)
    const result = pairing.openSession(paired!.id, nonce!, client.sign(nonce!))

    expect(result.outcome).toBe('ok')
    expect(pairing.resolveSession(result.token)).toBe(paired!.id)
  })

  test('eine manipulierte Challenge fällt durch', () => {
    const client = testClient()
    const { client: paired } = pairing.pair(pairing.issueCode(), 'Handy', client.publicKey)

    const nonce = pairing.challenge(paired!.id)
    const fremd = Buffer.from('etwas ganz anderes hier drin!!!!').toString('base64')

    expect(pairing.openSession(paired!.id, nonce!, client.sign(fremd)).outcome).toBe('bad-signature')
  })

  test('eine Challenge gilt nur einmal', () => {
    const client = testClient()
    const { client: paired } = pairing.pair(pairing.issueCode(), 'Handy', client.publicKey)

    const nonce = pairing.challenge(paired!.id)
    const signature = client.sign(nonce!)

    expect(pairing.openSession(paired!.id, nonce!, signature).outcome).toBe('ok')
    expect(pairing.openSession(paired!.id, nonce!, signature).outcome).toBe('bad-challenge')
  })

  test('eine abgelaufene Challenge fällt durch', () => {
    const client = testClient()
    const { client: paired } = pairing.pair(pairing.issueCode(), 'Handy', client.publicKey)

    const nonce = pairing.challenge(paired!.id)
    clock += CHALLENGE_LIFETIME_MS + 1

    expect(pairing.openSession(paired!.id, nonce!, client.sign(nonce!)).outcome).toBe(
      'bad-challenge',
    )
  })

  test('ein unbekannter Client bekommt keine Challenge', () => {
    expect(pairing.challenge('gibt-es-nicht')).toBeUndefined()
  })

  test('ein widerrufener Client kommt nicht mehr herein', () => {
    const client = testClient()
    const { client: paired } = pairing.pair(pairing.issueCode(), 'Handy', client.publicKey)

    const nonce = pairing.challenge(paired!.id)
    const { token } = pairing.openSession(paired!.id, nonce!, client.sign(nonce!))

    expect(pairing.revoke(paired!.id)).toBe(true)

    // Der Widerruf wirft ihn auch aus der laufenden Sitzung — sonst verschöbe
    // sich die Wirkung um bis zu zwölf Stunden.
    expect(pairing.resolveSession(token)).toBeUndefined()
    expect(pairing.challenge(paired!.id)).toBeUndefined()
  })

  test('ein erfundenes Token öffnet nichts', () => {
    expect(pairing.resolveSession('erfunden')).toBeUndefined()
    expect(pairing.resolveSession(undefined)).toBeUndefined()
  })

  test('in der Datei steht kein Klartext-Token', () => {
    const client = testClient()
    const store = new ClientStore(join(directory, 'clients.json'))
    const service = new PairingService(store, () => clock)

    const { client: paired } = service.pair(service.issueCode(), 'Handy', client.publicKey)
    const nonce = service.challenge(paired!.id)
    const { token } = service.openSession(paired!.id, nonce!, client.sign(nonce!))

    const gespeichert = JSON.stringify(new ClientStore(join(directory, 'clients.json')).list())

    expect(gespeichert).toContain(client.publicKey)
    expect(gespeichert).not.toContain(token)
  })
})
