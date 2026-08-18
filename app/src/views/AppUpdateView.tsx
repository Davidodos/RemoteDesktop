import { useCallback, useEffect, useState } from 'react'
import { describeAppUpdate, type AppUpdateState } from '../lib/appUpdateState.ts'
import { getPlatform, type UpdateInfo } from '../platform/index.ts'

/**
 * Das Update der App selbst — nicht des Rechners, den sie gerade steuert.
 *
 * Es hängt bewusst neben dem Agent-Update: beide Male geht es darum, dass etwas
 * neuer wird, und beide Male steht danach ein Neustart. Wer eine neue Fassung
 * ausrollt, findet hier beide Hälften an einer Stelle.
 *
 * In der PWA und im Windows-Fenster erscheint dieser Bereich nicht — dort sagt
 * die Plattform `selfUpdate: false`, weil ein Browser sich nicht selbst
 * austauscht und das Fenster über seinen eigenen Installer geht.
 */
export function AppUpdateView({
  asked = false,
  placement = 'inline',
}: {
  asked?: boolean
  /**
   * `banner` legt den Bereich über die Seite statt in sie hinein.
   *
   * <p>
   * **Wofür.** Die App soll bei jedem Start nachsehen, ob es etwas Neues gibt —
   * nicht nur, wenn jemand die Einstellungen aufsucht. Wer nie dorthin geht,
   * erfuhr sonst nie von einer neuen Fassung, und genau das ist der Fall, in dem
   * eine alte App am längsten liegen bleibt. Zu sehen ist davon trotzdem nur
   * dann etwas, wenn es etwas zu sagen gibt: bei „alles aktuell" bleibt der
   * Bereich leer (siehe `describeAppUpdate`).
   * </p>
   */
  placement?: 'inline' | 'banner'
} = {}): React.JSX.Element | null {
  const platform = getPlatform()
  const [state, setState] = useState<AppUpdateState>({ kind: 'checking' })
  const [offer, setOffer] = useState<UpdateInfo | undefined>(undefined)

  /**
   * Ob das Band weggetippt wurde.
   *
   * <p>
   * **Nur für dieses eine Angebot.** Ein Band, das man nicht loswird, ist keine
   * Nachricht mehr, sondern ein Hindernis: es liegt über der Oberfläche, und
   * wer gerade etwas anderes vorhat, kommt an ihm nicht vorbei. Beim nächsten
   * Start steht es wieder da — vergessen wird das Angebot nicht, nur
   * beiseitegeschoben.
   * </p>
   */
  const [dismissed, setDismissed] = useState(false)

  const pruefen = useCallback(async (): Promise<void> => {
    setState({ kind: 'checking' })

    try {
      const gefunden = await platform.update.check()

      setOffer(gefunden)
      setState(
        gefunden === undefined
          ? { kind: 'current' }
          : { kind: 'offer', version: gefunden.version },
      )
    } catch (failure) {
      setState({
        kind: 'failed',
        message: failure instanceof Error ? failure.message : String(failure),
      })
    }
  }, [platform])

  useEffect(() => {
    if (!platform.capabilities.selfUpdate) {
      return
    }

    void pruefen()
  }, [platform, pruefen])

  if (!platform.capabilities.selfUpdate) {
    return asked ? (
      <p className="agent-update-note" role="status">
        Diese Fassung aktualisiert sich nicht selbst: im Browser übernimmt das die Seite
        beim Neuladen, im Windows-Fenster der Installer.
      </p>
    ) : null
  }

  const labels = describeAppUpdate(state, asked)

  // Beiseitegeschoben gilt nur für das Band. Auf der Einstellungsseite wurde
  // ausdrücklich danach gefragt; dort etwas zu verstecken wäre eine Antwort,
  // die niemand bekommt.
  if (!labels.visible || (placement === 'banner' && dismissed)) {
    return null
  }

  const installieren = async (): Promise<void> => {
    if (offer === undefined) {
      void pruefen()
      return
    }

    setState({ kind: 'installing' })

    try {
      await platform.update.install(offer)

      // Die Zusage löst jetzt erst auf, wenn Android die Installation
      // abgeschlossen hat — vorher galt sie als erfüllt, sobald die Datei
      // abgegeben war, und der Bereich blieb bei „fragt gleich nach" stehen,
      // gleich ob jemand bestätigte oder ablehnte. Danach startet die App neu;
      // dass hier noch etwas gezeichnet wird, ist die Ausnahme.
      setOffer(undefined)
      setState({ kind: 'current' })
    } catch (failure) {
      // Ein Abbruch im Systemdialog kommt hier ebenso an wie ein fehlerhafter
      // Download. Beides darf nicht still enden — sonst wartet jemand auf ein
      // Update, das nie kommt.
      setState({
        kind: 'failed',
        message: failure instanceof Error ? failure.message : String(failure),
      })
    }
  }

  return (
    <section className={placement === 'banner' ? 'agent-update banner' : 'agent-update'}>
      <p className="agent-update-note" role="status">
        {labels.text}
      </p>

      {labels.action !== undefined && (
        <button
          type="button"
          onClick={() => void (state.kind === 'offer' ? installieren() : pruefen())}
        >
          {labels.action}
        </button>
      )}

      {/* Nur am Band, und nicht während der Installation: was dann läuft, lässt
          sich nicht wegtippen, und ein Knopf, der so tut, wäre eine Lüge. */}
      {placement === 'banner' && state.kind !== 'installing' && (
        <button
          type="button"
          className="agent-update-dismiss"
          aria-label="Hinweis ausblenden"
          title="Ausblenden"
          onClick={() => setDismissed(true)}
        >
          ×
        </button>
      )}
    </section>
  )
}
