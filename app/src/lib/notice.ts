import { useEffect, useMemo, useState } from 'react'

/**
 * Wie lange eine Meldung stehen bleibt, wenn sie niemand wegtippt.
 *
 * <p>
 * Lang genug zum Lesen, kurz genug, um nicht zum Möbelstück zu werden. Zwölf
 * Sekunden sind eher großzügig — der Satz darf zweimal gelesen werden.
 * </p>
 */
export const NOTICE_LIFETIME_MS = 12_000

/**
 * Eine Meldung, die von allein wieder geht.
 *
 * <p>
 * **Der Befund dahinter (19.08.2026):** ein Fehlerband blieb stehen, bis jemand
 * es wegtippte. Das klingt nach Sorgfalt und ist das Gegenteil: die meisten
 * Meldungen beschreiben einen Augenblick — die Verbindung wackelte, ein Recht
 * fehlte gerade, ein Gerät antwortete nicht. Ist der Augenblick vorbei,
 * beschreibt der Satz nichts mehr, steht aber weiter über der Oberfläche und
 * behauptet ein Problem. Wer ihn liest, sucht nach etwas, das es nicht mehr
 * gibt; wer ihn zweimal gelesen hat, liest ihn beim dritten Mal nicht mehr —
 * und übersieht dann den einen, auf den es ankommt.
 * </p>
 *
 * <p>
 * **Drei Wege hinaus, und nur einer davon ist der Finger.** Die Frist deckt den
 * Normalfall ab. Dazu kommen die Augenblicke, in denen eine Meldung nachweislich
 * nichts mehr beschreibt: wenn die Verbindung wieder steht, und wenn man das
 * Gerät verlässt, über das sie sprach. Beides ruft {@link Notices.clear} — siehe
 * `App.tsx`.
 * </p>
 *
 * <p>
 * Die Uhr steht hier und nicht in der Ansicht, damit beides prüfbar bleibt: dass
 * eine zweite Meldung die Frist zurücksetzt, und dass ein aufgelöster Zeitgeber
 * nicht nachfeuert. Beides sind Fehler, die man am Bildschirm erst bemerkt,
 * wenn sie schon eine Weile stören.
 * </p>
 */
export class Notices {
  private current: string | undefined
  private timer: ReturnType<typeof setTimeout> | undefined

  /**
   * @param onChange Wird bei jeder Änderung mit dem neuen Stand gerufen.
   * @param lifetimeMs Wie lange eine Meldung stehen bleibt.
   */
  constructor(
    private readonly onChange: (message: string | undefined) => void,
    private readonly lifetimeMs: number = NOTICE_LIFETIME_MS,
  ) {}

  get message(): string | undefined {
    return this.current
  }

  /** Etwas melden. Eine neue Meldung setzt die Frist zurück. */
  report(message: string): void {
    // Erst den alten Zeitgeber abräumen: sonst löschte er gleich die *neue*
    // Meldung, weil seine Frist schon halb aufgebraucht war.
    this.stop()

    this.current = message
    this.onChange(message)

    this.timer = setTimeout(() => {
      this.timer = undefined
      this.current = undefined
      this.onChange(undefined)
    }, this.lifetimeMs)
  }

  /** Weg damit — von Hand oder weil die Meldung nichts mehr beschreibt. */
  clear(): void {
    this.stop()

    if (this.current === undefined) {
      return
    }

    this.current = undefined
    this.onChange(undefined)
  }

  /** Für das Ende einer Ansicht: ein Zeitgeber ohne Ziel ist ein Fehler. */
  dispose(): void {
    this.stop()
  }

  private stop(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer)
      this.timer = undefined
    }
  }
}

export interface Notice {
  /** Was gerade dasteht, oder `undefined`. */
  message: string | undefined
  /** Etwas melden. Eine neue Meldung setzt die Frist zurück. */
  report: (message: string) => void
  /** Weg damit — von Hand oder weil sie nichts mehr beschreibt. */
  clear: () => void
}

/** Die Anbindung an React — die Regeln stehen in {@link Notices}. */
export function useNotice(lifetimeMs: number = NOTICE_LIFETIME_MS): Notice {
  const [message, setMessage] = useState<string | undefined>(undefined)

  const notices = useMemo(
    () => new Notices(setMessage, lifetimeMs),
    [lifetimeMs],
  )

  useEffect(() => () => notices.dispose(), [notices])

  return {
    message,
    report: (text) => notices.report(text),
    clear: () => notices.clear(),
  }
}
