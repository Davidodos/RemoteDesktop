/**
 * Das eigene Gerät als Gegenstelle.
 *
 * Wer koppelt, richtet damit nur eine Richtung ein: sein Schlüssel liegt danach
 * beim anderen. Für die Gegenrichtung braucht der andere einen Kopplungscode
 * von **hier** — und den kann nur diese Seite ausstellen.
 *
 * Beide Wege laufen nativ und nicht über HTTP: der eigene Agent weist sich mit
 * einem selbst ausgestellten Zertifikat aus, und die Seite müsste ihm erst
 * vertrauen, um ihn nach dem Vertrauen fragen zu können. Am Handy führt der Weg
 * über das Plugin, im Fenster über die Brücke zur Wirtsanwendung.
 */
export interface LocalNode {
  /**
   * Was die Gegenseite braucht, um sich hier zu melden — Adresse, Port, ein
   * frischer Code und der eigene Fingerabdruck.
   *
   * `undefined` heißt: dieses Gerät ist nicht steuerbar (kein Agent, kein
   * eingeschalteter Host). Dann bleibt es bei der einen Richtung, und das ist
   * kein Fehler.
   */
  offer(): Promise<BackPairing | undefined>

  /**
   * Das Angebot, das die Gegenseite beim Koppeln hinterlassen hat. Einmalig:
   * beim Lesen verbraucht.
   */
  take(): Promise<BackPairing | undefined>
}

export interface BackPairing {
  host: string
  port: number
  /** Sechs Ziffern, fünf Minuten gültig, einmal verwendbar. */
  code: string
  /**
   * Der Fingerabdruck der eigenen Stelle. Er kommt über eine Verbindung, die
   * bereits beglaubigt ist — deshalb muss ihn hier niemand mehr ablesen und
   * vergleichen. Das ist der eigentliche Gewinn der Gegenkopplung.
   */
  caFingerprint?: string
  /** Wie das Gerät heißt. Nur für die Anzeige. */
  name?: string
}

/** Für Umgebungen, die selbst keine Gegenstelle sind — der Browser vor allem. */
export const noLocalNode: LocalNode = {
  offer: (): Promise<BackPairing | undefined> => Promise.resolve(undefined),
  take: (): Promise<BackPairing | undefined> => Promise.resolve(undefined),
}

/**
 * Was von einem Angebot übrig bleibt, wenn man es ernst nimmt.
 *
 * Dieselben Regeln wie auf beiden Agent-Seiten: eine Adresse, ein brauchbarer
 * Port, sechs Ziffern. Unvollständiges wird verworfen statt halb benutzt — ein
 * Angebot, an dem etwas fehlt, scheitert sonst später an einer Stelle, an der
 * niemand mehr weiß, woher es kam.
 */
export function usableOffer(value: unknown): BackPairing | undefined {
  if (typeof value !== 'object' || value === null) {
    return undefined
  }

  const { host, port, code, caFingerprint, name } = value as Record<string, unknown>

  if (typeof host !== 'string' || host.trim().length === 0 || host.length > 255) {
    return undefined
  }

  if (typeof port !== 'number' || !Number.isInteger(port) || port < 1 || port > 65535) {
    return undefined
  }

  if (typeof code !== 'string' || !/^\d{6}$/.test(code.trim())) {
    return undefined
  }

  const fingerprint =
    typeof caFingerprint === 'string' && /^[0-9a-f]{64}$/i.test(caFingerprint.trim())
      ? caFingerprint.trim().toLowerCase()
      : undefined

  return {
    host: host.trim(),
    port,
    code: code.trim(),
    ...(fingerprint === undefined ? {} : { caFingerprint: fingerprint }),
    ...(typeof name === 'string' && name.trim().length > 0 ? { name: name.trim() } : {}),
  }
}
