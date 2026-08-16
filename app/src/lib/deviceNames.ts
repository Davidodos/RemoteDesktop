import type { Device } from './types.ts'

/**
 * Wie ein Rechner in dieser App heißt.
 *
 * Zwei Namen, und beide sind richtig: der Rechner nennt sich selbst so, wie
 * Windows ihn nennt (`name`), und dieses Gerät darf ihm einen eigenen geben
 * (`alias`). Der eigene gewinnt, weil er der ist, den jemand ausgesucht hat.
 * Er steht nur hier — der Rechner erfährt davon nichts, und ein zweites Handy
 * hat seinen eigenen.
 */
export function deviceLabel(device: Device): string {
  const alias = device.alias?.trim()

  return alias !== undefined && alias.length > 0 ? alias : device.name
}
