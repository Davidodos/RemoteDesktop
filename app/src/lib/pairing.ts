import { postJson } from '../transport/direct.ts'
import { TransportError } from '../transport/index.ts'
import type { PeerCredential } from './bothWays.ts'
import { certificateFingerprint } from './certificateTrust.ts'
import { clientFingerprint, ensureClientKey } from './clientKey.ts'
import { type DeviceProfile } from '../platform/index.ts'
import type { Device } from './types.ts'

// Weitergereicht, weil `bothWays.ts` ihn von hier holt — er wohnt seit dem
// 16.08.2026 bei den übrigen Schlüsselsachen, damit auch der Transport an ihn
// herankommt, ohne einen Kreis zu schließen.
export { ensureClientKey }

/**
 * Die Kopplung: einmal pro Rechner den angezeigten Code eintippen, danach nie
 * wieder ein Geheimnis abtippen.
 *
 * Der Agent bekommt dabei nur den öffentlichen Schlüssel dieses Geräts. Wer
 * später eines der beiden Geräte übernimmt, hat damit noch keinen Zugang zum
 * anderen — das ist der ganze Grund, warum es kein geteiltes Token mehr gibt.
 */

/**
 * Was die Gegenseite nach erfolgreicher Kopplung zurückmeldet.
 *
 * Zwei Arten von Gegenstelle antworten hier: ein Agent nennt seinen Rechnernamen
 * und den Fingerabdruck seines Schlüssels, ein Waker nur seine Rolle und den
 * Standort. Deshalb ist außer `clientId` alles freiwillig.
 */
interface PairResponse {
  clientId: string
  scopes?: string[]
  hostname?: string
  agentFingerprint?: string
  /**
   * Fingerabdruck der Zertifizierungsstelle, mit der sich der Agent ausweist.
   *
   * Bei einem Agent mit Zertifikat von Tailscale kommt hier ausdrücklich
   * `null` — es gibt dann nichts zu bestätigen. Deshalb steht `| null` im Typ
   * und nicht nur das Fragezeichen: genau diese Unterscheidung ging schief,
   * siehe {@link fingerprintOf}.
   */
  caFingerprint?: string | null
  /**
   * Was die Gegenstelle ist — Rechner oder Handy. Sie sagt es hier und nicht
   * erst in `/api/info`, damit die Liste das Symbol auch dann zeigen kann, wenn
   * das Gerät gerade aus ist. Ein Agent älter als Phase 31g meldet es nicht;
   * dann steht dort nichts, statt „Windows" zu raten.
   */
  platform?: string
  /** `waker` bei einem Knoten, der nur wecken kann. */
  role?: string
  siteId?: string
  canWake?: boolean
  /**
   * Der Ausweis der **Oberfläche** der Gegenseite — ihr öffentlicher
   * Client-Schlüssel und ihr Name. Damit trägt dieses Gerät die andere Richtung
   * bei sich ein, ohne noch einmal ins Netz zu gehen. Ein Waker und ein Agent
   * älter als Phase 31e melden das Feld nicht; dann bleibt es bei einer
   * Richtung.
   */
  peer?: { name?: string; clientKey?: string }
}

export interface PairTarget {
  /** MagicDNS-Name des Rechners. */
  host: string
  port: number
  code: string
  /** Wie dieses Gerät in der Liste am Rechner heißen soll. */
  label: string
  /**
   * Der eigene Steckbrief. Geht mit, wenn dieses Gerät selbst ein mögliches
   * Ziel ist — siehe `platform/localNode.ts`. Die Gegenseite trägt ihn in ihre
   * eigene Geräteliste ein; ein zweiter Aufruf dafür entfällt.
   */
  self?: DeviceProfile
}

/** Was die Gegenseite braucht, um dieses Gerät später zu erreichen. */
export interface PairedBothWays {
  device: Device
  /**
   * Der Ausweis der Gegenseite — `undefined` nur, wenn sie gar keiner ist (ein
   * Waker, oder ein Agent älter als Phase 31e). Ist sie eine Gegenstelle, steht
   * hier ein Eintrag; ob er einen `clientKey` trägt, entscheidet über die
   * Gegenrichtung. Siehe `grantPeer`.
   */
  peer?: PeerCredential
}

/**
 * Koppelt mit einem Agent und liefert das fertige Gerät.
 *
 * Gespeichert wird hier noch nichts — das entscheidet der Aufrufer, der auch
 * weiß, was schon in der Liste steht.
 */
export async function pairWithAgent(target: PairTarget): Promise<Device> {
  return (await pairBothWays(target)).device
}

/**
 * Dasselbe, aber mit dem, was die Gegenseite für die Gegenrichtung mitgeschickt
 * hat. Getrennt, weil die meisten Aufrufer nur das Gerät wollen — und weil ein
 * Rückgabewert, den drei von vier Stellen wegwerfen, an keiner davon auffällt.
 */
