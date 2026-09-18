/**
 * Die Zurück-Taste des Systems — Android hat eine, das Fenster und der
 * Browser nicht.
 *
 * Ohne Zuhörer beendet Android die App beim ersten Druck. Mit einem entscheidet
 * die Seite: eine Ebene hoch, und erst auf der obersten ein zweiter Druck
 * binnen kurzer Zeit beendet (`lib/useBackButton.ts`).
 */
export interface AppNavigation {
  /** Ob es eine Zurück-Taste gibt, auf die es zu hören lohnt. */
  readonly available: boolean
  /** Meldet jeden Druck. Gibt die Abmeldung zurück. */
  onBack(listener: () => void): () => void
  /** Beendet die App — der zweite Druck auf der obersten Ebene. */
  exit(): Promise<void>
}

export const noAppNavigation: AppNavigation = {
  available: false,
  onBack: () => () => undefined,
  exit: () => Promise.resolve(),
}
