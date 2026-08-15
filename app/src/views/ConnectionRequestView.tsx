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

  /**
   * Ob die Aufnahme schon läuft. `undefined`, solange es niemand wissen kann —
   * dann wird nicht nachgefragt, statt einen Systemdialog auf Verdacht zu
   * öffnen.
   */
  const [sharing, setSharing] = useState<boolean | undefined>(undefined)

  useEffect(() => {
    if (!host.available) {
      return
    }

    return host.onRequests(setRequests)
  }, [host])

  const current = requests[0]

  // Nachgesehen wird, sobald eine Frage ansteht, und nicht im Takt: der Stand
  // kann sich zwischen zwei Anfragen geändert haben — Android nimmt die
  // Erlaubnis beim Neustart des Geräts zurück.
  useEffect(() => {
    if (!host.available || current === undefined) {
      return
    }

    void host.status().then(
      (status) => setSharing(status.sharingScreen === true),
      () => setSharing(undefined),
    )
  }, [host, current])

  if (current === undefined) {
    return null
  }

  /**
   * Zulassen heißt: jetzt, für diese eine Verbindung — und erst jetzt fragt
   * Android nach der Aufnahme.
   *
   * <p>
   * **Der Befund dahinter:** die Aufnahme wurde vorher auf der Freigabeseite
   * im Voraus erteilt, also lange bevor jemand danach fragte. Damit lag auf dem
   * Handy eine offene Erlaubnis herum, die niemand mehr im Kopf hatte — und der
   * Systemdialog kam zu einem Zeitpunkt, an dem die Frage „wozu eigentlich?"
   * nicht zu beantworten war. Jetzt steht sie neben dem Namen dessen, der
   * gerade anfragt.
   * </p>
   *
   * <p>
   * Ein abgelehnter Systemdialog beendet die Verbindung nicht: wer „Zulassen"
   * getippt hat, hat zugelassen. Der Host sagt dem Gegenüber dann, dass es
   * dieses Gerät bedienen, aber nicht ansehen kann.
   * </p>
   */
  const answer = (allow: boolean): void => {
    // Die Liste kommt vom Zuhörer und wird hier nicht selbst gepflegt: die
    // native Seite meldet, wenn eine Frage erledigt ist — auch dann, wenn sie
    // abgelaufen ist, während jemand noch zielte.
    const settle = (): void => void host.answer(current.id, allow).catch(() => undefined)

    if (!allow || sharing !== false) {
      settle()

      return
    }

    // Erst der Systemdialog, dann die Antwort. Andersherum liefe die Verbindung
    // an, während die Aufnahme noch nicht steht — und das Gegenüber sähe ein
    // schwarzes Bild, das sich von einer hängenden Verbindung nicht
    // unterscheiden lässt.
    void host.enableScreen().then(settle, settle)
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

        {sharing === false && (
          <p className="settings-hint">
            Für das Bild fragt Android gleich noch einmal nach. Lehnst du das ab,
            kommt die Verbindung trotzdem zustande — dieses Gerät lässt sich dann
            bedienen, aber nicht ansehen.
          </p>
        )}

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
