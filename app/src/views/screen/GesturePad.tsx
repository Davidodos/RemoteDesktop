import { useEffect, useRef } from 'react'
import {
  decideTwoFinger,
  distance,
  isTap,
  midpoint,
  scrollNotches,
  type Point,
  type TwoFingerMode,
} from '../../lib/screenGestures.ts'

/** Länger gedrückt ohne Bewegung = Rechtsklick. */
const LONG_PRESS_MS = 500

/**
 * Höchstabstand zwischen zwei Tipps, damit der zweite noch als Doppeltipp
 * zählt. Bleibt der Finger beim zweiten Tipp liegen, wird daraus ein Ziehen.
 */
const DOUBLE_TAP_MS = 320

interface Props {
  /**
   * Wischen mit einem Finger, in CSS-Pixeln, dazu die Fingerposition.
   * `true` heißt: daraus soll am Ende kein Klick mehr werden.
   */
  onPan: (dx: number, dy: number, point: Point) => boolean
  /** Auseinanderziehen zweier Finger, als Faktor auf den bisherigen Zoom. */
  onZoom: (factor: number, center: Point) => void
  onScroll: (notches: number) => void
  onTap: (point: Point) => void
  /** Langes Drücken oder ein Tipp mit zwei Fingern. */
  onLongPress: (point: Point) => void
  /** Doppeltipp, bei dem der Finger liegen bleibt — Maustaste gedrückt halten. */
  onHoldStart: (point: Point) => void
  onHoldEnd: () => void
}

/** Was während einer laufenden Berührung gemerkt werden muss. */
interface Gesture {
  start: Point
  last: Point
  longPressTimer: number | undefined
  /** Die Geste war Scrollen oder Zoomen — am Ende also kein Klick. */
  handled: boolean
  pinchDistance: number
  /** Fingerabstand und Mitte beim Aufsetzen — Bezugspunkt der Entscheidung. */
  startSpread: number
  startCenter: Point
  twoFinger: TwoFingerMode
  scrollRest: number
  /** Zwei Finger aufgesetzt und wieder abgehoben, ohne zu zoomen oder zu scrollen. */
  twoFingerTap: boolean
  twoFingerStart: Point
  /** Der Finger hält gerade die Maustaste gedrückt. */
  holding: boolean
}

const IDLE: Gesture = {
  start: { x: 0, y: 0 },
  last: { x: 0, y: 0 },
  longPressTimer: undefined,
  handled: false,
  pinchDistance: 0,
  startSpread: 0,
  startCenter: { x: 0, y: 0 },
  twoFinger: 'undecided',
  scrollRest: 0,
  twoFingerTap: false,
  twoFingerStart: { x: 0, y: 0 },
  holding: false,
}

/**
 * Durchsichtige Fläche über dem Bildschirmbild, die Fingergesten einordnet:
 * ein Finger wischt, Tippen, langes Drücken, zwei Finger scrollen oder zoomen.
 *
 * Meldet nur, was die Finger getan haben — was daraus wird, entscheidet die
 * Bildschirmansicht. Sie arbeitet mit und ohne Zeiger-Overlay mit derselben
 * Fläche, damit sich die Gesten in beiden Fällen gleich anfühlen.
 */
