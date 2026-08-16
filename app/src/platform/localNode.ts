/**
 * Das eigene Gerät als Gegenstelle.
 *
 * <p>
 * **Eine Kopplung geht immer in beide Richtungen.** Wer koppelt, richtet damit
 * nicht nur eine Richtung ein: beide Seiten tauschen beim Koppeln aus, was sie
 * voneinander brauchen — in einem Aufruf, ohne zweiten Weg. Was danach zu tun
 * ist, erledigt jede Seite bei sich zu Hause, und dafür steht diese
 * Schnittstelle.
 * </p>
 *
 * <p>
 * **Der Vorgänger machte es andersherum**, und das war der Fehler: er reichte
 * einen Kopplungscode weiter, den die Gegenseite binnen fünf Minuten einlösen
 * musste. Damit hing die Gegenrichtung an einem laufenden Server, einem offenen
 * Fenster und einer Uhr — wer beim Koppeln die Freigabe nicht eingeschaltet
 * hatte, bekam sie nie.
 * </p>
 *
 * <p>
 * Alle vier Wege laufen nativ und nicht über HTTP: der eigene Agent weist sich
 * mit einem selbst ausgestellten Zertifikat aus, und die Seite müsste ihm erst
 * vertrauen, um ihn überhaupt fragen zu können. Am Handy führt der Weg über das
 * Plugin, im Fenster über die Brücke zur Wirtsanwendung.
 * </p>
 */
export interface LocalNode {
  /**
   * Ob es hier jemanden gibt, der die Liste der zugelassenen Geräte führt.
   *
   * <p>
   * **Nicht dasselbe wie ein vorhandener Steckbrief**, und die Verwechslung war
   * ein Fehler: die Gegenrichtung hing an {@link profile}, also an einer
   * Adresse. Ein Handy, das gerade in keinem Netz hängt, hat keine — und trug
   * den Schlüssel der Gegenseite deshalb **nicht** ein. Danach stand das Gerät
   * in der Liste und antwortete auf jede Anfrage mit „kenne ich nicht", ohne
   * dass irgendwo stand, warum. Eine Adresse ist eine Auskunft über das Netz von
   * jetzt; ein Eintrag in einer Datei gilt, sobald der Server startet.
   * </p>
   *
   * <p>
   * **Und warum es eine Frage bleibt und keine Eigenschaft:** am Handy führt die
   * Liste der Prozess selbst, hier ist die Antwort immer ja. Im Fenster führt sie
   * der Agent nebenan, und ob der läuft, ist eine Entscheidung des Nutzers — wer
   * nur andere Geräte steuern will, braucht ihn nicht. Ein „nein" ist dort keine
   * Störung, sondern der Normalfall, und darf deshalb keine Meldung auslösen.
   * </p>
   */
  ready(): Promise<boolean>

  /**
   * Der eigene Steckbrief — er geht mit, wenn dieses Gerät ein anderes koppelt.
   *
   * `undefined` heißt: dieses Gerät ist kein mögliches Ziel (kein Agent, keine
   * Adresse). Dann bleibt es bei der einen Richtung, und das ist kein Fehler.
   * Ob der Host gerade *läuft*, spielt ausdrücklich keine Rolle: der Steckbrief
   * beschreibt, wie dieses Gerät erreichbar wäre, und ein Eintrag in einer Datei
   * wirkt, sobald der Server startet.
   */
  profile(): Promise<DeviceProfile | undefined>

   /**
   * Die Steckbriefe, die beim Koppeln hier abgegeben wurden.
   *
   * Lesen leert den Eingang **nicht**. Es tat es einmal, und das war falsch:
   * ging danach irgendetwas schief, war der Steckbrief endgültig weg, und am
   * Bildschirm stand „noch kein Gerät gekoppelt" — ohne zweiten Versuch.
   */
  peers(): Promise<DeviceProfile[]>

  /**
   * Vergisst, was in der Liste steht. Erst jetzt: sonst käme ein Gerät, das
   * jemand aus seiner Liste entfernt hat, von allein zurück.
   */
  forget(ids: string[]): Promise<void>

  /**
   * Die Gegenrichtung eintragen: die Oberfläche der Gegenseite darf dieses
   * Gerät steuern. Ohne Code — der Schlüssel kam über eine Verbindung, an deren
   * Anfang genau ein Code stand.
   */
  grant(publicKey: string, label: string): Promise<void>

  /**
   * Der Ausweis dieses Geräts — das Schlüsselpaar, mit dem sich seine
   * Oberfläche bei fremden Geräten anmeldet.
   *
   * <p>
   * **Warum ihn die Gegenstelle führt und nicht die App:** bis zum 16.08.2026
   * lag er im Speicher der Weboberfläche, und die Gegenstelle kannte ihn nur,
   * weil die App ihn beim Start hinterlegte. Damit hing der Ausweis am
   * Lebenslauf einer Oberfläche — wer im Fenster nie die Fernsteuerung
   * anzeigte, hinterlegte nie etwas, und die Gegenseite bekam beim Koppeln ein
   * leeres `clientKey`. Jetzt liegt er dort, wo er hingehört: bei den übrigen
   * Schlüsseln des Geräts, und beide lesen dieselbe Stelle.
   * </p>
   *
   * <p>
   * `undefined` heißt: diese Umgebung führt keinen — im Browser ist das der
   * Normalfall. Dann legt die App selbst einen an und bewahrt ihn in ihrem
   * eigenen Schlüsselspeicher auf.
   * </p>
   */
  key(): Promise<ClientKey | undefined>
}

