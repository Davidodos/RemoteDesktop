import { useCallback, useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { getPlatform } from '../platform/index.ts'
import type { HostPairingCode, HostStatus } from '../platform/index.ts'

/** Wie oft die verbleibende Gültigkeit des Codes nachgerechnet wird. */
const TICK_MS = 1000

/**
 * Die andere Hälfte des Koppelns: **dieses** Gerät anbieten.
 *
 * <p>
 * Ein Code, ein QR-Code und die eigene Adresse — mehr braucht die Gegenseite
 * nicht. Die Adresse steht neben dem QR-Code und nicht statt seiner: wer von
 * einem Rechner aus koppelt, hat keine Kamera und tippt beides ab.
 * </p>
 *
 * <p>
 * **Der Code verschwindet auf vier Wegen** — Ablauf (der Countdown steht
 * dabei), Benutzung, ein Knopf daneben und das Verlassen der Seite. Ein Code,
 * der noch dasteht, wenn er nicht mehr gilt, wird abgetippt und scheitert ohne
 * erkennbaren Grund.
 * </p>
 *
 * <p>
 * **Was hier nicht mehr steht:** die Liste „wer dieses Gerät steuern darf" und
 * der Fingerabdruck der eigenen Stelle. Die Liste sagte dasselbe wie die
 * Geräteliste, nur an zweiter Stelle und mit einem zweiten Knopf zum Entfernen;
 * der Fingerabdruck stand zum Vergleichen da und wurde nie verglichen.
 * </p>
 */
export function PairingOffer(): React.JSX.Element {
  const host = getPlatform().host

  const [status, setStatus] = useState<HostStatus | undefined>(undefined)
  const [pairing, setPairing] = useState<HostPairingCode | undefined>(undefined)
  const [remaining, setRemaining] = useState(0)
  const [qr, setQr] = useState<string | undefined>(undefined)
  const [error, setError] = useState<string | undefined>(undefined)

  const hide = useCallback((): void => {
    setPairing(undefined)
    setQr(undefined)
  }, [])

  const refresh = useCallback((): void => {
    void host.status().then(setStatus, () => undefined)
  }, [host])

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
    return <p className="settings-hint">Im Browser lässt sich dieses Gerät nicht anbieten.</p>
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
            ? 'Erst freigeben — der Code kommt vom laufenden Server. Einstellungen → Dieses Gerät freigeben.'
            : 'Der Agent läuft nicht. Nur er gibt Codes aus. Starten unter „Übersicht“.'}
        </p>
      )}

      {running && pairing === undefined && (
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
      )}

      {running && pairing !== undefined && (
        <>
          <p className="pairing-code">{pairing.code}</p>
          <p className="settings-hint">Noch {remaining} Sekunden gültig.</p>

          {qr !== undefined && (
            <img className="pairing-qr" src={qr} alt="QR-Code zur Kopplung" />
          )}

          <button type="button" className="settings-entry" onClick={hide}>
            <span>Ausblenden</span>
          </button>
        </>
      )}

      {/* Die Adresse steht auch ohne Code da: sie beschreibt, wie dieses Gerät
          erreichbar wäre, und wer von Hand koppelt, braucht sie zuerst. */}
      {address === undefined ? (
        <p className="settings-hint">Noch keine Adresse im Netz.</p>
      ) : (
        <>
          <p className="settings-hint">Adresse für die Gegenseite:</p>
          <p className="pairing-code address">{address}</p>
        </>
      )}
    </>
  )
}
