import type { SurfaceBoard } from '../lib/surfaceBoard.ts'

/**
 * Die Flächen, die die Umgebung außerhalb der App anbietet.
 *
 * Unter Android sind das Widget, Quick-Settings-Tile und die Kürzel am
 * App-Symbol. Im Browser und im Windows-Fenster gibt es dergleichen nicht —
 * dort passiert hier schlicht nichts, wie beim Vordergrunddienst auch.
 */
export interface SurfaceBoardPublisher {
  /**
   * Reicht den Steckbrief nach draußen. `undefined` räumt die Flächen ab: ein
   * Widget, das auf ein entkoppeltes Gerät zeigt, ist schlimmer als keins.
   */
  publish(board: SurfaceBoard | undefined): Promise<void>
}

/** Die Umgebung hat keine solchen Flächen. */
export const noSurfaces: SurfaceBoardPublisher = {
  publish: (): Promise<void> => Promise.resolve(),
}
