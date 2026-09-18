import { useCallback, useEffect, useRef, useState } from 'react'

/** So lange steht ein Hinweis unten, dann geht er von allein. */
export const TOAST_MS = 2000

/**
 * Ein kurzer Hinweis unten in der Mitte — für Sätze, die nichts
 * falsch melden, sondern nur etwas ankündigen („Noch einmal Zurück beendet
 * die App."). Das rote Band oben ist für Fehler.
 */
export function useToast(): { toast: string | undefined; show: (text: string) => void } {
  const [toast, setToast] = useState<string | undefined>(undefined)
  const timer = useRef<number | undefined>(undefined)

  const show = useCallback((text: string): void => {
    window.clearTimeout(timer.current)
    setToast(text)
    timer.current = window.setTimeout(() => setToast(undefined), TOAST_MS)
  }, [])

  useEffect(() => () => window.clearTimeout(timer.current), [])

  return { toast, show }
}
