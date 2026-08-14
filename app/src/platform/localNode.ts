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
   * Den eigenen Ausweis hinterlegen, damit er beim Koppeln mitgehen kann. Ohne
   * ihn bliebe jede Kopplung einseitig: die Gegenseite bekäme in der Antwort
   * nichts, was sie in ihre eigene Liste eintragen könnte.
   */
  register(publicKey: string): Promise<void>
}

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
}

/** Für Umgebungen, die selbst keine Gegenstelle sind — der Browser vor allem. */
export const noLocalNode: LocalNode = {
  profile: (): Promise<DeviceProfile | undefined> => Promise.resolve(undefined),
  peers: (): Promise<DeviceProfile[]> => Promise.resolve([]),
  forget: (): Promise<void> => Promise.resolve(),
  grant: (): Promise<void> => Promise.resolve(),
  register: (): Promise<void> => Promise.resolve(),
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

  const { id, host, port, name, caFingerprint, agentFingerprint, clientKey } = value as Record<
    string,
    unknown
  >

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
  }
}

function hex(field: string, value: unknown, length: number): Record<string, string> {
  if (typeof value !== 'string') {
    return {}
  }

  const trimmed = value.trim().toLowerCase()

  return trimmed.length === length && /^[0-9a-f]+$/.test(trimmed) ? { [field]: trimmed } : {}
}
