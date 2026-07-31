import { describe, expect, it } from 'vitest'
import { DEFAULT_AGENT_PORT, buildPairingUri, parsePairingUri } from './pairingUri.ts'

/**
 * Der Inhalt des QR-Codes ist eine Schnittstelle zwischen zwei Programmen, die
 * getrennt aktualisiert werden: der Rechner zeigt ihn an, das Handy liest ihn.
 * Was hier durchrutscht, endet in einer Kopplung mit dem falschen Rechner oder
 * in einer Fehlermeldung ohne Aussage.
 */

describe('parsePairingUri', () => {
  it('liest Rechner, Port und Code', () => {
    // Act
    const target = parsePairingUri('remotedesktop://pair?host=arbeitsrechner&port=9443&code=123456')

    // Assert
    expect(target).toEqual({ host: 'arbeitsrechner', port: 9443, code: '123456' })
  })

  it('nimmt den üblichen Port an, wenn keiner dabeisteht', () => {
    // Der Agent hört praktisch immer auf 8443; der Code im QR soll deshalb
    // nicht daran scheitern, dass jemand den Port weglässt.
    expect(parsePairingUri('remotedesktop://pair?host=laptop&code=000001').port).toBe(
      DEFAULT_AGENT_PORT,
    )
  })

  it('verträgt Groß- und Kleinschreibung im Schema', () => {
    expect(parsePairingUri('REMOTEDESKTOP://pair?host=laptop&code=123456').host).toBe('laptop')
  })

  it('weist einen fremden QR-Code ab', () => {
    // Ein Handy scannt viele Codes. Ein WLAN-Code darf hier nicht als halbe
    // Kopplung durchgehen.
    expect(() => parsePairingUri('https://example.invalid/')).toThrow(/RemoteDesktop/)
    expect(() => parsePairingUri('WIFI:S=Netz;T=WPA;P=geheim;;')).toThrow(/RemoteDesktop/)
  })

  it('weist einen Code der falschen Länge ab', () => {
    expect(() => parsePairingUri('remotedesktop://pair?host=pc&code=12345')).toThrow(/sechs/)
    expect(() => parsePairingUri('remotedesktop://pair?host=pc&code=1234567')).toThrow(/sechs/)
  })

  it('weist Buchstaben im Code ab', () => {
    // Der Agent erzeugt sechs Ziffern. Alles andere ist ein anderer Code.
    expect(() => parsePairingUri('remotedesktop://pair?host=pc&code=12a456')).toThrow(/sechs/)
  })

  it('weist einen leeren Rechnernamen ab', () => {
    expect(() => parsePairingUri('remotedesktop://pair?code=123456')).toThrow(/Rechnername/)
    expect(() => parsePairingUri('remotedesktop://pair?host=&code=123456')).toThrow(/Rechnername/)
  })

  it('weist einen unmöglichen Port ab', () => {
    expect(() => parsePairingUri('remotedesktop://pair?host=pc&port=0&code=123456')).toThrow(/Port/)
    expect(() => parsePairingUri('remotedesktop://pair?host=pc&port=99999&code=123456')).toThrow(
      /Port/,
    )
    expect(() => parsePairingUri('remotedesktop://pair?host=pc&port=acht&code=123456')).toThrow(
      /Port/,
    )
  })

  it('weist eine andere Aktion ab', () => {
    // Später könnte es `remotedesktop://wake` geben. Bis dahin ist alles außer
    // `pair` ein Irrtum und kein stillschweigend akzeptierter Sonderfall.
    expect(() => parsePairingUri('remotedesktop://wake?host=pc&code=123456')).toThrow(/Kopplung/)
  })

  it('lässt Leerzeichen um den gescannten Text zu', () => {
    // Manche Scanner hängen ein Zeilenende an.
    expect(parsePairingUri('  remotedesktop://pair?host=pc&code=123456\n').host).toBe('pc')
  })
})

describe('buildPairingUri', () => {
  it('erzeugt, was parsePairingUri wieder versteht', () => {
    // Arrange
    const target = { host: 'arbeitsrechner', port: 8443, code: '654321' }

    // Act
    const round = parsePairingUri(buildPairingUri(target))

    // Assert — beide Seiten des QR-Codes stehen in einer Datei, damit sie nicht
    // auseinanderlaufen können.
    expect(round).toEqual(target)
  })

  it('kodiert Sonderzeichen im Rechnernamen', () => {
    expect(parsePairingUri(buildPairingUri({ host: 'a b', port: 8443, code: '111111' })).host).toBe(
      'a b',
    )
  })
})
