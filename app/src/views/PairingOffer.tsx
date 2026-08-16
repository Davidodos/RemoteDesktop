import { useCallback, useEffect, useRef, useState } from 'react'
import QRCode from 'qrcode'
import { readable } from '../lib/certificateTrust.ts'
import { getPlatform } from '../platform/index.ts'
import type { HostClient, HostPairingCode, HostStatus } from '../platform/index.ts'

/** Wie oft die verbleibende Gültigkeit des Codes nachgerechnet wird. */
const TICK_MS = 1000

/**
 * Die andere Hälfte des Koppelns: **dieses** Gerät anbieten.
 *
 * <p>
 * Ein Code, ein QR-Code und die eigene Adresse — mehr braucht die Gegenseite
 * nicht. Die Adresse steht ausdrücklich neben dem QR-Code und nicht statt
 * seiner: wer von einem Rechner aus koppelt, hat keine Kamera und tippt beides
 * ab.
 * </p>
 *
 * <p>
 * **Der Code verschwindet auf vier Wegen** — Ablauf (der Countdown steht
 * dabei), Benutzung, ein Knopf daneben und das Verlassen der Seite. Ein Code,
 * der noch dasteht, wenn er nicht mehr gilt, wird abgetippt und scheitert ohne
 * erkennbaren Grund; einer, der nach dem Einlösen stehen bleibt, sieht aus, als
 * ließe er sich noch einmal benutzen.
 * </p>
 *
 * <p>
 * Darunter steht, wer dieses Gerät steuern darf. Das gehört hierher und nicht
 * unter die Einstellungen: es ist dieselbe Frage wie die darüber, nur in der
 * Vergangenheitsform.
 * </p>
 */
