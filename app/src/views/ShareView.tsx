import { useCallback, useEffect, useState } from 'react'
import { getPlatform } from '../platform/index.ts'
import type { HostStatus } from '../platform/index.ts'

interface Props {
  onBack: () => void
}

/**
 * „Darf dieses Gerät ferngesteuert werden?" — die andere Richtung.
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
              ? 'Eingeschaltet. Läuft, solange die App nicht weggewischt wird. Jede Verbindung wird einzeln bestätigt.'
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

      {/* **Zwei Zeilen statt zweier Abschnitte.** Bildschirm und Eingaben sind
          dasselbe Muster — etwas ist freigegeben oder nicht, und es gibt einen
          Weg dorthin. Sie standen als zwei Überschriften mit je einem Absatz
          und einem Knopf da; das waren sechs Zeilen für zwei Zustände. */}
      {running && host.toggleable && (
        <section className="settings-group">
          <h2>Was freigegeben ist</h2>

          <Toggle
            label="Bildschirm"
            reversible
            on={status?.screenAllowed === true}
            hintOn="Andere dürfen zusehen. Android fragt beim ersten Zusehen noch einmal."
            hintOff="Dieses Gerät gibt kein Bild heraus."
            action={status?.screenAllowed === true ? 'Nicht mehr freigeben' : 'Freigeben'}
            onAction={() => {
              // **Kein Systemdialog hier.** Das ist eine Einstellung: sie sagt
              // der Gegenseite, dass es möglich ist. Die Aufnahmeerlaubnis
              // holt `ConnectionRequestView`, wenn wirklich jemand zusehen
              // will — vorher wäre sie eine Erlaubnis für nichts, und Android
              // nimmt sie beim nächsten Neustart ohnehin zurück.
              void host
                .allowScreen(status?.screenAllowed !== true)
                .then(setStatus, describe(setError))
            }}
          />

          <Toggle
            label="Eingaben"
            on={status?.acceptingInput === true}
            hintOn="Tippen, wischen und Text kommen an."
            hintOff="Android verlangt dafür die Bedienungshilfe — einschalten muss sie ein Mensch."
            action="Bedienungshilfen öffnen"
            onAction={() => {
              void host.openInputSettings().then(
                // Beim Zurückkommen ist der Stand ein anderer — nachgefragt
                // wird deshalb hier und nicht erst beim nächsten Öffnen.
                () => window.setTimeout(refresh, 500),
                describe(setError),
              )
            }}
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

/**
 * Eine Freigabe: Name, Zustand, ein Satz dazu und der Weg dorthin.
 *
 * Der Knopf steht rechts und nicht darunter — er gehört zu dieser einen Zeile,
 * und untereinander sahen zwei Freigaben aus wie vier voneinander unabhängige
 * Dinge.
 */
function Toggle({
  label,
  on,
  reversible = false,
  hintOn,
  hintOff,
  action,
  onAction,
}: {
  label: string
  on: boolean
  /**
   * Ob sich die Freigabe hier auch wieder zurücknehmen lässt. Bei den Eingaben
   * nicht: dort führt der Weg in die Systemeinstellungen, und der ist zu Ende,
   * sobald sie an sind.
   */
  reversible?: boolean
  hintOn: string
  hintOff: string
  action: string
  onAction: () => void
}): React.JSX.Element {
  return (
    <div className="share-toggle">
      <div className="share-toggle-text">
        <span className="device-name">
          {label} {on ? '✓' : ''}
        </span>
        <span className="settings-hint">{on ? hintOn : hintOff}</span>
      </div>

      {(!on || reversible) && (
        <button type="button" className="secondary" onClick={onAction}>
          {action}
        </button>
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
      <h1>Fernsteuerung dieses Geräts</h1>
    </>
  )
}

/** Aus einem beliebigen Fehlschlag wird ein Satz, den man lesen kann. */
function describe(report: (message: string) => void): (failure: unknown) => void {
  return (failure) =>
    report(failure instanceof Error ? failure.message : String(failure))
}
