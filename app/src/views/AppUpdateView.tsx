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
export function AppUpdateView(): React.JSX.Element | null {
  const platform = getPlatform()
  const [state, setState] = useState<AppUpdateState>({ kind: 'checking' })
  const [offer, setOffer] = useState<UpdateInfo | undefined>(undefined)

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
    return null
  }

  const labels = describeAppUpdate(state)

  if (!labels.visible) {
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
    <section className="agent-update">
      <p className="agent-update-note" role="status">
        {labels.text}
      </p>

      {labels.action !== undefined && (
        <button type="button" onClick={() => void installieren()}>
          {labels.action}
        </button>
      )}
    </section>
  )
}
