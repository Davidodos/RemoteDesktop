import { useEffect } from 'react'
import { getPlatform } from '../platform/index.ts'
import type { HostStatus } from '../platform/index.ts'

interface Props {
  status: HostStatus | undefined
  onStatus: (next: HostStatus) => void
  /** Stand neu holen — nach der Rückkehr aus den Systemeinstellungen. */
  onRefresh: () => void
  onError: (failure: unknown) => void
  /** Die Anleitung zu den Bedienungshilfen öffnen. */
  onGuide: () => void
}

/**
 * Die beiden Rechte dieses Handys: Bildschirm und Eingaben.
 *
 * <p>
 * Dieselben zwei Karten beim ersten Start und in den Einstellungen — zwei
 * Darstellungen derselben Sache wären zwei Stellen, an denen sie
 * auseinanderlaufen. Jede Karte behält ihre Größe, gleich in welchem Zustand:
 * ein Knopf, der beim Tippen springt, trifft beim zweiten Tippen daneben.
 * </p>
 */
export function PermissionCards({
  status,
  onStatus,
  onRefresh,
  onError,
  onGuide,
}: Props): React.JSX.Element {
  const host = getPlatform().host

  // Wer aus den Systemeinstellungen zurückkommt, hat dort etwas geändert —
  // nachgefragt wird beim Zurückkommen, nicht erst beim nächsten Öffnen.
  useEffect(() => {
    const onVisible = (): void => {
      if (document.visibilityState === 'visible') {
        onRefresh()
      }
    }

    document.addEventListener('visibilitychange', onVisible)

    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [onRefresh])

  const screen = status?.screenAllowed === true
  const input = status?.acceptingInput === true

  return (
    <>
      <PermissionCard
        label="Bildschirm"
        on={screen}
        hintOff="Nicht freigegeben"
        onToggle={() => {
          // **Kein Systemdialog hier.** Das ist eine Einstellung: sie sagt der
          // Gegenseite, dass es möglich ist. Die Aufnahmeerlaubnis holt
          // `ConnectionRequestView`, wenn wirklich jemand zusehen will.
          void host.allowScreen(!screen).then(onStatus, onError)
        }}
      />

      <PermissionCard
        label="Eingaben"
        on={input}
        hintOff="Nicht freigegeben. Benötigt Bedienungshilfen"
        onToggle={() => {
          // Aus geht es von hier, an nur über Android — wer die Bedienungshilfe
          // abschaltet, schaltet sie auch dort bewusst wieder ein.
          if (input) {
            // Android nimmt sie mit kurzem Verzug aus der Liste — deshalb
            // danach noch einmal nachsehen.
            void host.disableInput().then((next) => {
              onStatus(next)
              window.setTimeout(onRefresh, 800)
            }, onError)
          } else {
            void host.openInputSettings().catch(onError)
          }
        }}
      />

      <button type="button" className="link-button guide-link" onClick={onGuide}>
        Anleitung: Bedienungshilfen einschalten
      </button>
    </>
  )
}

function PermissionCard({
  label,
  on,
  hintOff,
  onToggle,
}: {
  label: string
  on: boolean
  hintOff: string
  onToggle: () => void
}): React.JSX.Element {
  return (
    <div className="share-toggle permission-card">
      <div className="share-toggle-text">
        <span className="device-name">{label}</span>
        <span className="settings-hint">{on ? 'Freigegeben' : hintOff}</span>
      </div>

      <button type="button" className="secondary" onClick={onToggle}>
        {on ? 'Deaktivieren' : 'Aktivieren'}
      </button>
    </div>
  )
}