export async function pairBothWays(target: PairTarget): Promise<PairedBothWays> {
  const key = await ensureClientKey()
  const base = `https://${target.host}:${target.port}`

  let response: PairResponse

  try {
    response = await postJson<PairResponse>(`${base}/api/pair`, {
      code: target.code.trim(),
      label: target.label.trim(),
      publicKey: key.publicKey,
      ...(target.self === undefined ? {} : { self: target.self }),
    })
  } catch (cause) {
    throw new PairingError(describeFailure(cause, target.host), { cause })
  }

  const waker = response.role === 'waker'

  // Geprüft wird der Wert und nicht seine Abwesenheit — siehe
  // `certificateFingerprint`. Ein `null` ist kein Fingerabdruck.
  const caFingerprint = certificateFingerprint(response.caFingerprint)

  const device: Device = {
    // Der Fingerabdruck des Agent-Schlüssels als Kennung: er bleibt gleich,
    // auch wenn der Rechner umbenannt wird oder eine andere Adresse bekommt.
    // Ein Waker hat keinen — dort tut es der Name, unter dem er erreichbar ist.
    id: response.agentFingerprint ?? target.host,
    // Ein Waker nennt keinen Rechnernamen; sein MagicDNS-Name ist das, was der
    // Nutzer eingetippt hat und wiedererkennt.
    name: response.hostname ?? target.host,
    host: target.host,
    port: target.port,
    clientId: response.clientId,
    ...(response.agentFingerprint === undefined
      ? {}
      : { fingerprint: response.agentFingerprint }),
    ...(caFingerprint === undefined ? {} : { caFingerprint }),
    ...(waker ? { waker: true } : {}),
    ...(response.siteId === undefined ? {} : { siteId: response.siteId }),
    ...(response.platform === 'windows' || response.platform === 'android'
      ? { platform: response.platform }
      : {}),
    // Jeder Agent kann seit Phase 14 Nachbarn wecken, ein Waker kann sonst
    // nichts. Ältere Agents melden das Feld nicht — dann eben nicht.
    canWake: response.canWake ?? false,
  }

  const clientKey = response.peer?.clientKey

  // Unter dieser Kennung steht die Gegenseite gleich in der eigenen Liste der
  // zugelassenen Geräte — ausgerechnet aus ihrem Ausweis, genau wie sie selbst
  // es tut. Ohne sie wüsste später niemand, welcher Eintrag zu welchem Gerät
  // gehört, und „Entfernen" ließe die Gegenrichtung stehen.
  if (typeof clientKey === 'string' && clientKey.length > 0) {
    device.peerClientId = await clientFingerprint(clientKey)
  }

  // Ein Waker und ein Agent älter als Phase 31e melden das Feld gar nicht — dann
  // gibt es keine Gegenrichtung, und das ist kein Fehler.
  //
  // Ein Agent, der `peer` schickt, dessen `clientKey` aber leer ist, ist etwas
  // anderes: dort läuft eine Gegenstelle, die ihren Ausweis nicht hinterlegt
  // hat. Beides in ein `undefined` zu falten war der Grund, warum eine
  // einseitige Kopplung wie eine gelungene aussah — siehe `grantPeer`.
  if (response.peer === undefined) {
    return { device }
  }

  return {
    device,
    peer: {
      name: response.peer.name ?? device.name,
      ...(typeof clientKey === 'string' && clientKey.length > 0 ? { clientKey } : {}),
    },
  }
}

export class PairingError extends Error {
  constructor(message: string, options: { cause?: unknown } = {}) {
    super(message, options)
    this.name = 'PairingError'
  }
}

/** Aus dem wortkargen Transportfehler wird hier ein Satz, der weiterhilft. */
function describeFailure(cause: unknown, host: string): string {
  if (!(cause instanceof TransportError)) {
    return cause instanceof Error ? cause.message : String(cause)
  }

  if (cause.status === undefined) {
    // Drei Ursachen sehen an dieser Stelle gleich aus: kein Rechner, kein Name,
    // kein Vertrauen. Die dritte ist die unangenehmste, weil der Rechner dabei
    // antwortet — deshalb steht sie mit dabei und nicht in einer Fußnote.
    return (
      `${host} antwortet nicht. Läuft der Agent auf diesem Rechner, und sind ` +
      'beide Geräte im selben Netz? Die Adresse muss genau so lauten wie im ' +
      'Fenster am Rechner. Läuft der Rechner ohne Tailscale, koppelt nur der ' +
      'QR-Code — er bringt das Zertifikat mit, das dieses Gerät vorher ' +
      'bestätigen muss.'
    )
  }

  return cause.serverMessage ?? `${host} antwortete mit HTTP ${cause.status}.`
}