export function PairingOffer(): React.JSX.Element {
  const host = getPlatform().host

  const [status, setStatus] = useState<HostStatus | undefined>(undefined)
  const [clients, setClients] = useState<HostClient[]>([])
  const [pairing, setPairing] = useState<HostPairingCode | undefined>(undefined)
  const [remaining, setRemaining] = useState(0)
  const [qr, setQr] = useState<string | undefined>(undefined)
  const [error, setError] = useState<string | undefined>(undefined)

  /** Wie viele Geräte beim letzten Nachsehen zugelassen waren. */
  const bekannt = useRef(0)

  const hide = useCallback((): void => {
    setPairing(undefined)
    setQr(undefined)
  }, [])

  const refresh = useCallback((): void => {
    void host.status().then(setStatus, () => undefined)

    void host.clients().then((fresh) => {
      // **Ein benutzter Code verschwindet.** Dass jemand ihn eingelöst hat,
      // sagt niemand ausdrücklich; es zeigt sich daran, dass die Liste um einen
      // Eintrag gewachsen ist.
      //
      // Gezählt wird in einem Ref und nicht im Zustand: ein Vergleich in einer
      // Zustandsfunktion müsste dort etwas anderes anstoßen, und die Funktion
      // soll rechnen und nichts auslösen.
      if (fresh.length > bekannt.current) {
        hide()
      }

      bekannt.current = fresh.length
      setClients(fresh)
    }, () => undefined)
  }, [host, hide])

  // Beim Öffnen einmal, danach im Takt: nur so fällt auf, dass der Code eben
  // benutzt wurde.
  useEffect(() => {
    refresh()

    const timer = window.setInterval(refresh, TICK_MS * 3)

    return () => window.clearInterval(timer)
  }, [refresh])

  // Der Code läuft nach fünf Minuten ab. Ohne die Anzeige steht er weiter da
  // und wird eingetippt, und die Kopplung scheitert ohne erkennbaren Grund.
  useEffect(() => {
    if (pairing === undefined) {
      return
    }

    const until = Date.now() + pairing.expiresInSeconds * 1000

    const tick = (): void => {
      const left = Math.max(0, Math.round((until - Date.now()) / 1000))

      setRemaining(left)

      if (left === 0) {
        hide()
      }
    }

    tick()

    const timer = window.setInterval(tick, TICK_MS)

    return () => window.clearInterval(timer)
  }, [pairing, hide])

  // Der QR-Code wird gezeichnet, sobald es ein Ziel gibt. Ohne Adresse gibt es
  // keins — dann bleibt der getippte Code der Weg, und der steht ohnehin da.
  useEffect(() => {
    const uri = pairing?.pairingUri

    if (uri === undefined) {
      setQr(undefined)
      return
    }

    void QRCode.toDataURL(uri, { margin: 1, width: 260 }).then(setQr, () => setQr(undefined))
  }, [pairing])

  if (!host.available) {
    return (
      <p className="settings-hint">
        Dieses Gerät lässt sich nicht anbieten — im Browser gibt es nichts, was
        eine Gegenstelle wäre. Von hier aus koppeln geht trotzdem: dafür ist der
        Bereich darunter da.
      </p>
    )
  }

  const running = status?.running === true
  const address =
    status !== undefined && status.addresses.length > 0
      ? `${status.addresses[0]}:${status.port}`
      : undefined

  return (
    <>
      {error !== undefined && <p className="error-text">{error}</p>}

      {!running && (
        <p className="settings-hint">
          {host.toggleable
            ? 'Dieses Gerät ist gerade nicht freigegeben. Einen Code gibt es erst, ' +
              'wenn es das ist — unter „Einstellungen → Dieses Gerät freigeben“.'
            : 'Der Agent läuft nicht. Einen Code kann nur er ausgeben und nur er ' +
              'einlösen — starten im Fenster unter „Übersicht“.'}
        </p>
      )}

      {running && pairing === undefined && (
        <>
          <p className="settings-hint">
            Der Code gilt fünf Minuten und lässt genau eine Kopplung zu.
          </p>
          <button
            type="button"
            className="settings-entry"
            onClick={() => {
              setError(undefined)
              void host.pairingCode().then(setPairing, (failure: unknown) => {
                setError(failure instanceof Error ? failure.message : String(failure))
              })
            }}
          >
            <span>Kopplungscode anzeigen</span>
          </button>
        </>
      )}

      {running && pairing !== undefined && (
        <>
          <p className="pairing-code">{pairing.code}</p>
          <p className="settings-hint">Noch {remaining} Sekunden gültig.</p>

          {qr === undefined ? (
            <p className="settings-hint">
              Für einen QR-Code fehlt die Adresse — abtippen geht trotzdem.
            </p>
          ) : (
            <img className="pairing-qr" src={qr} alt="QR-Code zur Kopplung" />
          )}

          <button type="button" className="settings-entry" onClick={hide}>
            <span>Code ausblenden</span>
          </button>
        </>
      )}

      {/* Die Adresse steht auch ohne Code da: sie beschreibt, wie dieses Gerät
          erreichbar wäre, und wer von Hand koppelt, braucht sie zuerst. */}
      <p className="settings-hint">
        {address === undefined
          ? 'Noch keine Adresse im Netz — ohne sie findet die Gegenseite dieses Gerät nicht.'
          : 'Wer von Hand koppelt, trägt drüben diese Adresse ein:'}
      </p>

      {address !== undefined && <p className="pairing-code address">{address}</p>}

      {status?.caFingerprint !== undefined && (
        <>
          <p className="settings-hint">
            Ohne QR-Code kommt kein Vergleichswert mit. Dann zeigt die Gegenseite
            den Fingerabdruck dieser Stelle an — er muss derselbe sein:
          </p>
          <p className="fingerprint">
            <small>{readable(status.caFingerprint)}</small>
          </p>
        </>
      )}

      <h2>Wer dieses Gerät steuern darf</h2>

      {clients.length === 0 ? (
        <p className="settings-hint">Noch niemand.</p>
      ) : (
        clients.map((client) => (
          <div key={client.id} className="settings-entry">
            <span>{client.label}</span>
            <button
              type="button"
              onClick={() => {
                void host.revoke(client.id).then(refresh, (failure: unknown) => {
                  setError(failure instanceof Error ? failure.message : String(failure))
                })
              }}
            >
              Entfernen
            </button>
          </div>
        ))
      )}
    </>
  )
}
