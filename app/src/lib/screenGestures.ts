/** Ein Punkt in Bildschirmkoordinaten des Handys. */
export interface Point {
  x: number
  y: number
}

/** Ausschnitt des Bildes: Zoomfaktor und Verschiebung in CSS-Pixeln. */
export interface Viewport {
  scale: number
  offsetX: number
  offsetY: number
}

export const MIN_ZOOM = 1
export const MAX_ZOOM = 6

/**
 * Zoom, mit dem das Zeiger-Overlay startet. Weit genug hinein, dass sich ein
 * Fensterknopf treffen lässt, aber noch genug Umgebung, um sich zu orientieren.
 */
export const POINTER_ZOOM = 2.5

/** Ab dieser Distanz gilt eine Berührung als Bewegung, nicht als Tippen. */
export const TAP_TOLERANCE_PX = 12

/** Fingerweg pro Scroll-Rastung bei zwei Fingern. */
export const SCROLL_STEP_PX = 24

/**
 * Ab dieser Abstandsänderung zwischen zwei Fingern ist eine Geste ein Zoom und
 * kein Scrollen. Ohne die Schwelle zoomt jedes Zwei-Finger-Wischen ein wenig,
 * weil zwei Finger nie exakt parallel laufen.
 */
export const PINCH_THRESHOLD_PX = 24

/**
 * Reine Rechenlogik der Bildschirm-Gesten — ohne DOM, damit sie testbar bleibt.
 * Alles hier entscheidet darüber, ob ein Klick dort landet, wo der Finger war.
 */

export function distance(a: Point, b: Point): number {
  return Math.hypot(a.x - b.x, a.y - b.y)
}

export function midpoint(a: Point, b: Point): Point {
  return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }
}

export function isTap(start: Point, end: Point, tolerance = TAP_TOLERANCE_PX): boolean {
  return distance(start, end) <= tolerance
}

export function clampScale(scale: number): number {
  return Math.min(Math.max(scale, MIN_ZOOM), MAX_ZOOM)
}

/**
 * Begrenzt die Verschiebung auf den Bereich, in dem noch Bild zu sehen ist.
 *
 * Bei Zoomfaktor 1 gibt es nichts zu verschieben; darüber darf höchstens so
 * weit gezogen werden, bis der Bildrand die Kante des Fensters erreicht.
 */
export function clampOffset(offset: number, scale: number, size: number): number {
  const limit = (size * (scale - 1)) / 2

  return Math.min(Math.max(offset, -limit), limit)
}

/**
 * Fingerposition → Position auf dem Monitor, jeweils 0..1.
 *
 * Das Rechteck ist das des Canvas <em>nach</em> Zoom und Verschiebung — der
 * Browser rechnet die Transformation also schon für uns aus.
 */
export function toNormalized(point: Point, rect: { left: number; top: number; width: number; height: number }): Point {
  if (rect.width <= 0 || rect.height <= 0) {
    return { x: 0, y: 0 }
  }

  return {
    x: clampUnit((point.x - rect.left) / rect.width),
    y: clampUnit((point.y - rect.top) / rect.height),
  }
}

/**
 * Wandelt zurückgelegten Fingerweg in ganze Scroll-Rastungen um und gibt den
 * Rest zurück, der noch keine Rastung ergeben hat.
 */
export function scrollNotches(
  accumulated: number,
  step = SCROLL_STEP_PX,
): { notches: number; rest: number } {
  const notches = Math.trunc(accumulated / step)

  return { notches, rest: accumulated - notches * step }
}

/**
 * Neuer Ausschnitt nach einer Zwei-Finger-Geste.
 *
 * Beim Zoomen bleibt der Punkt zwischen den Fingern stehen: verschiebt man das
 * Bild nicht mit, wandert unter den Fingern etwas völlig anderes durchs Bild.
 */
export function applyPinch(
  viewport: Viewport,
  factor: number,
  focus: Point,
  container: { width: number; height: number },
): Viewport {
  const scale = clampScale(viewport.scale * factor)
  const applied = scale / viewport.scale

  // Abstand des Fingerpunkts zur Mitte — um genau diesen Faktor wächst er beim
  // Zoomen mit.
  const dx = focus.x - container.width / 2
  const dy = focus.y - container.height / 2

  return {
    scale,
    offsetX: clampOffset(
      viewport.offsetX * applied + dx * (1 - applied), scale, container.width),
    offsetY: clampOffset(
      viewport.offsetY * applied + dy * (1 - applied), scale, container.height),
  }
}

/**
 * Wofür eine Zwei-Finger-Geste gehalten wird. `undecided` heißt: noch hat sich
 * weder der Abstand noch die Mitte weit genug bewegt, um sich festzulegen.
 */
export type TwoFingerMode = 'undecided' | 'zoom' | 'scroll'

