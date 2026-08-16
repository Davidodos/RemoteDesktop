/**
 * Die Sperre gegen Selbstverbindung.
 *
 * Wählt ein Rechner sich selbst als Ziel, zeigt sein Fenster sein eigenes
 * Fenster und darin wieder sich selbst — und die Eingaben laufen im Kreis. Das
 * ist kein hübscher Effekt, sondern ein Rechner, der sich nicht mehr bedienen
 * lässt, bis jemand das Fenster von außen schließt.
 *
 * Verglichen werden die Fingerabdrücke der beiden Agent-Schlüssel. Nur wo einer
 * davon fehlt, bleibt der Name — er ist ein Anhaltspunkt und kein Ausweis, und
 * genau daran ist die frühere Fassung gescheitert.
 */

/** Woran ein Gerät eindeutig zu erkennen ist — und woran nur ungefähr. */
export interface Identity {
  /** Der Name, den es von sich angibt. Ein Anhaltspunkt, kein Ausweis. */
  name?: string
  /**
   * Der Fingerabdruck seines Agent-Schlüssels. Eindeutig, und deshalb das
   * einzige, worauf sich diese Sperre verlässt, sobald es ihn gibt.
   */
  fingerprint?: string
}

/**
 * Ob das Ziel derselbe Rechner ist, auf dem dieser Client läuft.
 *
 * <p>
 * **Entschieden wird am Fingerabdruck, nicht am Namen.** Der Namensvergleich
 * war ein Fehlschluss, und er hat zugeschlagen: ein Handy meldet als Namen, was
 * unter „Gerätename" in den Android-Einstellungen steht — häufig schlicht der
 * Vorname seines Besitzers. Heißt der Windows-Rechner genauso, hielt die App
 * das Handy für den Rechner, vor dem man sitzt, und verweigerte die Verbindung
 * mit einer Begründung, die niemand nachvollziehen konnte. Zwei Geräte dürfen
 * denselben Namen tragen; denselben Schlüssel dürfen sie nicht.
 * </p>
 *
 * <p>
 * Der Name bleibt der Notbehelf für den Fall, dass eine der beiden Seiten
 * keinen Fingerabdruck hat — ein Waker etwa, oder ein Agent aus einer Fassung
 * vor der Kopplung. Dort ist er das Einzige, was es gibt.
 * </p>
 */
export function isSelfConnection(target: Identity, own: Identity): boolean {
  // Zwei Kennungen, die sich unterscheiden, gehören zwei Geräten — gleich, wie
  // sie heißen. Das ist der ganze Fix.
  if (target.fingerprint !== undefined && own.fingerprint !== undefined) {
    return target.fingerprint === own.fingerprint
  }

  if (target.name === undefined || own.name === undefined) {
    return false
  }

  const wanted = shortName(target.name)

  return wanted.length > 0 && wanted === shortName(own.name)
}

/** Meldung für den Nutzer — sie soll erklären, nicht nur verweigern. */
export function selfConnectionMessage(machineName: string): string {
  return (
    `${machineName} ist dieser Rechner. Sich selbst fernzusteuern ergibt ein ` +
    `Bild im Bild und Eingaben, die im Kreis laufen — deshalb ist das gesperrt.`
  )
}

/**
 * Ein Domänen-Suffix wird abgeschnitten, Groß- und Kleinschreibung ignoriert:
 * `pc.tailnet.ts.net` und `PC` sind derselbe Rechner.
 */
function shortName(value: string): string {
  const [first = ''] = value.trim().toLowerCase().split('.')

  return first
}
