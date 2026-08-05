import { describe, expect, test } from 'vitest'
import {
  certificateUrl,
  fetchAgentCertificate,
  readable,
  TrustError,
  TRUST_PORT,
} from './certificateTrust.ts'

/** Ein Byte-Muster als Antwort — der Inhalt ist egal, sein Fingerabdruck nicht. */
function antwort(bytes: Uint8Array, status = 200): typeof fetch {
  return (async () =>
    new Response(status === 200 ? (bytes as unknown as BodyInit) : null, {
      status,
    })) as unknown as typeof fetch
}

async function abdruck(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes as unknown as BufferSource)

  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('')
}

const zertifikat = new Uint8Array([48, 130, 1, 10, 2, 1, 3])

describe('Zertifikat eines Agents holen', () => {
  test('holt es unverschlüsselt — verschlüsselt käme es nicht durch', () => {
    // Genau die Verbindung, die noch nicht zustande kommt, wäre hier die
    // Voraussetzung. Deshalb http, und deshalb der Fingerabdruck als Prüfung.
    expect(certificateUrl('192.168.178.20')).toBe(`http://192.168.178.20:${TRUST_PORT}/ca.crt`)
  })

  test('gibt es heraus, wenn der Fingerabdruck stimmt', async () => {
    const erwartet = await abdruck(zertifikat)

    const geholt = await fetchAgentCertificate('pc', erwartet, antwort(zertifikat))

    expect(geholt.fingerprint).toBe(erwartet)
    expect(geholt.base64).toBe(btoa(String.fromCharCode(...zertifikat)))
  })

  test('lehnt ein fremdes Zertifikat ab', async () => {
    // Der eine Fall, der zählt: jemand im Netz schiebt sein eigenes unter.
    const fremd = 'a'.repeat(64)

    await expect(fetchAgentCertificate('pc', fremd, antwort(zertifikat))).rejects.toThrow(TrustError)
  })

  test('ohne Fingerabdruck wird gar nicht erst geholt', async () => {
    // Ein Zertifikat ohne Vergleichswert anzunehmen wäre dasselbe wie nicht
    // zu prüfen — dann könnte man es auch lassen.
    let gefragt = false

    const zaehler = (async () => {
      gefragt = true

      return new Response(zertifikat as unknown as BodyInit)
    }) as unknown as typeof fetch

    await expect(fetchAgentCertificate('pc', '', zaehler)).rejects.toThrow(TrustError)
    await expect(fetchAgentCertificate('pc', 'kein-hex', zaehler)).rejects.toThrow(TrustError)
    expect(gefragt).toBe(false)
  })

  test('Groß- und Kleinschreibung des Fingerabdrucks ist egal', async () => {
    const erwartet = await abdruck(zertifikat)

    await expect(
      fetchAgentCertificate('pc', erwartet.toUpperCase(), antwort(zertifikat)),
    ).resolves.toBeDefined()
  })

  test('ein nicht erreichbarer Rechner sagt, woran es liegen könnte', async () => {
    const kaputt = (async () => {
      throw new TypeError('failed to fetch')
    }) as unknown as typeof fetch

    await expect(fetchAgentCertificate('pc', 'a'.repeat(64), kaputt)).rejects.toThrow(
      /Port 8442/,
    )
  })

  test('eine Fehlerseite ist kein Zertifikat', async () => {
    await expect(
      fetchAgentCertificate('pc', 'a'.repeat(64), antwort(zertifikat, 404)),
    ).rejects.toThrow(TrustError)
  })

  test('der Fingerabdruck lässt sich vorlesen', () => {
    // `a1b2c3…` vergleicht niemand von Hand, `a1:b2:c3` schon.
    expect(readable('a1b2c3')).toBe('a1:b2:c3')
  })
})
