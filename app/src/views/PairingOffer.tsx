import { useCallback, useEffect, useRef, useState } from 'react'
import QRCode from 'qrcode'
import { parsePairingUri } from '../lib/pairingUri.ts'
import { getPlatform } from '../platform/index.ts'
import type { HostClient, HostPairingCode } from '../platform/index.ts'

/** Wie oft die verbleibende Gültigkeit des Codes nachgerechnet wird. */
const TICK_MS = 1000

/** Wie oft nachgesehen wird, ob jemand den Code eingelöst hat. */
const WATCH_MS = 2000

/** Ein Code auf dem Bildschirm und alles, was an ihm hängt. */
export interface PairingCode {
  code: HostPairingCode | undefined
  /** Sekunden, bis er abläuft. */
  remaining: number
  /** Ob er abgelaufen ist — dann steht er nicht mehr da. */
  expired: boolean
  busy: boolean
  error: string | undefined
  /** Einen neuen holen; ein offener gilt danach nicht mehr. */
  renew: () => void
  /** Den offenen Code sofort ungültig machen. */
  cancel: () => void
}

/**
 * Der Kopplungscode dieses Geräts — einer für beide Wege.
 *
 * <p>
 * **Er lebt über den QR-Code und die Handeingabe hinweg.** Wer vom einen zum
 * anderen wechselt, bekommt denselben Code; ein neuer machte den ersten
 * ungültig, während ihn drüben vielleicht gerade jemand eintippt.
 * </p>
 *
 * <p>
 * **Eingelöst wird er drüben — gemerkt wird es hier** an der Clientliste: ein
 * Eintrag, der neu ist oder frisch gekoppelt (`createdAt`), ist die Gegenseite.
 * Eine eigene Nachricht dafür gibt es nicht, und sie ist auch nicht nötig.
 * </p>
 *
 * @param onPaired Wer den Code eingelöst hat — sein Name.
 */
export function usePairingCode(onPaired: (name: string) => void): PairingCode {
  const host = getPlatform().host

  const [code, setCode] = useState<HostPairingCode | undefined>(undefined)
  const [remaining, setRemaining] = useState(0)
  const [expired, setExpired] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  /** Die Clientliste zum Zeitpunkt des Codes: Kennung → gekoppelt am. */
  const baseline = useRef<Map<string, number> | undefined>(undefined)
  const paired = useRef(onPaired)
  const open = useRef(false)

  useEffect(() => {
    paired.current = onPaired
  })

  const renew = useCallback((): void => {
    setBusy(true)
    setError(undefined)
    setExpired(false)

    void host
      .clients()
      .catch((): HostClient[] => [])
      .then(async (before) => {
        baseline.current = new Map(before.map((client) => [client.id, client.createdAt ?? 0]))

        return host.pairingCode()
      })
      .then(
        (next) => {
          open.current = true
          setCode(next)
          setBusy(false)
        },
        (failure: unknown) => {
          setBusy(false)
          setError(failure instanceof Error ? failure.message : String(failure))
        },
      )
  }, [host])

  const cancel = useCallback((): void => {
    setCode(undefined)
    baseline.current = undefined

    if (open.current) {
      open.current = false
      void host.cancelPairing().catch(() => undefined)
    }
  }, [host])

  // Wer die Seite verlässt, ohne „Schließen" zu drücken, meint dasselbe.
  useEffect(() => cancel, [cancel])

  // Der Code läuft nach fünf Minuten ab. Ohne die Anzeige steht er weiter da
  // und wird eingetippt, und die Kopplung scheitert ohne erkennbaren Grund.
  useEffect(() => {
    if (code === undefined) {
      return
    }

    const until = Date.now() + code.expiresInSeconds * 1000

    const tick = (): void => {
      const left = Math.max(0, Math.round((until - Date.now()) / 1000))

      setRemaining(left)

      if (left === 0) {
        setExpired(true)
        cancel()
      }
    }

    tick()

    const timer = window.setInterval(tick, TICK_MS)

    return () => window.clearInterval(timer)
  }, [code, cancel])

  useEffect(() => {
    if (code === undefined) {
      return
    }

    let current = true

    const look = (): void => {
      void host.clients().then(
        (now) => {
          const before = baseline.current

          if (!current || before === undefined) {
            return
          }

          const fresh = now.find(
            (client) => !before.has(client.id) || before.get(client.id) !== (client.createdAt ?? 0),
          )

          if (fresh !== undefined) {
            // Eingelöst ist eingelöst — der Code gilt ohnehin nicht mehr, und
            // eine Gegenstelle, die nur dafür lief, darf jetzt gehen. Erst
            // meldet sich aber die Seite: sie holt noch den Steckbrief ab.
            baseline.current = undefined
            setCode(undefined)
            paired.current(fresh.label)
          }
        },
        () => undefined,
      )
    }

    const timer = window.setInterval(look, WATCH_MS)

    return () => {
      current = false
      window.clearInterval(timer)
    }
  }, [code, host])

  return { code, remaining, expired, busy, error, renew, cancel }
}

