/**
 * „Zuletzt verbunden" in Worten.
 *
 * <p>
 * Ein Datum mit Uhrzeit beantwortet die Frage nicht, die man sich vor einer
 * Geräteliste stellt — die lautet „war das gerade eben oder letzte Woche?".
 * Deshalb steht hier eine Spanne und kein Zeitpunkt, und ab einer Woche wird
 * das Datum wieder genauer als jede Zählung von Tagen.
 * </p>
 */
export function lastSeen(at: number | undefined, now = Date.now()): string | undefined {
  if (at === undefined || !Number.isFinite(at) || at <= 0) {
    // Noch nie verbunden — dann steht dort nichts. „Nie" wäre eine Angabe über
    // das Gerät, dabei ist es eine über diese App.
    return undefined
  }

  const seconds = Math.max(0, Math.round((now - at) / 1000))

  if (seconds < 90) {
    return 'gerade eben'
  }

  const minutes = Math.round(seconds / 60)

  if (minutes < 60) {
    return `vor ${minutes} Minuten`
  }

  const hours = Math.round(minutes / 60)

  if (hours < 24) {
    return hours === 1 ? 'vor einer Stunde' : `vor ${hours} Stunden`
  }

  const days = Math.round(hours / 24)

  if (days < 7) {
    return days === 1 ? 'gestern' : `vor ${days} Tagen`
  }

  return `am ${new Date(at).toLocaleDateString()}`
}