export function GesturePad({
  onPan,
  onZoom,
  onScroll,
  onTap,
  onLongPress,
  onHoldStart,
  onHoldEnd,
}: Props): React.JSX.Element {
  const gesture = useRef<Gesture>({ ...IDLE })
  const padRef = useRef<HTMLDivElement>(null)

  /*
    Ohne dieses `preventDefault` erzeugt Android aus jeder Berührung zusätzlich
    Maus-Ereignisse, und deren Standardverhalten nimmt dem Eingabefeld den
    Fokus — bei offener Texteingabe klappte die Handy-Tastatur bei jedem Klick
    auf das Bild zu. React hängt `touchstart` passiv ein, dort wirkt
    `preventDefault` nicht; deshalb ein eigener Listener.
  */
  useEffect(() => {
    const pad = padRef.current

    if (pad === null) {
      return
    }

    const swallow = (event: TouchEvent): void => event.preventDefault()

    pad.addEventListener('touchstart', swallow, { passive: false })

    return () => pad.removeEventListener('touchstart', swallow)
  }, [])

  // Der letzte abgeschlossene Tipp — er entscheidet, ob der nächste ein
  // Doppeltipp ist. Muss die einzelne Geste überleben.
  const lastTap = useRef<{ at: number; point: Point } | undefined>(undefined)

  const cancelLongPress = (): void => {
    if (gesture.current.longPressTimer !== undefined) {
      clearTimeout(gesture.current.longPressTimer)
      gesture.current.longPressTimer = undefined
    }
  }

  const handleStart = (event: React.TouchEvent): void => {
    const [first, second] = [event.touches[0], event.touches[1]]

    if (first === undefined) {
      return
    }

    const point = { x: first.clientX, y: first.clientY }

    if (second !== undefined) {
      const other = { x: second.clientX, y: second.clientY }

      const center = midpoint(point, other)
      const spread = distance(point, other)

      cancelLongPress()
      gesture.current = {
        ...gesture.current,
        last: center,
        handled: true,
        pinchDistance: spread,
        startSpread: spread,
        startCenter: center,
        twoFinger: 'undecided',
        scrollRest: 0,
        twoFingerTap: true,
        twoFingerStart: center,
      }

      return
    }

    const previous = lastTap.current

    if (
      previous !== undefined &&
      Date.now() - previous.at < DOUBLE_TAP_MS &&
      isTap(previous.point, point)
    ) {
      // Zweiter Tipp, Finger bleibt liegen: ab jetzt wird gezogen. Der Klick
      // des ersten Tipps ist schon draußen — genau wie auf einem Trackpad.
      lastTap.current = undefined
      gesture.current = { ...IDLE, start: point, last: point, handled: true, holding: true }
      onHoldStart(point)
      navigator.vibrate?.(30)
      return
    }

    gesture.current = {
      ...IDLE,
      start: point,
      last: point,
      longPressTimer: window.setTimeout(() => {
        gesture.current.longPressTimer = undefined
        gesture.current.handled = true
        onLongPress(point)
        navigator.vibrate?.(40)
      }, LONG_PRESS_MS),
    }
  }

  const handleMove = (event: React.TouchEvent): void => {
    const [first, second] = [event.touches[0], event.touches[1]]

    if (first === undefined) {
      return
    }

    const point = { x: first.clientX, y: first.clientY }

    if (second !== undefined) {
      handleTwoFinger(point, { x: second.clientX, y: second.clientY })
      return
    }

    const previous = gesture.current.last
    gesture.current.last = point

    if (!isTap(gesture.current.start, point)) {
      cancelLongPress()
    }

    if (onPan(point.x - previous.x, point.y - previous.y, point)) {
      gesture.current.handled = true
    }
  }

  const handleTwoFinger = (first: Point, second: Point): void => {
    const current = gesture.current
    const center = midpoint(first, second)
    const spread = distance(first, second)

    const previous = current.twoFinger

    // Gemessen wird seit dem Aufsetzen, nicht von Ereignis zu Ereignis — pro
    // Ereignis sind es nur wenige Pixel.
    current.twoFinger = decideTwoFinger(
      previous,
      spread - current.startSpread,
      center.y - current.startCenter.y,
    )

    // Sobald gezoomt, gescrollt oder nennenswert gewandert wird, war es kein
    // Tipp mehr.
    if (current.twoFinger !== 'undecided' || !isTap(current.twoFingerStart, center)) {
      current.twoFingerTap = false
    }

    if (current.twoFinger !== previous) {
      // Im Moment der Entscheidung neu ansetzen: sonst käme die bis dahin
      // aufgelaufene Bewegung auf einen Schlag ins Bild, und das sieht aus wie
      // ein Sprung.
      current.pinchDistance = spread
      current.last = center
      return
    }

    if (current.twoFinger === 'zoom' && current.pinchDistance > 0) {
      onZoom(spread / current.pinchDistance, center)
    } else if (current.twoFinger === 'scroll') {
      const { notches, rest } = scrollNotches(current.scrollRest + (center.y - current.last.y))

      if (notches !== 0) {
        onScroll(notches)
      }

      current.scrollRest = rest
    }

    current.pinchDistance = spread
    current.last = center
  }

  const handleEnd = (event: React.TouchEvent): void => {
    cancelLongPress()

    const finished = event.changedTouches[0]
    const current = gesture.current
    const remaining = event.touches[0]

    if (remaining !== undefined) {
      // Von zwei Fingern ist einer übrig. Ohne diesen Neuansatz meldete der
      // verbliebene Finger den Abstand zur bisherigen Fingermitte als Wischer —
      // das Bild sprang.
      const point = { x: remaining.clientX, y: remaining.clientY }

      current.start = point
      current.last = point
      current.handled = true
      return
    }
    const point =
      finished === undefined ? undefined : { x: finished.clientX, y: finished.clientY }

    if (event.touches.length === 0 && current.holding) {
      onHoldEnd()
    } else if (event.touches.length === 0 && current.twoFingerTap) {
      // Tipp mit zwei Fingern = Rechtsklick, wie auf einem Notebook-Trackpad.
      onLongPress(current.twoFingerStart)
      navigator.vibrate?.(40)
    } else if (
      event.touches.length === 0 &&
      !current.handled &&
      point !== undefined &&
      isTap(current.start, point)
    ) {
      onTap(point)
      navigator.vibrate?.(15)
      lastTap.current = { at: Date.now(), point }
    }

    if (event.touches.length === 0) {
      gesture.current = { ...IDLE }
    }
  }

  return (
    <div
      ref={padRef}
      className="gesture-pad"
      onTouchStart={handleStart}
      onTouchMove={handleMove}
      onTouchEnd={handleEnd}
      onTouchCancel={handleEnd}
    />
  )
}
