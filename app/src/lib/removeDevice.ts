import { AgentClient } from './agentClient.ts'
import { forgetLocalDevice } from './deviceSources.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from './types.ts'

/**
 * Ein Gerät entfernen — **bei beiden**.
 *
 * <p>
 * Eine Kopplung besteht aus vier Stellen, und drei davon liegen hier: das Gerät
 * in der eigenen Liste, sein Ausweis in der eigenen `clients.json`, seine
 * Zertifizierungsstelle in den bestätigten Stellen. Die vierte liegt drüben —
 * dieses Gerät in *seiner* `clients.json` — und wird über `/api/unpair`
 * abgeräumt, mit dem eigenen Sitzungstoken.
 * </p>
 *
 * <p>
 * **Ist die Gegenseite nicht erreichbar, wird hier trotzdem entfernt.** Ein
 * Entfernen, das an einem ausgeschalteten Handy scheitert, wäre keins. Gesagt
 * wird es aber: drüben bleibt dann ein Eintrag stehen, der diesem Gerät weiter
 * Zugang gäbe — und wer das nicht weiß, sucht später nicht danach.
 * </p>
 */
export interface Removal {
  /** Die Liste ohne dieses Gerät. */
  devices: Device[]
  /**
   * Was drüben liegen blieb — `undefined`, wenn nichts liegen blieb. Ein Satz
   * für den Bildschirm, kein Fehlercode: hier ist nichts mehr zu tun, was die
   * App selbst tun könnte.
   */
  rest?: string
}

export async function removeDevice(device: Device): Promise<Removal> {
  // Zuerst drüben, solange es dieses Gerät dort noch gibt. Andersherum wäre der
  // eigene Eintrag weg — und mit ihm der Ausweis, mit dem sich dieses Gerät
  // drüben anmelden müsste, um sich auszutragen.
  const rest = await forgetThere(device)

  await forgetHere(device)

  return { devices: forgetLocalDevice(device.id), ...(rest === undefined ? {} : { rest }) }
}

/**
 * Dieses Gerät aus der Liste der Gegenseite nehmen.
 *
 * @returns Ein Satz, wenn es nicht geklappt hat.
 */
async function forgetThere(device: Device): Promise<string | undefined> {
  if (device.waker === true) {
    // Ein Waker führt keine Geräteliste; es gibt drüben nichts wegzuräumen.
    return undefined
  }

  try {
    await new AgentClient(device).unpair()

    return undefined
  } catch {
    return (
      `${device.name} war nicht erreichbar. Hier ist es entfernt — dort steht ` +
      'dieses Gerät weiter in der Liste und dürfte es steuern. Beim nächsten ' +
      'Mal, wenn beide Geräte an sind, dort noch einmal entfernen.'
    )
  }
}

/**
 * Alles, was auf dieser Seite an dem Gerät hing.
 *
 * Fehlschläge bleiben folgenlos: das Gerät verschwindet trotzdem aus der Liste.
 * Ein Entfernen, das an einem gestoppten Agent hängen bliebe, wäre keins — und
 * ein Eintrag ohne Gerät ist harmlos, solange niemand mehr auf ihn zeigt.
 */
async function forgetHere(device: Device): Promise<void> {
  const platform = getPlatform()

  if (device.peerClientId !== undefined) {
    await platform.node.revoke(device.peerClientId).catch(() => undefined)
  }

  if (device.caFingerprint !== undefined) {
    await platform.trust.forget?.(device.caFingerprint).catch(() => undefined)
  }
}
