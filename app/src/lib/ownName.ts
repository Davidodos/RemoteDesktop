import { useCallback, useEffect, useState } from 'react'
import { getPlatform } from '../platform/index.ts'
import type { IdentityState } from '../platform/index.ts'

/**
 * Wie dieses Gerät heißt — einmal gewählt, bei jeder Kopplung benutzt.
 *
 * <p>
 * **Der Befund dahinter:** der Name wurde bei *jeder* Kopplung neu eingetippt,
 * einmal für die eigene Seite und einmal für die andere. Zwei Felder in einem
 * Ablauf, in dem sonst nichts zu entscheiden ist — und wer drei Geräte
 * koppelte, tippte denselben Namen dreimal, beim dritten Mal anders.
 * </p>
 *
 * <p>
 * Der Wert liegt nativ (siehe `platform/identity.ts`), nicht im Speicher der
 * Seite: er steht in `/api/info`, und das beantwortet ein Gerät auch dann, wenn
 * keine Oberfläche offen ist.
 * </p>
 */
export async function ownName(): Promise<string> {
  const { name } = await getPlatform().identity.read()

  return name
}

/** Länger nennt sich kein Gerät — wie `DeviceNameFile.MaxLength`. */
export const MAX_NAME_LENGTH = 64

/**
 * Was von einem eingetippten Namen übrig bleibt. Dieselben Regeln wie auf
 * beiden Gegenseiten: getrimmt, gekürzt, und leer heißt „kein Name".
 */
export function cleanName(name: string): string {
  return name.replace(/[\p{Cc}\p{Cf}]/gu, '').trim().slice(0, MAX_NAME_LENGTH).trim()
}

/**
 * Der Stand, wie die Oberfläche ihn braucht: der Name, ob ihn jemand gewählt
 * hat, und ob der Erststart durch ist.
 *
 * `undefined`, solange die Antwort noch nicht da ist — die Ansichten
 * unterscheiden das ausdrücklich von „kein Name": eine Erststartfrage, die für
 * einen Bilddurchlauf aufblitzt, wäre schlimmer als eine, die kurz auf sich
 * warten lässt.
 */
export function useIdentity(): {
  state: IdentityState | undefined
  rename: (name: string) => Promise<void>
  finishFirstRun: () => Promise<void>
} {
  const [state, setState] = useState<IdentityState | undefined>(undefined)

  const load = useCallback((): void => {
    void getPlatform().identity.read().then(setState, () => undefined)
  }, [])

  useEffect(load, [load])

  const rename = useCallback(
    async (name: string): Promise<void> => {
      await getPlatform().identity.rename(cleanName(name))
      load()
    },
    [load],
  )

  const finishFirstRun = useCallback(async (): Promise<void> => {
    await getPlatform().identity.finishFirstRun()
    load()
  }, [load])

  return { state, rename, finishFirstRun }
}
