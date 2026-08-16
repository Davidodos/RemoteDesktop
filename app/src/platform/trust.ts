/**
 * Einem selbst ausgestellten Zertifikat vertrauen — die Schnittstelle dafür.
 *
 * Eigene Datei und nicht in der Sammelstelle: die holen sich alle drei
 * Umsetzungen, und die Sammelstelle holt sich umgekehrt die Vorgabe-Plattform
 * von `web.ts`. In einem solchen Kreis entsteht das zuerst Gefragte zuletzt —
 * die Felder der Vorgabe-Plattform waren deshalb leer.
 */

/**
 * Einer Zertifizierungsstelle vertrauen, die sich ein Agent selbst ausgestellt
 * hat.
 *
 * Das kann keine Weboberfläche selbst — es ist eine Angelegenheit des Geräts,
 * nicht der Seite. Android bringt dafür einen Systemdialog mit, im Browser
 * bleibt nur die Warnung, die man einmal wegklickt. Deshalb steht hier eine
 * Schnittstelle und keine Umsetzung: die Oberfläche fragt vorher, ob es geht,
 * und bietet den Knopf sonst gar nicht erst an.
 */
export interface TrustService {
  /** Ob dieses Gerät überhaupt einen Weg dafür hat. */
  readonly available: boolean

  /**
   * Holt die Zertifizierungsstelle der Gegenstelle — **nativ**, nicht aus der
   * Seite heraus.
   *
   * <p>
   * **Der Befund dahinter:** die App lief unter `https` (Capacitor auf
   * `https://localhost`, das Fenster auf einem virtuellen Host), und der Abruf
   * ging an `http://<adresse>:8442/ca.crt`. Chromium verwirft das als aktiven
   * Mixed Content, bevor irgendetwas über das Netz geht — die Ausnahme sieht
   * genauso aus wie ein Rechner, der nicht antwortet. Am Gerät stand deshalb
   * „<IP> antwortet nicht", während der Agent lief und antwortete.
   * </p>
   *
   * <p>
   * Nativ gibt es diese Sperre nicht: dort ist es eine gewöhnliche
   * HTTP-Anfrage. `undefined` heißt, dass die Umgebung das nicht kann — dann
   * bleibt der Abruf aus der Seite heraus, der im gewöhnlichen Browser auch
   * funktioniert.
   * </p>
   */
  readonly fetchAuthority?: (host: string, port: number) => Promise<TrustedAuthority>

  /**
   * Übergibt das geprüfte Zertifikat dem System. Was danach passiert, gehört
   * dem System — es fragt selbst nach und kann abgelehnt werden.
   *
   * @param certificateBase64 Das Zertifikat.
   * @param fingerprint Der erwartete Fingerabdruck aus der Kopplung. Er geht
   *   mit, obwohl `lib/certificateTrust.ts` bereits verglichen hat: die
   *   Weboberfläche ist austauschbar, und eine Prüfung, die nur an einer Stelle
   *   steht, ist eine, die beim nächsten Umbau verschwindet.
   */
  install(certificateBase64: string, fingerprint: string): Promise<TrustOutcome>

  /**
   * Einer Stelle nicht mehr glauben — beim Entfernen eines Geräts.
   *
   * `undefined` heißt: diese Umgebung kann es nicht wieder zurücknehmen.
   * Android reicht das Zertifikat an das System weiter, und was das System
   * damit macht, gehört ihm; herausnehmen lässt es sich nur dort. Das gehört
   * dann auf den Bildschirm, statt still zu scheitern.
   */
  readonly forget?: (fingerprint: string) => Promise<void>
}

/**
 * Wie weit das System gekommen ist.
 *
 * `dialog` heißt: Android hat seinen Bestätigungsdialog gezeigt, danach ist es
 * erledigt. `settings` heißt: es lässt das seit Android 11 nicht mehr aus einer
 * App heraus zu — die Datei liegt jetzt in den Downloads, und die
 * Systemeinstellungen sind offen. Der Unterschied gehört auf den Bildschirm:
 * beim zweiten Fall passiert sonst scheinbar nichts.
 */
export type TrustOutcome = 'dialog' | 'settings'

/** Ein geholtes Zertifikat samt seinem Fingerabdruck. */
export interface TrustedAuthority {
  /** Das Zertifikat als Base64 (DER). */
  base64: string
  /** `sha256` darüber, kleingeschrieben und ohne Trennzeichen. */
  fingerprint: string
}

/** Für Umgebungen, die es nicht können — der Browser vor allem. */
export const noTrust: TrustService = {
  available: false,
  install: (): Promise<TrustOutcome> =>
    Promise.reject(
      new Error(
        'Auf diesem Gerät lässt sich das Zertifikat nicht aus der App heraus bestätigen.',
      ),
    ),
}