interface Props {
  pairing: PairingCode
  /** QR-Code oder Adresse, Code und Prüfzeichen zum Abtippen. */
  mode: 'qr' | 'manual'
  onClose: () => void
}

/**
 * Die Anzeige des Codes — als QR-Code oder zum Abtippen, nie beides.
 *
 * <p>
 * Darunter stehen immer dieselben zwei Knöpfe: ein neuer Code, und Schließen.
 * Schließen macht den Code sofort ungültig; er bliebe sonst fünf Minuten
 * einlösbar, ohne dass ihn noch jemand sieht.
 * </p>
 */
export function PairingOffer({ pairing, mode, onClose }: Props): React.JSX.Element {
  const [qr, setQr] = useState<string | undefined>(undefined)
  const { code } = pairing

  useEffect(() => {
    const uri = code?.pairingUri

    if (typeof uri !== 'string') {
      setQr(undefined)
      return
    }

    void QRCode.toDataURL(uri, { margin: 1, width: 260 }).then(setQr, () => setQr(undefined))
  }, [code])

  const target = targetOf(code)

  return (
    <>
      {pairing.error !== undefined && <p className="error-text">{pairing.error}</p>}

      {pairing.busy && code === undefined && <p className="settings-hint">Code wird erzeugt…</p>}

      {pairing.expired && code === undefined && (
        <p className="settings-hint">Der Code ist abgelaufen.</p>
      )}

      {code !== undefined && mode === 'qr' && (
        <>
          {qr === undefined ? (
            <p className="settings-hint">
              Noch keine Adresse im Netz — ohne sie gibt es keinen QR-Code.
            </p>
          ) : (
            <img className="pairing-qr" src={qr} alt="QR-Code zur Kopplung" />
          )}
        </>
      )}

      {code !== undefined && mode === 'manual' && (
        <div className="pairing-manual">
          <span className="field-label">Adresse</span>
          <p className="pairing-code address">{target ?? 'noch keine Adresse im Netz'}</p>

          <span className="field-label">Code</span>
          <p className="pairing-code">{code.code}</p>

          {/* Ein Gerät mit Zertifikat von Tailscale hat kein Prüfzeichen —
              dann steht hier auch keins. */}
          {typeof code.check === 'string' && code.check.length > 0 && (
            <>
              <span className="field-label">Prüfzeichen</span>
              <p className="pairing-code check">{code.check}</p>
            </>
          )}
        </div>
      )}

      {code !== undefined && (
        <p className="settings-hint pairing-timer">Noch {pairing.remaining} Sekunden gültig.</p>
      )}

      <div className="choice-buttons">
        <button type="button" disabled={pairing.busy} onClick={pairing.renew}>
          Neuen Code erzeugen
        </button>
        <button type="button" className="secondary" onClick={onClose}>
          Schließen
        </button>
      </div>
    </>
  )
}

/** Adresse und Port aus dem QR-Inhalt — dieselbe Angabe, die gescannt würde. */
function targetOf(code: HostPairingCode | undefined): string | undefined {
  if (typeof code?.pairingUri !== 'string') {
    return undefined
  }

  try {
    const target = parsePairingUri(code.pairingUri)

    return `${target.host}:${target.port}`
  } catch {
    return undefined
  }
}
