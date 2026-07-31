import { useEffect, useRef, useState } from 'react'
import type { InputChannel } from '../lib/inputChannel.ts'
import { getPlatform } from '../platform/index.ts'
import type { MouseButton } from '../lib/types.ts'

/** Ab dieser Distanz gilt eine Berührung als Bewegung, nicht als Tippen. */
const TAP_MOVE_TOLERANCE_PX = 10

/** Länger gedrückt ohne Bewegung = Rechtsklick. */
const LONG_PRESS_MS = 500

/** Zeigerbeschleunigung — ein Wischen soll den ganzen Bildschirm überstreichen. */
const POINTER_SPEED = 1.8

/** Fingerweg pro Scroll-Rastung. */
const SCROLL_STEP_PX = 24

interface Props {
  input: InputChannel
}

/**
 * Trackpad-Fläche für relative Mausbewegung.
 *
 * Gesten: ein Finger bewegt den Zeiger, kurzes Tippen ist ein Linksklick,
 * langes Drücken ein Rechtsklick, zwei Finger scrollen.
 */
export function TouchpadView({ input }: Props): React.JSX.Element {
  const [held, setHeld] = useState<MouseButton | undefined>(undefined)
  const [locked, setLocked] = useState(false)

  const surface = useRef<HTMLDivElement | null>(null)
  const canLock = getPlatform().capabilities.pointerLock

  const lastPoint = useRef<{ x: number; y: number } | undefined>(undefined)
  const startPoint = useRef<{ x: number; y: number } | undefined>(undefined)
  const scrollRest = useRef(0)
  const longPressTimer = useRef<number | undefined>(undefined)
  const gestureHandled = useRef(false)

  /**
   * Am Desktop wird der echte Zeiger eingefangen und seine Bewegung
   * weitergereicht. Das ist genauer als jede nachgeführte Position — und es
   * erspart der App, sich zu merken, wo der Zeiger drüben gerade steht.
   */
  useEffect(() => {
    if (!canLock) {
      return
    }

    const onChange = (): void => setLocked(document.pointerLockElement === surface.current)

    const onMove = (event: MouseEvent): void => {
      if (document.pointerLockElement !== surface.current) {
        return
      }

      input.moveBy(event.movementX, event.movementY)
    }

    document.addEventListener('pointerlockchange', onChange)
    document.addEventListener('mousemove', onMove)

    return () => {
      document.removeEventListener('pointerlockchange', onChange)
      document.removeEventListener('mousemove', onMove)
    }
  }, [canLock, input])

  const cancelLongPress = (): void => {
    if (longPressTimer.current !== undefined) {
      clearTimeout(longPressTimer.current)
      longPressTimer.current = undefined
    }
  }

  const handleStart = (event: React.TouchEvent): void => {
    const touch = event.touches[0]

    if (touch === undefined) {
      return
    }

    lastPoint.current = { x: touch.clientX, y: touch.clientY }
    startPoint.current = { x: touch.clientX, y: touch.clientY }
    scrollRest.current = 0
    gestureHandled.current = false

    if (event.touches.length === 1) {
      longPressTimer.current = window.setTimeout(() => {
        input.click('right')
        gestureHandled.current = true
        navigator.vibrate?.(40)
      }, LONG_PRESS_MS)
    } else {
      cancelLongPress()
    }
  }

  const handleMove = (event: React.TouchEvent): void => {
    const touch = event.touches[0]
    const previous = lastPoint.current

    if (touch === undefined || previous === undefined) {
      return
    }

    const dx = touch.clientX - previous.x
    const dy = touch.clientY - previous.y

    lastPoint.current = { x: touch.clientX, y: touch.clientY }

    if (movedBeyondTolerance(startPoint.current, touch)) {
      cancelLongPress()
    }

    if (event.touches.length >= 2) {
      handleTwoFingerScroll(dy)
      return
    }

    input.moveBy(Math.round(dx * POINTER_SPEED), Math.round(dy * POINTER_SPEED))
  }

  const handleTwoFingerScroll = (dy: number): void => {
    scrollRest.current += dy

    const notches = Math.trunc(scrollRest.current / SCROLL_STEP_PX)

    if (notches !== 0) {
      // Nach unten wischen soll den Inhalt nach unten bewegen — daher das
      // Vorzeichen umdrehen.
      input.scroll(notches)
      scrollRest.current -= notches * SCROLL_STEP_PX
      gestureHandled.current = true
    }
  }

  const handleEnd = (event: React.TouchEvent): void => {
    cancelLongPress()

    const wasTap =
      !gestureHandled.current &&
      event.touches.length === 0 &&
      !movedBeyondTolerance(startPoint.current, event.changedTouches[0])

    if (wasTap) {
      input.click('left')
      navigator.vibrate?.(15)
    }

    lastPoint.current = undefined
    startPoint.current = undefined
  }

  /** Taste dauerhaft gedrückt halten — für Drag-and-Drop und Spiele. */
  const toggleHold = (button: MouseButton): void => {
    if (held === button) {
      input.buttonUp(button)
      setHeld(undefined)
      return
    }

    if (held !== undefined) {
      input.buttonUp(held)
    }

    input.buttonDown(button)
    setHeld(button)
    navigator.vibrate?.(30)
  }

  return (
    <div className="touchpad-view">
      <div
        ref={surface}
        className="touchpad-surface"
        onTouchStart={handleStart}
        onTouchMove={handleMove}
        onTouchEnd={handleEnd}
        onTouchCancel={handleEnd}
        onClick={() => {
          if (canLock && !locked) {
            void surface.current?.requestPointerLock()
          }
        }}
      >
        <span className="touchpad-hint">
          {!canLock
            ? 'Wischen bewegt · Tippen klickt · Halten = Rechtsklick · zwei Finger scrollen'
            : locked
              ? 'Maus eingefangen — Esc gibt sie wieder frei'
              : 'Klicken fängt die Maus ein'}
        </span>
      </div>

      <div className="button-row">
        <button type="button" className="mouse-button" onClick={() => input.click('left')}>
          Links
        </button>
        <button type="button" className="mouse-button" onClick={() => input.click('middle')}>
          Mitte
        </button>
        <button type="button" className="mouse-button" onClick={() => input.click('right')}>
          Rechts
        </button>
      </div>

      <div className="button-row">
        {(['left', 'right'] as const).map((button) => (
          <button
            key={button}
            type="button"
            className={held === button ? 'hold-button active' : 'hold-button'}
            onClick={() => toggleHold(button)}
          >
            {button === 'left' ? 'Links' : 'Rechts'} halten
            {held === button ? ' ●' : ''}
          </button>
        ))}
      </div>
    </div>
  )
}

function movedBeyondTolerance(
  start: { x: number; y: number } | undefined,
  touch: React.Touch | Touch | undefined,
): boolean {
  if (start === undefined || touch === undefined) {
    return false
  }

  return (
    Math.abs(touch.clientX - start.x) > TAP_MOVE_TOLERANCE_PX ||
    Math.abs(touch.clientY - start.y) > TAP_MOVE_TOLERANCE_PX
  )
}
