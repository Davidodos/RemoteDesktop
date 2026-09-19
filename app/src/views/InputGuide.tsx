import { useState } from 'react'
import { getPlatform } from '../platform/index.ts'

interface Props {
  onBack: () => void
}

/**
 * Wie die Bedienungshilfe eingeschaltet wird — in der App, ohne Internet.
 *
 * <p>
 * **Warum es diese Seite gibt:** seit Android 13 sperrt das System die
 * Bedienungshilfe einer App, die nicht aus Google Play kommt. In der Liste
 * steht sie dann ausgegraut, und ein Tippen darauf sagt nur „eingeschränkte
 * Einstellung". Der Weg daran vorbei führt über die App-Info — und den findet
 * niemand, der ihn nicht kennt.
 * </p>
 */
export function InputGuide({ onBack }: Props): React.JSX.Element {
  const host = getPlatform().host
  const [error, setError] = useState<string | undefined>(undefined)

  const report = (failure: unknown): void =>
    setError(failure instanceof Error ? failure.message : String(failure))

  return (
    <div className="settings-view input-guide">
      <button type="button" className="link-button back-arrow" onClick={onBack}>
        ← Zurück
      </button>
      <h1>Bedienungshilfen einschalten</h1>

      {error !== undefined && <p className="error-text">{error}</p>}

      <ol className="guide-steps">
        <li>
          <strong>Eingeschränkte Einstellungen zulassen</strong> (Android 13 und neuer): App-Info
          öffnen → ⋮ oben rechts → „Eingeschränkte Einstellungen zulassen". Fehlt der Eintrag,
          ist nichts gesperrt — weiter mit Schritt 2.
          <button type="button" className="secondary" onClick={() => void host.openAppInfo().catch(report)}>
            App-Info öffnen
          </button>
        </li>
        <li>
          <strong>Bedienungshilfe einschalten</strong>: Bedienungshilfen → Installierte Apps (bei
          manchen Herstellern „Heruntergeladene Apps") → RemoteDesktop → Ein.
          <button
            type="button"
            className="secondary"
            onClick={() => void host.openInputSettings().catch(report)}
          >
            Bedienungshilfen öffnen
          </button>
        </li>
      </ol>
    </div>
  )
}
