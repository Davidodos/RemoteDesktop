import { beforeEach, describe, expect, test, vi } from 'vitest'
import { credentialsFor } from './direct.ts'
import { setPlatform } from '../platform/index.ts'
import { webPlatform } from '../platform/web.ts'
import { createClientKey } from '../lib/clientKey.ts'
import type { ClientKey, Platform } from '../platform/index.ts'
import type { Device } from '../lib/types.ts'

/**
 * Womit sich die App bei einem gekoppelten Gerät anmeldet.
 *
 * <p>
 * **Der Befund dahinter (16.08.2026):** die Wahl hing daran, ob im Speicher der
 * App ein privater Schlüssel liegt. Seit 31h liegt er dort nicht mehr — er
 * gehört der Gegenstelle dieses Geräts (`clientkey.txt` am Handy,
 * `{app}\data\clientkey.json` am Rechner). Solange noch ein Rest aus der Zeit
 * davor im Speicher lag, fiel das nicht auf. Nach einer wirklich sauberen
 * Neuinstallation fiel jede Anfrage auf ein **leeres Bearer-Token** zurück, und
 * der Agent notierte für jede einzelne „Abgelehnt (Nicht angemeldet.)" — Bild,
 * Eingabe und selbst `/api/info`.
 * </p>
 */
describe('credentialsFor', () => {
  beforeEach(() => {
    setPlatform(webPlatform)
    window.localStorage.clear()
  })

  const paired: Device = {
    id: 'pc',
    name: 'PC',
    host: 'pc.example.ts.net',
    port: 8443,
    clientId: 'handy-1',
    canWake: true,
  }

  test('ein gekoppeltes Gerät meldet sich mit dem Schlüssel der Gegenstelle an', async () => {
    // Arrange — kein Schlüssel im App-Speicher, aber einer bei der Gegenstelle.
    // Genau die Lage nach einer sauberen Neuinstallation.
    const key = await createClientKey()

    setPlatform(withNodeKey(key))

    // Act
    const credentials = credentialsFor(paired)

    // Assert — kein Token im Voraus, sondern eine echte Anmeldung. Der leere
    // Rückfall hätte hier sofort `''` geliefert.
    expect(credentials.peek()).toBeUndefined()
  })

  test('ohne Kopplung bleibt es beim alten Token', () => {
    // Arrange — ein Eintrag aus der Zeit vor der Kopplung.
    const alt: Device = { ...paired, token: 'ge heim' }

    delete alt.clientId

    // Act & Assert
    expect(credentialsFor(alt).peek()).toBe('ge heim')
  })

  /**
   * Ein leeres Token ist kein Token. Es kam heraus, wo der Ausweis nicht
   * gefunden wurde — und sah an jeder Aufrufstelle aus wie ein gültiger.
   */
  test('ein gekoppeltes Gerät fällt nie auf ein leeres Token zurück', () => {
    expect(credentialsFor(paired).peek()).not.toBe('')
  })
})

/** Eine Plattform, deren Gegenstelle den Ausweis dieses Geräts führt. */
function withNodeKey(key: ClientKey): Platform {
  return {
    ...webPlatform,
    node: { ...webPlatform.node, key: vi.fn(() => Promise.resolve(key)) },
  }
}
