import { useCallback, useEffect, useState } from 'react'
import { getPlatform } from '../platform/index.ts'
import type { HostStatus } from '../platform/index.ts'
import { InputGuide } from './InputGuide.tsx'
import { PermissionCards } from './PermissionCards.tsx'

interface Props {
  onBack: () => void
}

/**
 * „Remote-Steuerung für dieses Gerät zulassen?" — die andere Richtung.
 *
 * <p>
 * Am Handy ist das ein Schalter: der Server lebt mit der App. Am Rechner ist es
 * eine Auskunft — freigegeben ist er, solange sein Agent läuft, und den startet
 * nicht die Oberfläche.
 * </p>
 *
 * <p>
 * **Was hier nicht mehr steht:** der Fingerabdruck der eigenen Stelle (stand
 * zum Vergleichen da und wurde nie verglichen), der Verweis auf die
 * Kopplungsseite und drei Absätze darüber, was eine Freigabe bedeutet. Der
 * Schalter sagt es selbst.
 * </p>
 */
export function ShareView({ onBack }: Props): React.JSX.Element {
  const host = getPlatform().host

  const [status, setStatus] = useState<HostStatus | undefined>(undefined)
  const [error, setError] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)
  const [guide, setGuide] = useState(false)

  const refresh = useCallback((): void => {
    if (!host.available) {
      return
    }

    void host.status().then(setStatus, describe(setError))
  }, [host])

  useEffect(refresh, [refresh])

  if (!host.available) {
    return (
      <div className="settings-view">
        <Header onBack={onBack} />
        <p className="settings-hint">
          An diesem Rechner übernimmt das der Agent — siehe „Übersicht“.
        </p>
      </div>
    )
  }

  if (guide) {
    return (
      <InputGuide
        onBack={() => {
          setGuide(false)
          refresh()
        }}
      />
    )
  }

  const running = status?.running === true

  const toggle = (): void => {
    setBusy(true)
    setError(undefined)

    const action = running ? host.stop() : host.start()

    void action.then(
      (next) => {
        setStatus(next)
        setBusy(false)
        refresh()
      },
      (failure: unknown) => {
        describe(setError)(failure)
        setBusy(false)
      },
    )
  }

  return (
    <div className="settings-view">
      <Header onBack={onBack} />

      {error !== undefined && <p className="error-text">{error}</p>}

      <section className="settings-group">
        <p className="settings-hint">
          {host.toggleable
            ? running
              ? 'Eingeschaltet.'
              : 'Ausgeschaltet. Dieses Gerät ist von außen nicht erreichbar.'
            : running
              ? 'Der Agent läuft — dieser Rechner ist erreichbar.'
              : 'Der Agent läuft nicht. Koppeln geht trotzdem, steuern nicht.'}
        </p>

        {host.toggleable && (
          <button type="button" className="settings-entry" onClick={toggle} disabled={busy}>
            <span>{running ? 'Ausschalten' : 'Einschalten'}</span>
          </button>
        )}
      </section>

      {running && host.toggleable && (
        <section className="settings-group">
          <h2>Rechte</h2>

          <PermissionCards
            status={status}
            onStatus={setStatus}
            onRefresh={refresh}
            onError={describe(setError)}
            onGuide={() => setGuide(true)}
          />
        </section>
      )}

      {running && (
        <section className="settings-group">
          <h2>Erreichbar als</h2>
          <p className="settings-hint">{status?.deviceName ?? 'Dieses Gerät'}</p>

          {/* Eine Adresse, nicht drei. Vorher standen hier alle Schnittstellen
              nebeneinander — WLAN, Mobilfunk, Tunnel, Attrappen —, und wer
              abtippen wollte, musste raten. Jetzt steht vorn, was das System
              gerade benutzt. */}
          <p className="pairing-code address">
            {status !== undefined && status.addresses.length > 0
              ? `${status.addresses[0]}:${status.port}`
              : 'noch keine Adresse im Netz'}
          </p>
        </section>
      )}
    </div>
  )
}

function Header({ onBack }: { onBack: () => void }): React.JSX.Element {
  return (
    <>
      <button type="button" className="link-button" onClick={onBack}>
        ← Einstellungen
      </button>
      <h1>Freigabe</h1>
    </>
  )
}

/** Aus einem beliebigen Fehlschlag wird ein Satz, den man lesen kann. */
function describe(report: (message: string) => void): (failure: unknown) => void {
  return (failure) =>
    report(failure instanceof Error ? failure.message : String(failure))
}