/**
 * Ein Schlüsselpaar, wie es die Gegenstelle herausgibt: ECDSA P-256, der
 * öffentliche Teil als Base64 im SPKI-Format, der private als Base64 im
 * PKCS-8-Format. Genau so nimmt es die WebCrypto-API des Browsers an.
 *
 * Schwesterfassungen: `setup/ClientKeyFile.cs` und `host/LocalClientKey.kt`.
 */
export interface ClientKey {
  publicKey: string
  privateKey: string
}

/** Was ein Gerät ist. Gegenstück zu `setup/DevicePlatform.cs`. */
export type DevicePlatform = 'windows' | 'android'

/**
 * Der Steckbrief eines Geräts: alles, was die Gegenseite braucht, um es später
 * von sich aus zu erreichen. Schwesterfassungen: `agent/Auth/DeviceProfile.cs`
 * und `host/DeviceProfile.kt`.
 */
export interface DeviceProfile {
  /**
   * Woran der Eingang diesen Eintrag wiedererkennt. Nur bei abgeholten
   * Steckbriefen gesetzt — der eigene braucht keine.
   */
  id?: string
  host: string
  port: number
  /** Wie das Gerät heißt. Für die Anzeige in der Geräteliste. */
  name: string
  /**
   * Womit es sich ausweist — `undefined` bei einem Zertifikat von Tailscale,
   * dem ohnehin jeder glaubt. Dann gibt es nichts zu bestätigen.
   */
  caFingerprint?: string
  /**
   * Der Fingerabdruck seines Agent-Schlüssels. Er ist die Kennung des Geräts in
   * der Liste: er bleibt gleich, auch wenn Name oder Adresse wechseln.
   */
  agentFingerprint?: string
  /**
   * Der öffentliche Schlüssel, mit dem sich die **Oberfläche** dieses Geräts
   * anmeldet. Er gehört in die `clients.json` der Gegenseite — das ist die
   * ganze Gegenrichtung, in einem Feld.
   */
  clientKey?: string
  /**
   * Ob dahinter ein Rechner oder ein Handy steckt — für das Symbol in der
   * Geräteliste. Er steht im Steckbrief und nicht nur in `/api/info`, damit
   * die Liste ihn auch dann zeigt, wenn das Gerät gerade aus ist.
   */
  platform?: DevicePlatform
}

/** Für Umgebungen, die selbst keine Gegenstelle sind — der Browser vor allem. */
export const noLocalNode: LocalNode = {
  ready: (): Promise<boolean> => Promise.resolve(false),
  profile: (): Promise<DeviceProfile | undefined> => Promise.resolve(undefined),
  peers: (): Promise<DeviceProfile[]> => Promise.resolve([]),
  forget: (): Promise<void> => Promise.resolve(),
  grant: (): Promise<void> => Promise.resolve(),
  key: (): Promise<ClientKey | undefined> => Promise.resolve(undefined),
}

/**
 * Was von einem Steckbrief übrig bleibt, wenn man ihn ernst nimmt.
 *
 * Dieselben Regeln wie auf beiden Agent-Seiten: eine Adresse, ein brauchbarer
 * Port. Unvollständiges wird verworfen statt halb benutzt — ein Steckbrief, an
 * dem etwas fehlt, führte später zu einem Fehlschlag an einer Stelle, an der
 * niemand mehr weiß, woher er kam.
 */
export function usableProfile(value: unknown): DeviceProfile | undefined {
  if (typeof value !== 'object' || value === null) {
    return undefined
  }

  const { id, host, port, name, caFingerprint, agentFingerprint, clientKey, platform } =
    value as Record<string, unknown>

  if (typeof host !== 'string' || host.trim().length === 0 || host.length > 255) {
    return undefined
  }

  if (typeof port !== 'number' || !Number.isInteger(port) || port < 1 || port > 65535) {
    return undefined
  }

  const address = host.trim()

  return {
    ...(typeof id === 'string' && id.length > 0 ? { id } : {}),
    host: address,
    port,
    // Ein leerer Eintrag in der Liste ließe sich später niemandem zuordnen.
    name: typeof name === 'string' && name.trim().length > 0 ? name.trim() : address,
    ...hex('caFingerprint', caFingerprint, 64),
    ...hex('agentFingerprint', agentFingerprint, 16),
    ...(typeof clientKey === 'string' && clientKey.trim().length > 0
      ? { clientKey: clientKey.trim() }
      : {}),
    // Ein unbekannter Wert zählt als keiner: die Liste zeigt dann kein Symbol,
    // und das ist besser als ein falsches.
    ...(platform === 'windows' || platform === 'android' ? { platform } : {}),
  }
}

function hex(field: string, value: unknown, length: number): Record<string, string> {
  if (typeof value !== 'string') {
    return {}
  }

  const trimmed = value.trim().toLowerCase()

  return trimmed.length === length && /^[0-9a-f]+$/.test(trimmed) ? { [field]: trimmed } : {}
}
