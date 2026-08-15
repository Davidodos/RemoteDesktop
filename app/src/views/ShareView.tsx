import { useCallback, useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { readable } from '../lib/certificateTrust.ts'
import { getPlatform } from '../platform/index.ts'
import type { HostClient, HostPairingCode, HostStatus } from '../platform/index.ts'

/** Wie oft die verbleibende Gültigkeit des Codes nachgerechnet wird. */
const TICK_MS = 1000

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
  const [clients, setClients] = useState<HostClient[]>([])
  const [pairing, setPairing] = useState<HostPairingCode | undefined>(undefined)
  const [remaining, setRemaining] = useState(0)
  const [qr, setQr] = useState<string | undefined>(undefined)
  const [error, setError] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)

  const refresh = useCallback((): void => {
    if (!host.available) {
      return
    }

    void host.status().then(setStatus, describe(setError))
    void host.clients().then(setClients, () => undefined)
  }, [host])

  useEffect(refresh, [refresh])

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
        setPairing(undefined)
        setQr(undefined)
      }
    }

    tick()

    const timer = window.setInterval(tick, TICK_MS)

    return () => window.clearInterval(timer)
  }, [pairing])

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

        // Beim Abschalten fällt auch der offene Code weg — er gehört zum
        // laufenden Host, und ein Code ohne Server ist eine Sackgasse.
        if (next.running) {
          refresh()
        } else {
          setPairing(undefined)
        }
      },
      (failure: unknown) => {
        describe(setError)(failure)
        setBusy(false)
      },
    )
  }

  const showCode = (): void => {
    setError(undefined)

    void host.pairingCode().then((fresh) => {
      setPairing(fresh)
    }, describe(setError))
  }

  return (
    <div className="settings-view">
      <Header onBack={onBack} />

      {error !== undefined && <p className="error-text">{error}</p>}

      <section className="settings-group">
        <h2>Dieses Gerät darf ferngesteuert werden</h2>
        <p className="settings-hint">
          {running
            ? 'Eingeschaltet. Der Server läuft, solange diese App offen ist — ' +
              'und endet mit ihr. Er überlebt, dass die App im Hintergrund ist ' +
              'oder der Bildschirm ausgeht; daran erinnert die Benachrichtigung.'
            : 'Ausgeschaltet. Solange das so bleibt, ist dieses Handy von außen ' +
              'nicht erreichbar — auch nicht von einem Gerät, das schon ' +
              'gekoppelt ist.'}
        </p>
        <p className="settings-hint">
          Jede Verbindung wird hier außerdem einzeln bestätigt: es erscheint eine
          Karte, und ohne Antwort gilt die Anfrage nach einer halben Minute als
          abgelehnt. Eine Kopplung sagt, <em>wer</em> fragen darf — dass jetzt
          gerade jemand zusehen darf, sagt nur ein Mensch.
        </p>

        <button type="button" className="settings-entry" onClick={toggle} disabled={busy}>
          <span>{running ? 'Ausschalten' : 'Einschalten'}</span>
        </button>
      </section>

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

      {running && (
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

      {running && (
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

      {running && (
        <section className="settings-group">
          <h2>Gerät koppeln</h2>

          {pairing === undefined ? (
            <>
              <p className="settings-hint">
                Der Code gilt fünf Minuten und lässt genau eine Kopplung zu.
              </p>
              <button type="button" className="settings-entry" onClick={showCode}>
                <span>Kopplungscode anzeigen</span>
              </button>
            </>
          ) : (
            <>
              <p className="pairing-code">{pairing.code}</p>
              <p className="settings-hint">Noch {remaining} Sekunden gültig.</p>

              {qr === undefined ? (
                <p className="settings-hint">
                  Am anderen Handy „Gerät koppeln" öffnen und Adresse und Code
                  eintippen. Am PC führt derselbe Weg über das Fenster.
                </p>
              ) : (
                <img className="pairing-qr" src={qr} alt="QR-Code zur Kopplung" />
              )}
            </>
          )}
        </section>
      )}

      <section className="settings-group">
        <h2>Wer darf</h2>

        {clients.length === 0 ? (
          <p className="settings-hint">Noch niemand.</p>
        ) : (
          clients.map((client) => (
            <div key={client.id} className="settings-entry">
              <span>{client.label}</span>
              <button
                type="button"
                onClick={() => {
                  void host.revoke(client.id).then(refresh, describe(setError))
                }}
              >
                Entfernen
              </button>
            </div>
          ))
        )}
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
