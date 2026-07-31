import { useState } from 'react'
import { saveLocalDevice } from '../lib/deviceSources.ts'
import { pairWithAgent } from '../lib/pairing.ts'
import { DEFAULT_AGENT_PORT, parsePairingUri } from '../lib/pairingUri.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from '../lib/types.ts'

interface Props {
  onPaired: (devices: Device[], paired: Device) => void
  onCancel: () => void
}

/**
 * Ein Gerät koppeln: Rechnernamen und den Code eintippen, den der Rechner
 * anzeigt — oder den QR-Code scannen, der dasselbe enthält.
 *
 * Der Scanner erscheint nur dort, wo es eine Kamera gibt (die APK aus Phase 12).
 * Im Browser und im Windows-Fenster bleibt es beim Abtippen; ein Knopf, der nur
 * eine Fehlermeldung erzeugt, wäre schlimmer als keiner.
 */
export function PairingView({ onPaired, onCancel }: Props): React.JSX.Element {
  const [host, setHost] = useState('')
  const [port, setPort] = useState(DEFAULT_AGENT_PORT)
  const [code, setCode] = useState('')
  const [label, setLabel] = useState(defaultLabel())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  const platform = getPlatform()

  const submit = async (): Promise<void> => {
    setBusy(true)
    setError(undefined)

    try {
      const device = await pairWithAgent({
        host: host.trim(),
        port,
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

  /**
   * Der Scan füllt nur die Felder aus und koppelt nicht von selbst. Der Name,
   * unter dem dieses Gerät am Rechner erscheint, steht danach noch zur
   * Änderung — und wer versehentlich den falschen Code erwischt hat, sieht es,
   * bevor etwas passiert.
   */
  const scan = async (): Promise<void> => {
    setError(undefined)

    try {
      const target = parsePairingUri(await platform.qr.scan())

      setHost(target.host)
      setPort(target.port)
      setCode(target.code)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
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

      {platform.capabilities.camera && (
        <button type="button" className="secondary" onClick={() => void scan()} disabled={busy}>
          QR-Code scannen
        </button>
      )}

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
