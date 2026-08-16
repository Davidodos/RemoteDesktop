import { useCallback, useEffect, useState } from 'react'
import { readable } from '../lib/certificateTrust.ts'
import { getPlatform } from '../platform/index.ts'
import type { HostStatus } from '../platform/index.ts'

interface Props {
  onBack: () => void
}

/**
 * „Dieses Gerät steuerbar machen" — die andere Richtung.
 *
 * Bis V4 war die App ausschließlich Fernbedienung. Hier wird das Handy selbst
 * zum Ziel: es spricht dasselbe Protokoll wie ein Windows-Agent, und wer es
 * steuern will, koppelt sich genauso wie an einen PC.
 *
 * Die Seite sagt beim Einschalten ausdrücklich, was das bedeutet. Ein Schalter,
 * der ein Handy von außen erreichbar macht, gehört nicht kommentarlos neben die
 * Aktualisierungsprüfung.
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
          Steuerbar machen lässt sich nur die Android-App. An diesem Rechner
          übernimmt das der Agent — er läuft neben dem Fenster und wird in der
          Einrichtung eingeschaltet.
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
        <h2>Dieses Gerät darf ferngesteuert werden</h2>

        {/* Am Handy ist das ein Schalter: der Server lebt mit der App. Am
            Rechner ist es eine Auskunft — freigegeben ist er, solange sein
            Agent läuft, und den startet nicht die Oberfläche. Ein Schalter
            hier meinte etwas, das ihr nicht gehört. */}
        <p className="settings-hint">
          {host.toggleable
            ? running
              ? 'Eingeschaltet. Der Server läuft, solange diese App offen ist — ' +
                'und endet mit ihr. Er überlebt, dass die App im Hintergrund ist ' +
                'oder der Bildschirm ausgeht; daran erinnert die Benachrichtigung.'
              : 'Ausgeschaltet. Solange das so bleibt, ist dieses Handy von außen ' +
                'nicht erreichbar — auch nicht von einem Gerät, das schon ' +
                'gekoppelt ist.'
            : running
              ? 'Der Agent läuft — dieser Rechner ist für gekoppelte Geräte ' +
                'erreichbar. Beenden lässt er sich im Fenster unter ' +
                '„Einstellungen".'
              : 'Der Agent läuft nicht. Dieser Rechner ist damit von außen nicht ' +
                'erreichbar — koppeln geht trotzdem, gesteuert werden kann er ' +
                'erst wieder, wenn er läuft. Starten im Fenster unter ' +
                '„Einstellungen".'}
        </p>

        {host.toggleable && (
          <>
            <p className="settings-hint">
              Jede Verbindung wird hier außerdem einzeln bestätigt: es erscheint eine
              Karte, und ohne Antwort gilt die Anfrage nach einer halben Minute als
              abgelehnt. Eine Kopplung sagt, <em>wer</em> fragen darf — dass jetzt
              gerade jemand zusehen darf, sagt nur ein Mensch.
            </p>

            <button type="button" className="settings-entry" onClick={toggle} disabled={busy}>
              <span>{running ? 'Ausschalten' : 'Einschalten'}</span>
            </button>
          </>
        )}
      </section>

      {(running || !host.toggleable) && (
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

          {status !== undefined && status.addresses.length > 1 && (
            <p className="settings-hint">
              Über ein zweites Netz erreichbar als{' '}
              {status.addresses
                .slice(1)
                .map((address) => `${address}:${status.port}`)
                .join(' · ')}
            </p>
          )}
          <p className="settings-hint">
            Die Adresse kommt vom Router und kann sich ändern. Wer dieses Handy
            dauerhaft erreichen will, trägt im Router eine feste Adresse ein —
            oder benutzt Tailscale, wo der Name bleibt.
          </p>

          {status?.caFingerprint !== undefined && (
            <>
              <p className="settings-hint">
                Fingerabdruck der eigenen Stelle. Wer von Hand koppelt — am PC
                gibt es keine Kamera —, bekommt ihn dort gezeigt und vergleicht
                ihn mit diesem:
              </p>
              <p className="fingerprint">
                <small>{readable(status.caFingerprint)}</small>
              </p>
            </>
          )}
        </section>
      )}

      {running && host.toggleable && (
        <section className="settings-group">
          <h2>Bildschirm</h2>
          <p className="settings-hint">
            {status?.sharingScreen === true
              ? 'Die Aufnahme läuft. Sie endet, wenn dieses Handy neu startet ' +
                'oder du sie hier beendest — danach fragt Android bei der ' +
                'nächsten zugelassenen Verbindung wieder nach.'
              : 'Android fragt danach, sobald du die erste Verbindung zulässt — ' +
                'nicht vorher. Bis dahin liegt hier keine Erlaubnis herum, die ' +
                'jemand vergessen könnte. Lehnst du sie dann ab, lässt sich ' +
                'dieses Gerät bedienen, aber nicht ansehen.'}
          </p>

          {status?.sharingScreen === true && (
            <button
              type="button"
              className="settings-entry"
              onClick={() => {
                void host.disableScreen().then(setStatus, describe(setError))
              }}
            >
              <span>Bildschirmaufnahme beenden</span>
            </button>
          )}
        </section>
      )}

      {running && host.toggleable && (
        <section className="settings-group">
          <h2>Fernsteuerung</h2>
          <p className="settings-hint">
            {status?.acceptingInput === true
              ? 'Eingaben kommen an. Tippen, wischen und Text in das gerade ' +
                'geöffnete Eingabefeld — echte Tastenkombinationen kennt ' +
                'Android nicht.'
              : 'Noch nicht eingeschaltet. Android verlangt dafür den Gang in ' +
                'die Einstellungen unter „Bedienungshilfen" und dort ' +
                '„RemoteDesktop-Fernsteuerung". Das ist ein großes Recht: der ' +
                'Dienst darf überall hintippen. Deshalb kann die App es nicht ' +
                'für dich einschalten.'}
          </p>

          {status?.acceptingInput !== true && (
            <button
              type="button"
              className="settings-entry"
              onClick={() => {
                void host.openInputSettings().then(
                  // Beim Zurückkommen ist der Stand ein anderer — nachgefragt
                  // wird deshalb hier und nicht erst beim nächsten Öffnen.
                  () => window.setTimeout(refresh, 500),
                  describe(setError),
                )
              }}
            >
              <span>Bedienungshilfen öffnen</span>
            </button>
          )}
        </section>
      )}

      <section className="settings-group">
        <h2>Koppeln</h2>
        <p className="settings-hint">
          Der Kopplungscode und die Liste der Geräte, die dieses hier steuern
          dürfen, stehen unter <strong>Geräte → Neues Gerät koppeln</strong>.
          Beides gehört zusammen: wer koppelt, entscheidet damit genau darüber,
          wer in dieser Liste steht.
        </p>
      </section>
    </div>
  )
}

function Header({ onBack }: { onBack: () => void }): React.JSX.Element {
  return (
    <>
      <button type="button" className="link-button" onClick={onBack}>
        ← Einstellungen
      </button>
      <h1>Dieses Gerät freigeben</h1>
    </>
  )
}

/** Aus einem beliebigen Fehlschlag wird ein Satz, den man lesen kann. */
function describe(report: (message: string) => void): (failure: unknown) => void {
  return (failure) =>
    report(failure instanceof Error ? failure.message : String(failure))
}
