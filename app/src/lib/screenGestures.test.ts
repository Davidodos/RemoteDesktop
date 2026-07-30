import { describe, expect, test } from 'vitest'
import {
  MAX_ZOOM,
  MIN_ZOOM,
  applyPinch,
  clampOffset,
  clampScale,
  decideTwoFinger,
  distance,
  focusPointer,
  isTap,
  midpoint,
  movePointer,
  panBy,
  scrollNotches,
  toNormalized,
} from './screenGestures.ts'

const STAGE = { width: 400, height: 800 }

describe('Fingerposition auf Monitorkoordinaten abbilden', () => {
  const rect = { left: 20, top: 100, width: 360, height: 200 }

  test('Mitte des Bildes ergibt 0.5/0.5', () => {
    // Act
    const point = toNormalized({ x: 200, y: 200 }, rect)

    // Assert
    expect(point).toEqual({ x: 0.5, y: 0.5 })
  })

  test('linke obere Ecke ergibt 0/0', () => {
    expect(toNormalized({ x: 20, y: 100 }, rect)).toEqual({ x: 0, y: 0 })
  })

  test('rechte untere Ecke ergibt 1/1', () => {
    expect(toNormalized({ x: 380, y: 300 }, rect)).toEqual({ x: 1, y: 1 })
  })

  test('außerhalb liegende Finger werden auf den Rand geklemmt', () => {
    // Arrange — beim Wischen über die Kante liefert der Browser solche Werte.
    const point = toNormalized({ x: -500, y: 5000 }, rect)

    // Assert
    expect(point).toEqual({ x: 0, y: 1 })
  })

  test('ein Rechteck ohne Ausdehnung führt nicht zu NaN', () => {
    // Assert — sonst schickt die App NaN an den Agent.
    expect(toNormalized({ x: 10, y: 10 }, { left: 0, top: 0, width: 0, height: 0 }))
      .toEqual({ x: 0, y: 0 })
  })
})

describe('Tippen von Wischen unterscheiden', () => {
  test('unbewegter Finger ist ein Tippen', () => {
    expect(isTap({ x: 100, y: 100 }, { x: 100, y: 100 })).toBe(true)
  })

  test('leichtes Zittern gilt noch als Tippen', () => {
    expect(isTap({ x: 100, y: 100 }, { x: 104, y: 103 })).toBe(true)
  })

  test('deutliche Bewegung ist kein Tippen mehr', () => {
    expect(isTap({ x: 100, y: 100 }, { x: 160, y: 100 })).toBe(false)
  })
})

describe('Zoomgrenzen', () => {
  test('unter die Originalgröße geht es nicht', () => {
    expect(clampScale(0.2)).toBe(MIN_ZOOM)
  })

  test('nach oben ist bei der Obergrenze Schluss', () => {
    expect(clampScale(99)).toBe(MAX_ZOOM)
  })

  test('Werte dazwischen bleiben unverändert', () => {
    expect(clampScale(2.5)).toBe(2.5)
  })
})

describe('Verschiebung begrenzen', () => {
  test('ohne Zoom gibt es nichts zu verschieben', () => {
    expect(clampOffset(120, 1, 400)).toBe(0)
  })

  test('bei doppeltem Zoom ist die halbe Breite die Grenze', () => {
    expect(clampOffset(9999, 2, 400)).toBe(200)
    expect(clampOffset(-9999, 2, 400)).toBe(-200)
  })

  test('innerhalb der Grenze bleibt der Wert erhalten', () => {
    expect(clampOffset(50, 2, 400)).toBe(50)
  })
})

describe('Ausschnitt verschieben', () => {
  test('Verschiebung addiert sich', () => {
    // Arrange
    const viewport = { scale: 2, offsetX: 10, offsetY: 10 }

    // Act
    const moved = panBy(viewport, 20, -5, STAGE)

    // Assert
    expect(moved).toEqual({ scale: 2, offsetX: 30, offsetY: 5 })
  })

  test('am Bildrand ist Schluss', () => {
    // Act
    const moved = panBy({ scale: 2, offsetX: 0, offsetY: 0 }, 5000, 5000, STAGE)

    // Assert
    expect(moved.offsetX).toBe(STAGE.width / 2)
    expect(moved.offsetY).toBe(STAGE.height / 2)
  })
})

