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

/**
 * Ein brauchbarer Vorschlag für den eigenen Namen, aus dem, was schon dasteht.
 *
 * Bei `pc.tailnet-1234.ts.net` ist das `pc`: der vordere Teil ist der, den
 * jemand wiedererkennt, der Rest ist Verwaltung. Bei einer IP-Adresse gibt es
 * nichts zu kürzen — dort bleibt sie stehen, bis der Rechner seinen echten
 * Namen meldet.
 */
export function suggestAlias(host: string): string {
  const trimmed = host.trim()

  // Eine IPv4-Adresse hat auch Punkte, meint damit aber nichts Abkürzbares.
  if (/^[\d.]+$/.test(trimmed) || trimmed.includes(':')) {
    return trimmed
  }

  const [first] = trimmed.split('.')

  return first !== undefined && first.length > 0 ? first : trimmed
}
