import { useState } from 'react'
import { saveLocalDevice } from '../lib/deviceSources.ts'
import { pairWithAgent } from '../lib/pairing.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from '../lib/types.ts'

/** Vorgabe, die praktisch nie ein anderer ist (siehe agent/README.md). */
const DEFAULT_PORT = 8443

interface Props {
  onPaired: (devices: Device[], paired: Device) => void
  onCancel: () => void
}

/**
 * Ein Gerät koppeln: Rechnernamen und den Code eintippen, den der Rechner
 * anzeigt.
 *
 * Der QR-Scanner käme hier hin, sobald es eine Kamera gibt (Phase 12) — bis
 * dahin sagt die Ansicht ausdrücklich, warum sie keinen anbietet, statt einen
 * Knopf zu zeigen, der nur eine Fehlermeldung erzeugt.
 */
export function PairingView({ onPaired, onCancel }: Props): React.JSX.Element {
  const [host, setHost] = useState('')
  const [code, setCode] = useState('')
  const [label, setLabel] = useState(defaultLabel())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  const submit = async (): Promise<void> => {
    setBusy(true)
    setError(undefined)

    try {
      const device = await pairWithAgent({
        host: host.trim(),
        port: DEFAULT_PORT,
        code: code.trim(),
        label: label.trim(),
      })

      onPaired(saveLocalDevice(device), device)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  const ready = host.trim().length > 0 && code.trim().length === 6 && label.trim().length > 0

  return (
    <form
      className="token-prompt"
      onSubmit={(event) => {
        event.preventDefault()

        if (ready && !busy) {
          void submit()
        }
      }}
    >
      <h1>Gerät koppeln</h1>
      <p>
        Der Code steht am Rechner selbst. Er gilt fünf Minuten und lässt sich nur
        einmal verwenden.
      </p>

      {error !== undefined && <p className="error-text">{error}</p>}

      <input
        value={host}
        onChange={(event) => setHost(event.target.value)}
        placeholder="Rechnername im Tailnet"
        autoCapitalize="off"
        autoCorrect="off"
        spellCheck={false}
      />

      <input
        value={code}
        onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
        placeholder="6-stelliger Code"
        inputMode="numeric"
        autoComplete="off"
      />

      <input
        value={label}
        onChange={(event) => setLabel(event.target.value)}
        placeholder="Name dieses Geräts"
      />

      <button type="submit" disabled={!ready || busy}>
        {busy ? 'Koppeln…' : 'Koppeln'}
      </button>

      <button type="button" className="secondary" onClick={onCancel} disabled={busy}>
        Abbrechen
      </button>
    </form>
  )
}

/**
 * Unter diesem Namen taucht das Gerät in der Liste am Rechner auf. Ein
 * brauchbarer Vorschlag ist wichtiger, als er aussieht: wer widerrufen will,
 * muss Monate später erkennen, welcher Eintrag welches Gerät ist.
 */
function defaultLabel(): string {
  return getPlatform().name === 'web' ? 'Browser' : 'Handy'
}
