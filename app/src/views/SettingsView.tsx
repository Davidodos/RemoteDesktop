import { useEffect, useState } from 'react'
import { AppUpdateView } from './AppUpdateView.tsx'
import { DevicesIcon } from './icons.tsx'
import { getPlatform } from '../platform/index.ts'

interface Props {
  /** Zu den verbundenen Geräten — dort wird verwaltet und gekoppelt. */
  onDevices: () => void
}

/**
 * Die Einstellungen der App.
 *
 * <p>
 * **Der Befund dahinter:** hinter dem Burger-Menü stand „Alle Geräte
 * verwalten", und das war zugleich der einzige Ort, an dem es überhaupt etwas
 * einzustellen gab. Die Aktualisierung der App war zwar gebaut, hing aber in
 * der Power-Ansicht *eines* Rechners — an einer Stelle also, an der niemand
 * sucht, was die App selbst betrifft, und die es gar nicht gibt, solange kein
 * Gerät verbunden ist. Aus Sicht des Nutzers gab es die Funktion damit nicht.
 * </p>
 *
 * <p>
 * Jetzt führt der Weg über „Einstellungen": was die App angeht, steht hier, und
 * die Geräte sind ein Eintrag darin statt der Überschrift darüber.
 * </p>
 */
export function SettingsView({ onDevices }: Props): React.JSX.Element {
  const platform = getPlatform()
  const [version, setVersion] = useState<string | undefined>(undefined)

  useEffect(() => {
    // Eine fehlende Fassungsangabe ist kein Fehler, sondern eine Plattform, die
    // sie nicht kennt. Dann steht hier eben nichts.
    void platform.update.installed().then(setVersion, () => undefined)
  }, [platform])

  return (
    <div className="settings-view">
      <h1>Einstellungen</h1>

      <section className="settings-group">
        <h2>Verbundene Geräte</h2>
        <p className="settings-hint">
          Rechner umbenennen, entfernen oder einen neuen koppeln.
        </p>

        <button type="button" className="settings-entry" onClick={onDevices}>
          <DevicesIcon />
          <span>Verbundene Geräte</span>
        </button>
      </section>

      <section className="settings-group">
        <h2>App</h2>
        <p className="settings-hint">
          {version === undefined ? 'RemoteDesktop' : `RemoteDesktop, Fassung ${version}`}
        </p>

        {/* `asked`: hier hat jemand ausdrücklich nachgesehen. „Alles aktuell"
            ist dann die Antwort auf eine Frage und keine überflüssige Zeile. */}
        <AppUpdateView asked />
      </section>
    </div>
  )
}
