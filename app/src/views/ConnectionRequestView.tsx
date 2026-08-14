import { useEffect, useState } from 'react'
import { getPlatform } from '../platform/index.ts'
import type { ConnectionRequest } from '../platform/index.ts'

/**
 * „Darf dieses Gerät jetzt verbinden?" — die Karte, die alles andere verdeckt.
 *
 * <p>
 * **Warum sie über allem liegt.** Am anderen Ende wartet jemand vor einem
 * schwarzen Bild; die Frage hat etwa dreißig Sekunden, dann gilt sie als
 * abgelehnt. Sie in eine Seite zu legen, die man erst aufsuchen muss, hieße,
 * sie in den meisten Fällen ablaufen zu lassen.
 * </p>
 *
 * <p>
 * **Warum überhaupt gefragt wird.** Eine Kopplung ist eine Erlaubnis auf Dauer;
 * sie sagt, *wer* fragen darf. Sie sagt nicht, dass jetzt gerade jemand zusehen
 * darf. Ein Handy ist kein Rechner auf dem Schreibtisch — es liegt auf dem
 * Tisch, in der Tasche, neben dem Bett.
 * </p>
 *
 * <p>
 * Es gibt keinen Weg, die Karte wegzutippen: „Ablehnen" ist der Weg, und er
 * steht daneben. Ein versehentliches Wegwischen sähe sonst aus wie eine
 * Zustimmung, die niemand gegeben hat.
 * </p>
 */
export function ConnectionRequestView(): React.JSX.Element | null {
  const host = getPlatform().host

  const [requests, setRequests] = useState<ConnectionRequest[]>([])

  useEffect(() => {
    if (!host.available) {
      return
    }

    return host.onRequests(setRequests)
  }, [host])

  const current = requests[0]

  if (current === undefined) {
    return null
  }

  const answer = (allow: boolean): void => {
    // Die Liste kommt vom Zuhörer und wird hier nicht selbst gepflegt: die
    // native Seite meldet, wenn eine Frage erledigt ist — auch dann, wenn sie
    // abgelaufen ist, während jemand noch zielte.
    void host.answer(current.id, allow).catch(() => undefined)
  }

  return (
    <div className="request-overlay">
      <div className="token-prompt">
        <h1>Verbindung zulassen?</h1>
        <p>
          <strong>{current.label}</strong> möchte den Bildschirm dieses Geräts sehen
          und es bedienen.
        </p>
        <p className="settings-hint">
          Jede Verbindung wird einzeln bestätigt — auch von einem Gerät, das schon
          gekoppelt ist. Ohne Antwort gilt sie nach einer halben Minute als
          abgelehnt.
        </p>

        <button type="button" onClick={() => answer(true)}>
          Zulassen
        </button>

        <button type="button" className="secondary" onClick={() => answer(false)}>
          Ablehnen
        </button>

        {requests.length > 1 && (
          <p className="settings-hint">
            Danach warten noch {requests.length - 1} weitere Anfragen.
          </p>
        )}
      </div>
    </div>
  )
}
