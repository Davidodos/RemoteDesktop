import { PlatformError } from './errors.ts'

/**
 * Wo das Kürzel für die Übernahme liegt.
 *
 * <p>
 * **Warum das nicht in den App-Speicher gehört.** Es ist keine Einstellung der
 * Oberfläche, sondern eine des Fensters: geändert wird es dort unter
 * „Einstellungen", und angezeigt gehört es an dieselbe Stelle. Läge es im
 * localStorage der WebView, könnte die native Seite es weder lesen noch
 * schreiben — und es stünde an zwei Orten, die nur so lange übereinstimmen, wie
 * niemand einen davon anfasst.
 * </p>
 *
 * <p>
 * **Wo es sie nicht gibt.** Ein Handy übernimmt keinen Rechner: es hat weder
 * Maus noch Tastatur, die es einfangen könnte. Dort steht {@link noHotkey}, und
 * die App bietet die Übernahme gar nicht erst an.
 * </p>
 */
export interface HotkeySetting {
  /** Ob diese Umgebung eine Übernahme kennt. */
  readonly available: boolean

  /**
   * Das gespeicherte Kürzel in der Form aus `lib/hotkey.ts`.
   *
   * @returns `undefined`, solange keins vergeben wurde — genau daran erkennt
   *   die App, dass sie beim ersten Verbinden danach fragen muss.
   */
  read(): Promise<string | undefined>

  write(value: string): Promise<void>
}

export const noHotkey: HotkeySetting = {
  available: false,

  read: () => Promise.resolve(undefined),

  write: () =>
    Promise.reject(
      new PlatformError('Dieses Gerät übernimmt keinen fremden Rechner — es hat keine Maus.'),
    ),
}
