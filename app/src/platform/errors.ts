/**
 * Eine Fähigkeit, die es auf dieser Plattform nicht gibt.
 *
 * Wird bewusst geworfen statt still nichts zu tun: ein Aufruf ohne vorherige
 * Prüfung der Fähigkeiten ist ein Fehler im Aufrufer, und der soll sichtbar
 * sein.
 *
 * Steht in einer eigenen Datei, damit `web.ts` sie benutzen kann, ohne dass
 * zwischen `index.ts` und den Umsetzungen ein Ringschluss entsteht.
 */
export class PlatformError extends Error {
  constructor(message: string, options: { cause?: unknown } = {}) {
    super(message, options)
    this.name = 'PlatformError'
  }
}