/** Ab so viel senkrechter Bewegung zweier Finger ist die Geste ein Scrollen. */
export const SCROLL_DECIDE_PX = 16

/**
 * Entscheidet einmalig, ob zwei Finger zoomen oder scrollen.
 *
 * Beide Werte werden **seit dem Aufsetzen** gemessen, nicht von einem Ereignis
 * zum nächsten: pro Ereignis bewegen sich die Finger nur wenige Pixel, und
 * damit war die Zoom-Schwelle praktisch nie erreichbar — jede Kneifbewegung
 * landete beim Scrollen.
 *
 * Verglichen wird zusätzlich, was von beidem überwiegt. Wer kneift, verschiebt
 * dabei immer auch die Mitte ein Stück; ohne diesen Vergleich gewänne das
 * Scrollen allein deshalb, weil es die niedrigere Schwelle hat.
 *
 * Einmal festgelegt bleibt es dabei, bis die Finger wieder hochgehen — sonst
 * kippt eine Geste mitten im Scrollen ins Zoomen.
 */
export function decideTwoFinger(
  current: TwoFingerMode,
  spreadChange: number,
  centerShiftY: number,
): TwoFingerMode {
  if (current !== 'undecided') {
    return current
  }

  const pinched = Math.abs(spreadChange)
  const scrolled = Math.abs(centerShiftY)

  if (pinched > PINCH_THRESHOLD_PX && pinched >= scrolled) {
    return 'zoom'
  }

  return scrolled > SCROLL_DECIDE_PX ? 'scroll' : 'undecided'
}

/** Größe eines Elements in CSS-Pixeln, ohne Zoom gerechnet. */
export interface Size {
  width: number
  height: number
}

/**
 * Neue Zeigerposition (jeweils 0..1) nach einem Wischer über `dx`/`dy`
 * CSS-Pixel im Zeiger-Overlay.
 *
 * Gerechnet wird auf dem <em>angezeigten</em> Bild: bei doppeltem Zoom legt
 * derselbe Fingerweg nur den halben Weg auf dem Monitor zurück. Genau das macht
 * das Hineinzoomen erst nützlich.
 */
export function movePointer(
  pointer: Point,
  dx: number,
  dy: number,
  media: Size,
  scale: number,
): Point {
  if (media.width <= 0 || media.height <= 0 || scale <= 0) {
    return pointer
  }

  return {
    x: clampUnit(pointer.x + dx / (media.width * scale)),
    y: clampUnit(pointer.y + dy / (media.height * scale)),
  }
}

/** Bildausschnitt samt Stelle, an der die Zeigermarke zu zeichnen ist. */
export interface PointerFocus {
  viewport: Viewport
  /** Position der Marke in Koordinaten der Bühne (linke obere Ecke = 0/0). */
  marker: Point
}

/**
 * Schiebt den Ausschnitt so, dass der Zeiger in der Mitte der Bühne steht.
 *
 * Am Bildrand geht das nicht mehr auf — dort bleibt der Ausschnitt stehen und
 * die Marke wandert stattdessen aus der Mitte heraus. Ohne diese Begrenzung
 * würde man beim Anfahren einer Ecke schwarze Ränder ins Bild ziehen.
 */
export function focusPointer(
  pointer: Point,
  scale: number,
  media: Size,
  stage: Size,
): PointerFocus {
  const offsetX = focusAxis(pointer.x, scale, media.width, stage.width)
  const offsetY = focusAxis(pointer.y, scale, media.height, stage.height)

  return {
    viewport: { scale, offsetX, offsetY },
    marker: {
      x: stage.width / 2 + (pointer.x - 0.5) * media.width * scale + offsetX,
      y: stage.height / 2 + (pointer.y - 0.5) * media.height * scale + offsetY,
    },
  }
}

/**
 * Verschiebung einer Achse, damit der Zeiger mittig steht — begrenzt auf das,
 * was das Bild hergibt. Passt das Bild in die Bühne, gibt es nichts zu
 * verschieben.
 */
function focusAxis(position: number, scale: number, mediaSize: number, stageSize: number): number {
  const wanted = (0.5 - position) * mediaSize * scale
  const limit = Math.max((mediaSize * scale - stageSize) / 2, 0)

  return Math.min(Math.max(wanted, -limit), limit)
}

export function panBy(
  viewport: Viewport,
  dx: number,
  dy: number,
  container: { width: number; height: number },
): Viewport {
  return {
    scale: viewport.scale,
    offsetX: clampOffset(viewport.offsetX + dx, viewport.scale, container.width),
    offsetY: clampOffset(viewport.offsetY + dy, viewport.scale, container.height),
  }
}

function clampUnit(value: number): number {
  return Math.min(Math.max(value, 0), 1)
}
