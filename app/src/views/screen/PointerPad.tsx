import { useRef } from 'react'
import type { Point } from '../../lib/screenGestures.ts'
import type { MouseButton } from '../../lib/types.ts'

/**
 * Wie weit gezogen werden muss, um einmal zu verdoppeln beziehungsweise zu
 * halbieren. In Bildschirmpunkten, gemessen an einem Fenster üblicher Höhe.
 */
const ZOOM_TRAVEL_PX = 220

/** Weiter als das geht keine Zoomgeste — sonst springt das Handy ins Nichts. */
const MIN_PINCH = 0.25
const MAX_PINCH = 4

/** Ein Rasterschritt des Mausrads, wie ihn der Browser meldet. */
const WHEEL_NOTCH = 100

interface Props {
  /**
   * Ob am anderen Ende Berührungen erwartet werden.
   *
   * Daran hängt genau eine Sache: was ein gezogener Rechtsklick bedeutet. Auf
   * einem Rechner ist er ein Rechtsklick, auf einem Handy die einzige Geste,
   * die sich mit einer Maus nicht nachbauen lässt — zwei Finger, die
   * auseinandergehen.
   */
  touchRemote: boolean
  /** Der Zeiger steht jetzt hier — Bildschirmkoordinaten. */
  onMove: (point: Point) => void
  onDown: (button: MouseButton) => void
  onUp: (button: MouseButton) => void
  /** Rasterschritte; positiv ist hoch. */
  onScroll: (notches: number) => void
  /** Mittelpunkt der Geste und der Faktor darauf; über 1 heißt heranholen. */
  onPinch: (center: Point, scale: number) => void
}

/**
 * Die Maus über dem Bildschirmbild — das Gegenstück zu {@link GesturePad} für
 * einen Rechner, der einen anderen steuert.
 *
 * <p>
 * **Warum es das braucht:** die Fläche über dem Bild hörte bisher nur auf
 * Finger. Am Handy war das richtig; im Windows-Fenster kam damit kein einziger
 * Klick an — wer einen anderen Rechner oder ein Handy vor sich hatte, sah ein
 * Bild und konnte nichts damit tun.
 * </p>
 *
 * <p>
 * Übersetzt wird eins zu eins und ohne Deutung: Drücken ist Drücken, Loslassen
 * ist Loslassen. Was daraus wird — ein Tippen, ein Ziehen, ein langer Druck —
 * entscheidet die Gegenseite, weil nur sie weiß, was sie hat. Die einzige
 * Ausnahme ist der gezogene Rechtsklick auf einem Handy, und die steht oben.
 * </p>
 */
export function PointerPad({
  touchRemote,
  onMove,
  onDown,
  onUp,
  onScroll,
  onPinch,
}: Props): React.JSX.Element {
  /** Wo die Zoomgeste angefangen hat — `undefined`, wenn gerade keine läuft. */
  const pinchFrom = useRef<Point | undefined>(undefined)
  const pinchTo = useRef<Point | undefined>(undefined)

  const at = (event: React.PointerEvent | React.WheelEvent): Point => ({
    x: event.clientX,
    y: event.clientY,
  })

  const handleDown = (event: React.PointerEvent<HTMLDivElement>): void => {
    // Ohne das nimmt der Browser die Fläche als Textauswahl und verliert bei
    // schnellem Ziehen die folgenden Ereignisse.
    event.preventDefault()
    event.currentTarget.setPointerCapture(event.pointerId)

    const point = at(event)

    if (event.button === 2 && touchRemote) {
      pinchFrom.current = point
      pinchTo.current = point
      return
    }

    onMove(point)
    onDown(buttonOf(event.button))
  }

  const handleMove = (event: React.PointerEvent<HTMLDivElement>): void => {
    const point = at(event)

    if (pinchFrom.current !== undefined) {
      // Die Geste geht erst beim Loslassen hinaus: eine Zoomgeste je
      // Mausbewegung wären dreißig sich überholende Zweifingergesten in der
      // Sekunde, und Android verwirft alle bis auf die erste.
      pinchTo.current = point
      return
    }

    onMove(point)
  }

  const handleUp = (event: React.PointerEvent<HTMLDivElement>): void => {
    const start = pinchFrom.current

    if (start !== undefined) {
      const end = pinchTo.current ?? start

      pinchFrom.current = undefined
      pinchTo.current = undefined

      const scale = pinchScale(start.y - end.y)

      // Ein Rechtsklick ohne Zug ist keine Geste, sondern ein verrutschter
      // Finger. Ihn als Zoom um Faktor 1 zu schicken hieße, das Handy für
      // nichts eine Geste ausführen zu lassen.
      if (scale !== 1) {
        onPinch(start, scale)
      }

      return
    }

    onUp(buttonOf(event.button))
  }

  return (
    <div
      className="gesture-pad"
      onPointerDown={handleDown}
      onPointerMove={handleMove}
      onPointerUp={handleUp}
      onPointerCancel={handleUp}
      // Sonst legt Windows sein eigenes Menü über das Bild, sobald jemand
      // drüben etwas mit der rechten Taste tun will.
      onContextMenu={(event) => event.preventDefault()}
      onWheel={(event) => {
        const notches = Math.round(-event.deltaY / WHEEL_NOTCH)

        // Ein feines Rad meldet Bruchteile eines Rasterschritts. Sie zu
        // verwerfen hieße, dass ein Präzisionsrad gar nicht scrollt.
        onScroll(notches === 0 ? (event.deltaY > 0 ? -1 : 1) : notches)
      }}
    />
  )
}

/** Wie weit gezogen wurde, als Faktor. Nach oben heißt heranholen. */
function pinchScale(travel: number): number {
  const raw = 2 ** (travel / ZOOM_TRAVEL_PX)

  return Math.min(MAX_PINCH, Math.max(MIN_PINCH, raw))
}

export function buttonOf(index: number): MouseButton {
  return index === 2 ? 'right' : index === 1 ? 'middle' : 'left'
}
