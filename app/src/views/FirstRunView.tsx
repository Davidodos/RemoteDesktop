import { useCallback, useEffect, useState } from 'react'
import { cleanName, MAX_NAME_LENGTH } from '../lib/ownName.ts'
import { getPlatform } from '../platform/index.ts'
import type { HostStatus } from '../platform/index.ts'
import { InputGuide } from './InputGuide.tsx'
import { PermissionCards } from './PermissionCards.tsx'

interface Props {
  /** Der vorgeschlagene Name — vom System, bis jemand einen eigenen wählt. */
  suggestion: string
  rename: (name: string) => Promise<void>
  onDone: () => void
}

type Step = 'name' | 'ask' | 'permissions'

/**
 * Der erste Start am Handy: zwei Fragen, dann ist die App benutzbar.
 *
 * <p>
 * **Wie heißt dieses Gerät?** Der Name steht später in jeder fremden
 * Geräteliste. Vorher wurde er bei jeder Kopplung neu eingetippt — und wer nur
 * seinen Code vorzeigte, hieß drüben „Pixel 8".
 * </p>
 *
 * <p>
 * **Remote-Steuerung zulassen?** Ein „nein" ist hier der Normalfall und
 * kostet nichts: gekoppelt und gesteuert wird trotzdem, nur eben in die eine
 * Richtung. Ein „ja" führt sofort weiter zu den beiden Rechten, die Android
 * dafür verlangt — sie später nachzureichen hieße, dass am anderen Ende jemand
 * wartend vor einem schwarzen Bild sitzt.
 * </p>
 *
 * <p>
 * Am Rechner gibt es diese Seite nicht: dort führt der Einrichtungsassistent
 * des Fensters, und der fragt dasselbe.
 * </p>
 */
export function FirstRunView({ suggestion, rename, onDone }: Props): React.JSX.Element {
  const host = getPlatform().host

  const [step, setStep] = useState<Step>('name')
  const [name, setName] = useState(suggestion)
  const [status, setStatus] = useState<HostStatus | undefined>(undefined)
  const [error, setError] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)
  const [guide, setGuide] = useState(false)

  const refresh = useCallback((): void => {
    if (host.available) {
      void host.status().then(setStatus, () => undefined)
    }
  }, [host])

  useEffect(() => {
    if (step === 'permissions') {
      refresh()
    }
  }, [step, refresh])

  const report = (failure: unknown): void =>
    setError(failure instanceof Error ? failure.message : String(failure))

  if (step === 'name') {
    return (
      <form
        className="token-prompt"
        onSubmit={(event) => {
          event.preventDefault()

          if (cleanName(name).length === 0) {
            setError('Ein Name darf nicht leer sein.')

            return
          }

          setBusy(true)

          void rename(name).then(
            () => {
              setBusy(false)
              setError(undefined)
              setStep(host.available ? 'ask' : 'name')

              if (!host.available) {
                onDone()
              }
            },
            (failure: unknown) => {
              setBusy(false)
              report(failure)
            },
          )
        }}
      >
        <h1>Wie heißt dieses Gerät?</h1>
        <p>So steht es später in den Listen der Geräte, mit denen du es koppelst.</p>

        {error !== undefined && <p className="error-text">{error}</p>}

        <input
          value={name}
          maxLength={MAX_NAME_LENGTH}
          onChange={(event) => setName(event.target.value)}
          placeholder="z. B. Handy"
          autoFocus
        />

        <button type="submit" disabled={busy}>
          Weiter
        </button>
      </form>
    )
  }

  if (step === 'ask') {
    return (
      <div className="token-prompt">
        <h1>Remote-Steuerung für dieses Gerät zulassen?</h1>
        <p>
          Aktiviert Bildschirmfreigabe und ermöglicht Eingaben von einem anderen Gerät. Jederzeit
          in den Einstellungen änderbar.
        </p>

        {error !== undefined && <p className="error-text">{error}</p>}

        <button
          type="button"
          disabled={busy}
          onClick={() => {
            setBusy(true)
            setError(undefined)

            void host.start().then(
              (next) => {
                setStatus(next)
                setBusy(false)
                setStep('permissions')
              },
              (failure: unknown) => {
                setBusy(false)
                report(failure)
              },
            )
          }}
        >
          Ja
        </button>

        <button type="button" className="secondary" disabled={busy} onClick={onDone}>
          Nein
        </button>
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

  return (
    <div className="token-prompt">
      <h1>Freigaben</h1>

      {error !== undefined && <p className="error-text">{error}</p>}

      <div>
        <PermissionCards
          status={status}
          onStatus={setStatus}
          onRefresh={refresh}
          onError={report}
          onGuide={() => setGuide(true)}
        />
      </div>

      <button type="button" className="secondary" onClick={onDone}>
        Fertig
      </button>
    </div>
  )
}
