import { useEffect, useState } from 'react'
import { AppUpdateView } from './AppUpdateView.tsx'
import { PencilIcon, ScreenIcon } from './icons.tsx'
import { cleanName, MAX_NAME_LENGTH, useIdentity } from '../lib/ownName.ts'
import { getPlatform } from '../platform/index.ts'

interface Props {
  /** Zur Freigabe dieses Geräts. Fehlt, wo die Umgebung das nicht kann. */
  onShare?: () => void
}

/**
 * Die Einstellungen der App: der eigene Name, die Freigabe, die Fassung.
 *
 * <p>
 * **Die Geräte stehen nicht mehr hier.** Sie hatten einen Abschnitt mit genau
 * einem Knopf, der woandershin führte — und daneben, im selben Menü, stand die
 * Liste der gekoppelten Geräte schon. Jetzt führt „Geräte" im Menü direkt
 * dorthin, und die Einstellungen handeln von diesem Gerät.
 * </p>
 */
export function SettingsView({ onShare }: Props): React.JSX.Element {
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

      <NameCard />

      {onShare !== undefined && platform.host.available && (
        <section className="settings-group">
          <h2>Fernsteuerung dieses Geräts</h2>

          <button type="button" className="settings-entry" onClick={onShare}>
            <ScreenIcon />
            <span>Freigabe und Rechte</span>
          </button>
        </section>
      )}

      <section className="settings-group">
        <h2>App</h2>
        <p className="settings-hint">
          {version === undefined ? 'RemoteDesktop' : `RemoteDesktop ${version}`}
        </p>

        {/* `asked`: hier hat jemand ausdrücklich nachgesehen. „Alles aktuell"
            ist dann die Antwort auf eine Frage und keine überflüssige Zeile. */}
        <AppUpdateView asked />
      </section>
    </div>
  )
}

/**
 * Der eigene Gerätename.
 *
 * Er steht ganz oben, weil er das Einzige ist, was andere Geräte von diesem
 * hier zu sehen bekommen. Am Rechner ist er nur zu lesen: dort gehört das dem
 * Fenster, und ein zweiter Weg zu derselben Einstellung wäre einer zu viel.
 */
function NameCard(): React.JSX.Element | null {
  const { state, rename } = useIdentity()
  const [draft, setDraft] = useState<string | undefined>(undefined)
  const [error, setError] = useState<string | undefined>(undefined)

  if (state === undefined) {
    return null
  }

  const editable = getPlatform().name === 'capacitor'

  if (!editable) {
    return (
      <section className="settings-group">
        <h2>Dieses Gerät</h2>
        <p className="settings-hint">
          {state.name} — ändern im Fenster unter „Einstellungen“.
        </p>
      </section>
    )
  }

  if (draft === undefined) {
    return (
      <section className="settings-group">
        <h2>Dieses Gerät</h2>

        <button
          type="button"
          className="settings-entry"
          onClick={() => setDraft(state.name)}
        >
          <span>{state.name}</span>
          <PencilIcon size={14} />
        </button>
      </section>
    )
  }

  return (
    <section className="settings-group">
      <h2>Dieses Gerät</h2>

      <form
        className="device-rename"
        onSubmit={(event) => {
          event.preventDefault()

          if (cleanName(draft).length === 0) {
            setError('Ein Name darf nicht leer sein.')

            return
          }

          void rename(draft).then(
            () => {
              setDraft(undefined)
              setError(undefined)
            },
            (failure: unknown) =>
              setError(failure instanceof Error ? failure.message : String(failure)),
          )
        }}
      >
        {/* Eigene Zeile — siehe `RenameRow` in `DeviceListView`: neben zwei
            Knöpfen war vom getippten Namen am Handy nichts mehr zu sehen. */}
        <input
          type="text"
          value={draft}
          maxLength={MAX_NAME_LENGTH}
          onChange={(event) => setDraft(event.target.value)}
          autoFocus
        />

        <div className="device-rename-row">
          <button type="submit" className="secondary">
            Übernehmen
          </button>

          <button type="button" className="secondary" onClick={() => setDraft(undefined)}>
            Abbrechen
          </button>
        </div>
      </form>

      {error !== undefined && <p className="error-text">{error}</p>}

      <p className="settings-hint">So steht dieses Gerät in fremden Listen.</p>
    </section>
  )
}
