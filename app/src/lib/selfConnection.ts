/**
 * Die Sperre gegen Selbstverbindung.
 *
 * Wählt ein Rechner sich selbst als Ziel, zeigt sein Fenster sein eigenes
 * Fenster und darin wieder sich selbst — und die Eingaben laufen im Kreis. Das
 * ist kein hübscher Effekt, sondern ein Rechner, der sich nicht mehr bedienen
 * lässt, bis jemand das Fenster von außen schließt.
 *
 * Verglichen wird der Hostname aus `/api/info` mit dem Namen der Maschine, auf
 * der dieser Client läuft. Den kennt nur eine Umgebung, die ihn hergibt — im
 * Browser gibt es ihn nicht, dort kann die Frage gar nicht auftreten.
 */

/**
 * Ob das Ziel derselbe Rechner ist, auf dem dieser Client läuft.
 *
 * Groß- und Kleinschreibung spielen keine Rolle: Windows meldet den Namen mal
 * in Versalien, mal wie eingetragen. Ein Domänen-Suffix am Hostnamen wird
 * abgeschnitten — `pc.tailnet.ts.net` und `PC` sind derselbe Rechner.
 */
export function isSelfConnection(
  agentHostname: string | undefined,
  machineName: string | undefined,
): boolean {
  if (agentHostname === undefined || machineName === undefined) {
    return false
  }

  const target = shortName(agentHostname)
  const own = shortName(machineName)

  return target.length > 0 && target === own
}

/** Meldung für den Nutzer — sie soll erklären, nicht nur verweigern. */
export function selfConnectionMessage(machineName: string): string {
  return (
    `${machineName} ist dieser Rechner. Sich selbst fernzusteuern ergibt ein ` +
    `Bild im Bild und Eingaben, die im Kreis laufen — deshalb ist das gesperrt.`
  )
}

function shortName(value: string): string {
  const [first = ''] = value.trim().toLowerCase().split('.')

  return first
}