describe('Zoomen mit zwei Fingern', () => {
  const center = { x: STAGE.width / 2, y: STAGE.height / 2 }

  test('Auseinanderziehen vergrößert', () => {
    // Act
    const zoomed = applyPinch({ scale: 1, offsetX: 0, offsetY: 0 }, 2, center, STAGE)

    // Assert
    expect(zoomed.scale).toBe(2)
  })

  test('in der Mitte gezoomt bleibt der Ausschnitt zentriert', () => {
    // Act
    const zoomed = applyPinch({ scale: 1, offsetX: 0, offsetY: 0 }, 2, center, STAGE)

    // Assert
    expect(zoomed.offsetX).toBe(0)
    expect(zoomed.offsetY).toBe(0)
  })

  test('abseits der Mitte wandert der Ausschnitt zum Finger', () => {
    // Arrange — Zoom auf den linken Rand.
    const focus = { x: 0, y: center.y }

    // Act
    const zoomed = applyPinch({ scale: 1, offsetX: 0, offsetY: 0 }, 2, focus, STAGE)

    // Assert — das Bild rückt nach rechts, damit links mehr sichtbar wird.
    expect(zoomed.offsetX).toBeGreaterThan(0)
  })

  test('die Obergrenze gilt auch beim Zoomen', () => {
    // Act
    const zoomed = applyPinch({ scale: MAX_ZOOM, offsetX: 0, offsetY: 0 }, 3, center, STAGE)

    // Assert
    expect(zoomed.scale).toBe(MAX_ZOOM)
  })

  test('vollständiges Herauszoomen zentriert wieder', () => {
    // Act
    const zoomed = applyPinch({ scale: 2, offsetX: 150, offsetY: 0 }, 0.1, center, STAGE)

    // Assert
    expect(zoomed.scale).toBe(MIN_ZOOM)
    expect(zoomed.offsetX).toBe(0)
  })
})

describe('Scrollen in Rastungen umrechnen', () => {
  test('kurzer Weg ergibt noch keine Rastung', () => {
    // Act
    const { notches, rest } = scrollNotches(10, 24)

    // Assert
    expect(notches).toBe(0)
    expect(rest).toBe(10)
  })

  test('der Rest wird für die nächste Bewegung aufgehoben', () => {
    // Act
    const { notches, rest } = scrollNotches(30, 24)

    // Assert
    expect(notches).toBe(1)
    expect(rest).toBe(6)
  })

  test('nach oben wischen ergibt negative Rastungen', () => {
    // Act
    const { notches } = scrollNotches(-50, 24)

    // Assert
    expect(notches).toBe(-2)
  })

  test('angesammelte Reste ergeben irgendwann eine Rastung', () => {
    // Arrange — zehn Bewegungen von je 5 px.
    let rest = 0
    let total = 0

    // Act
    for (let i = 0; i < 10; i++) {
      const result = scrollNotches(rest + 5, 24)
      rest = result.rest
      total += result.notches
    }

    // Assert
    expect(total).toBe(2)
  })
})

describe('Hilfsrechnungen', () => {
  test('Abstand zweier Finger', () => {
    expect(distance({ x: 0, y: 0 }, { x: 3, y: 4 })).toBe(5)
  })

  test('Punkt zwischen zwei Fingern', () => {
    expect(midpoint({ x: 0, y: 0 }, { x: 10, y: 20 })).toEqual({ x: 5, y: 10 })
  })
})

describe('Zwei-Finger-Geste einordnen', () => {
  test('deutliches Auseinanderziehen ist ein Zoom', () => {
    expect(decideTwoFinger('undecided', 40, 0)).toBe('zoom')
  })

  test('gemeinsames Wandern nach unten ist ein Scrollen', () => {
    expect(decideTwoFinger('undecided', 2, 20)).toBe('scroll')
  })

  test('kleine Bewegungen legen sich noch nicht fest', () => {
    expect(decideTwoFinger('undecided', 3, 4)).toBe('undecided')
  })

  test('einmal entschieden bleibt es dabei', () => {
    // Arrange — beim Scrollen laufen die Finger fast immer etwas auseinander.
    expect(decideTwoFinger('scroll', 200, 0)).toBe('scroll')
  })
})

