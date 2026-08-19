/**
 * Was der Update-Bereich der App gerade sagt.
 *
 * Als eigenes Modul, weil daran mehr hängt, als es aussieht: ein Bereich, der
 * „Suche…" stehen lässt, sieht aus wie ein Absturz, und einer, der bei jedem
 * Start „kein Update" meldet, ist Lärm. Die Ansicht selbst bleibt dadurch eine
 * Handvoll Knöpfe.
 */

export type AppUpdateState =
  | { kind: 'checking' }
  /** Es gibt nichts Neues. */
  | { kind: 'current' }
  /** Eine neue Fassung liegt bereit. */
  | { kind: 'offer'; version: string }
  | { kind: 'installing' }
  | { kind: 'failed'; message: string }

export interface AppUpdateLabels {
  text: string
  /** Beschriftung des Knopfes — `undefined` heißt: gerade gibt es nichts zu drücken. */
  action?: string
  /**
   * Ob der Bereich überhaupt sichtbar sein soll. Beim ersten Prüfen und wenn
   * alles aktuell ist, hat er nichts zu sagen: eine Zeile „alles in Ordnung"
   * kostet Platz und Aufmerksamkeit für eine Nachricht, die niemand braucht.
   */
  visible: boolean
}

/**
 * @param asked Ob jemand ausdrücklich nachgesehen hat.
 *
 * Nebenbei am Rande einer anderen Seite hat „alles aktuell" nichts zu sagen und
 * bleibt weg. Auf der Einstellungsseite ist es die Antwort auf eine Frage, die
 * gerade gestellt wurde — dort verschwiegen zu werden sähe aus, als sei der
 * Knopf kaputt.
 */
export function describeAppUpdate(state: AppUpdateState, asked = false): AppUpdateLabels {
  switch (state.kind) {
    case 'checking':
      return { text: 'Suche nach einer neuen Fassung…', visible: asked }

    case 'current':
      return {
        text: 'Die App ist auf dem neuesten Stand.',
        ...(asked ? { action: 'Erneut suchen' } : {}),
        visible: asked,
      }

    case 'offer':
      return {
        text: `Version ${state.version} ist verfügbar`,
        action: 'Jetzt aktualisieren',
        visible: true,
      }

    case 'installing':
      // Android zeigt danach seinen eigenen Dialog. Der Satz sagt, dass gleich
      // etwas passiert, das die App nicht mehr in der Hand hat.
      return { text: 'Wird geladen — Android fragt gleich nach.', visible: true }

    case 'failed':
      return {
        text: state.message,
        action: 'Noch einmal versuchen',
        visible: true,
      }
  }
}
