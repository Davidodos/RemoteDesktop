import type { AgentInfo, Device } from './types.ts'

/**
 * Wer einen schlafenden Rechner wecken kann.
 *
 * **Wecken ist eine Eigenschaft des Netzes, in dem der Rechner steht — nicht
 * des Rechners.** Ein schlafender Rechner führt keine Software aus; sein
 * Netzwerkchip lauscht auf ein Ethernet-Frame, und das kommt über keinen
 * Router. Also muss ein waches Gerät **im selben Netz** es aussenden.
 *
 * Die Zuordnung soll sich von selbst ergeben — niemand soll auswählen müssen,
 * welcher Waker gerade zuständig ist. Sie tut es, weil jeder Knoten meldet, in
 * welchem Netz er steht (`siteId`, siehe `waker/README.md`).
 */

/** Ein Knoten, der ein Magic Packet aussenden könnte. */
export interface WakeCandidate {
  device: Device
  /** Warum gerade dieser — nur für die Erklärung in der Oberfläche. */
  reason: 'waker' | 'agent'
}

/**
 * Sucht den Knoten, der das gemeinte Gerät wecken kann.
 *
 * Reihenfolge, wenn mehrere passen: erst ein Waker (der läuft durch), dann ein
 * wacher Agent am selben Ort. Ein Gerät weckt sich nicht selbst — es schläft
 * ja gerade.
 *
 * @param online Kennungen der Geräte, von denen bekannt ist, dass sie antworten.
 *   Ein Knoten, der selbst nicht erreichbar ist, sendet auch nichts.
 */
export function findWakeCandidate(
  target: Device,
  nodes: readonly Device[],
  online: ReadonlySet<string>,
): WakeCandidate | undefined {
  if (target.mac === undefined || target.siteId === undefined) {
    return undefined
  }

  const passend = nodes.filter(
    (node) =>
      node.id !== target.id &&
      node.canWake &&
      node.siteId === target.siteId &&
      online.has(node.id),
  )

  const waker = passend.find((node) => node.waker === true)

  if (waker !== undefined) {
    return { device: waker, reason: 'waker' }
  }

  const agent = passend.find((node) => node.waker !== true)

  return agent === undefined ? undefined : { device: agent, reason: 'agent' }
}

/**
 * Warum der Weckknopf nicht da ist.
 *
 * Ausgegraut mit Begründung statt eines Fehlers beim Draufdrücken: die meisten
 * Nutzer haben genau einen Rechner und damit niemanden im Netz, der ihn wecken
 * könnte. Das ist kein Fehler, sondern der Normalfall.
 */
export function explainMissingCandidate(target: Device): string {
  if (target.mac === undefined || target.siteId === undefined) {
    return (
      `Von ${target.name} ist noch nicht bekannt, wo er steht. ` +
      'Einmal verbinden, solange er läuft — danach kann er geweckt werden.'
    )
  }

  return (
    `In ${target.name}s Netz ist gerade kein Gerät erreichbar, das ihn wecken könnte. ` +
    'Ein Magic Packet kommt über keinen Router — es braucht einen wachen Rechner ' +
    'oder den Waker vor Ort.'
  )
}

/**
 * Übernimmt Standort und MAC aus der Auskunft eines wachen Agents.
 *
 * Das ist der einzige Zeitpunkt, an dem beides zu haben ist. Fehlt in der
 * Auskunft etwas — ein älterer Agent kennt die Felder nicht —, bleibt der alte
 * Wert stehen: eine Kennung von gestern ist besser als keine.
 */
export function rememberSite(device: Device, info: AgentInfo): Device {
  const mac = info.mac ?? device.mac
  const siteId = info.siteId ?? device.siteId

  return {
    ...device,
    ...(mac === undefined ? {} : { mac }),
    ...(siteId === undefined ? {} : { siteId }),
    canWake: info.canWake ?? device.canWake,
  }
}

/** Ob sich an dem, was gemerkt wird, überhaupt etwas geändert hat. */
export function siteChanged(device: Device, updated: Device): boolean {
  return (
    device.mac !== updated.mac ||
    device.siteId !== updated.siteId ||
    device.canWake !== updated.canWake
  )
}