describe('Zeiger im Overlay bewegen', () => {
  const MEDIA = { width: 400, height: 225 }

  test('Wischen verschiebt anteilig zur Bildgröße', () => {
    // Act — 100 px nach rechts auf 400 px Bildbreite ohne Zoom.
    const next = movePointer({ x: 0.5, y: 0.5 }, 100, 0, MEDIA, 1)

    // Assert
    expect(next.x).toBeCloseTo(0.75)
    expect(next.y).toBeCloseTo(0.5)
  })

  test('bei doppeltem Zoom legt derselbe Weg nur die halbe Strecke zurück', () => {
    const next = movePointer({ x: 0.5, y: 0.5 }, 100, 0, MEDIA, 2)

    expect(next.x).toBeCloseTo(0.625)
  })

  test('am Bildrand ist Schluss', () => {
    const next = movePointer({ x: 0.9, y: 0.1 }, 1000, -1000, MEDIA, 1)

    expect(next).toEqual({ x: 1, y: 0 })
  })

  test('ohne bekannte Bildgröße bleibt der Zeiger stehen', () => {
    // Arrange — so lange das <video> noch keine Auflösung kennt.
    const pointer = { x: 0.3, y: 0.4 }

    expect(movePointer(pointer, 50, 50, { width: 0, height: 0 }, 1)).toEqual(pointer)
  })
})

describe('Ausschnitt dem Zeiger nachführen', () => {
  // Ein Bild, das doppelt so hoch ist wie die Bühne, wenn es gezoomt wird.
  const MEDIA = { width: 400, height: 400 }
  const FOCUS_STAGE = { width: 400, height: 400 }

  test('Zeiger in der Bildmitte braucht keine Verschiebung', () => {
    // Act
    const { viewport, marker } = focusPointer({ x: 0.5, y: 0.5 }, 2, MEDIA, FOCUS_STAGE)

    // Assert
    expect(viewport).toEqual({ scale: 2, offsetX: 0, offsetY: 0 })
    expect(marker).toEqual({ x: 200, y: 200 })
  })

  test('Zeiger abseits der Mitte zieht den Ausschnitt hinterher', () => {
    // Act — ein Viertel nach rechts, bei doppeltem Zoom sind das 200 px.
    const { viewport, marker } = focusPointer({ x: 0.75, y: 0.5 }, 2, MEDIA, FOCUS_STAGE)

    // Assert — die Marke bleibt trotzdem mittig.
    expect(viewport.offsetX).toBe(-200)
    expect(marker.x).toBe(200)
  })

  test('am Bildrand bleibt der Ausschnitt stehen und die Marke wandert', () => {
    // Act
    const { viewport, marker } = focusPointer({ x: 1, y: 0.5 }, 2, MEDIA, FOCUS_STAGE)

    // Assert — mehr als 200 px gibt das Bild nicht her, also läuft die Marke
    // die restlichen 200 px nach rechts an den Rand.
    expect(viewport.offsetX).toBe(-200)
    expect(marker.x).toBe(400)
  })

  test('passt das Bild in die Bühne, wird nichts verschoben', () => {
    // Arrange — kleines Bild, kein Zoom.
    const { viewport, marker } = focusPointer({ x: 0, y: 0 }, 1, { width: 200, height: 100 }, FOCUS_STAGE)

    // Assert
    expect(viewport).toEqual({ scale: 1, offsetX: 0, offsetY: 0 })
    expect(marker).toEqual({ x: 100, y: 150 })
  })
})

describe('Zoom von Scrollen trennen', () => {
  test('Kneifen mit leicht wanderndem Mittelpunkt bleibt ein Zoom', () => {
    // Arrange — zwei Finger laufen beim Kneifen nie exakt symmetrisch.
    expect(decideTwoFinger('undecided', 30, 20)).toBe('zoom')
  })

  test('überwiegt die senkrechte Bewegung, wird gescrollt', () => {
    // Assert — auch wenn die Finger dabei auseinanderlaufen.
    expect(decideTwoFinger('undecided', 30, 50)).toBe('scroll')
  })

  test('knapp unter der Zoom-Schwelle passiert noch nichts', () => {
    expect(decideTwoFinger('undecided', 20, 10)).toBe('undecided')
  })
})
