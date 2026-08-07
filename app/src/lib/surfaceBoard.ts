import { deviceLabel } from './deviceNames.ts'
import type { AgentActionSummary, Device } from './types.ts'
import { findWakeCandidate } from './wake.ts'

/**
 * Der Steckbrief für die nativen Flächen — Widget, Quick-Settings-Tile und die
 * App-Kürzel am Startbildschirm.
 *
 * Diese Flächen laufen **ohne die App**: wenn jemand das Widget antippt, gibt
 * es keine WebView, kein React und keinen Speicher, aus dem sich etwas lesen
 * ließe. Der native Teil braucht deshalb alles, was er zum Auslösen braucht, an
 * einer Stelle beisammen. Genau das steht hier drin.
 *
 * Was **nicht** hier steht: der private Geräteschlüssel. Er bleibt, wo er ist —
 * eine zweite Kopie desselben Geheimnisses in einer zweiten Ablage wäre die
 * Sorte Bequemlichkeit, die man später bereut.
 */

/** Ein Knoten, den der native Teil ansprechen kann. */
export interface SurfaceNode {
  host: string
  port: number
  /** Nur gekoppelt: der native Teil weist sich mit dem Geräteschlüssel aus. */
  clientId: string
}

/** Ein Knopf auf dem Widget. Mehr als Kennung und Aufschrift braucht er nicht. */
export interface SurfaceAction {
  id: string
  label: string
}

export interface SurfaceBoard {
  deviceId: string
  deviceName: string
  node: SurfaceNode
  actions: SurfaceAction[]
  /**
   * Wie dieser Rechner geweckt würde. `via` ist der Bote, nicht das Ziel — ein
   * schlafender Rechner nimmt nichts entgegen.
   */
  wake?: {
    mac: string
    via: SurfaceNode
  }
}

/**
 * Stellt zusammen, was die Flächen über den gerade benutzten Rechner wissen
 * müssen.
 *
 * `undefined` heißt: für diesen Rechner gibt es keine Flächen. Das ist der Fall
 * bei einem Gerät, das noch über das alte geteilte Token läuft — der native Teil
 * meldet sich ausschließlich mit dem Geräteschlüssel an, und ein Token, das für
 * alles gilt, gehört nicht in ein Widget.
 */
export function buildSurfaceBoard(
  device: Device,
  actions: readonly AgentActionSummary[],
  nodes: readonly Device[],
): SurfaceBoard | undefined {
  const node = toNode(device)

  if (node === undefined) {
    return undefined
  }

  const via = wakeCandidate(device, nodes)

  return {
    deviceId: device.id,
    // Der Name, den dieses Gerät vergeben hat — auf dem Homescreen soll
    // dasselbe stehen wie in der App.
    deviceName: deviceLabel(device),
    node,
    actions: actions.filter((action) => !action.confirm).map(({ id, label }) => ({ id, label })),
    ...(via === undefined || device.mac === undefined ? {} : { wake: { mac: device.mac, via } }),
  }
}

/**
 * Wer diesen Rechner wecken könnte — **ohne** zu prüfen, wer gerade antwortet.
 *
 * Beim Zusammenstellen ist das die falsche Frage: der Steckbrief wird
 * geschrieben, solange der Rechner läuft, und benutzt wird er womöglich Tage
 * später. Ob der Bote dann erreichbar ist, prüft die Fläche selbst, kurz bevor
 * sie den Knopf anbietet.
 */
function wakeCandidate(target: Device, nodes: readonly Device[]): SurfaceNode | undefined {
  const alle = new Set(nodes.map((node) => node.id))
  const candidate = findWakeCandidate(target, nodes, alle)

  return candidate === undefined ? undefined : toNode(candidate.device)
}

function toNode(device: Device): SurfaceNode | undefined {
  return device.clientId === undefined
    ? undefined
    : { host: device.host, port: device.port, clientId: device.clientId }
}
