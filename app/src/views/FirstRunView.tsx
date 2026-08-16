import { useCallback, useEffect, useState } from 'react'
import { cleanName, MAX_NAME_LENGTH } from '../lib/ownName.ts'
import { getPlatform } from '../platform/index.ts'
import type { HostStatus } from '../platform/index.ts'

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
 * **Darf es ferngesteuert werden?** Ein „nein" ist hier der Normalfall und
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
        <h1>Darf dieses Gerät ferngesteuert werden?</h1>
        <p>Andere Geräte steuern kannst du in beiden Fällen. Änderbar in den Einstellungen.</p>

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

  const input = status?.acceptingInput === true
  const screen = status?.sharingScreen === true

  return (
    <div className="token-prompt">
      <h1>Zwei Rechte fehlen noch</h1>
      <p>Android vergibt beide selbst — die App kann das nicht für dich tun.</p>

      {error !== undefined && <p className="error-text">{error}</p>}

      <button
        type="button"
        disabled={input}
        onClick={() => {
          setError(undefined)

          // Beim Zurückkommen ist der Stand ein anderer — nachgefragt wird
          // deshalb hier und nicht erst beim nächsten Öffnen.
          void host.openInputSettings().then(
            () => window.setTimeout(refresh, 500),
            report,
          )
        }}
      >
        {input ? '✓ Eingaben freigegeben' : 'Eingaben freigeben (Bedienungshilfe)'}
      </button>

      <button
        type="button"
        disabled={screen}
        onClick={() => {
          setError(undefined)
          void host.enableScreen().then(setStatus, report)
        }}
      >
        {screen ? '✓ Bildschirm freigegeben' : 'Bildschirm freigeben'}
      </button>

      <button type="button" className="secondary" onClick={onDone}>
        Fertig
      </button>
    </div>
  )
}
