import { useState } from 'react'
import { CertificateTrustStep } from './CertificateTrustStep.tsx'
import { saveLocalDevice } from '../lib/deviceSources.ts'
import { suggestAlias } from '../lib/deviceNames.ts'
import { pairWithAgent } from '../lib/pairing.ts'
import { DEFAULT_AGENT_PORT, parsePairingUri, type PairingTarget } from '../lib/pairingUri.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from '../lib/types.ts'

interface Props {
  onPaired: (devices: Device[], paired: Device) => void
  onCancel: () => void
}

/**
 * Ein Gerät koppeln — in zwei Schritten, und der erste ist meist ein Scan.
 *
 * <p>
 * **Der Befund dahinter:** vorher füllte der Scan drei Eingabefelder, die
 * danach ausgefüllt dastanden und trotzdem angeschaut werden wollten. Adresse,
 * Port und Code sind aber nichts, worüber jemand entscheidet — sie stehen im
 * QR-Code und sind entweder richtig oder unbrauchbar. Entschieden wird nur über
 * die beiden Namen, und genau die stehen jetzt auf der zweiten Seite.
 * </p>
 *
 * <p>
 * Das Abtippen bleibt für alles ohne Kamera — im Browser und im Windows-Fenster
 * gibt es keinen Scanner, und ein Knopf, der nur eine Fehlermeldung erzeugt,
 * wäre schlimmer als keiner.
 * </p>
 */
export function PairingView({ onPaired, onCancel }: Props): React.JSX.Element {
  const [target, setTarget] = useState<PairingTarget | undefined>(undefined)
  const [manual, setManual] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  /**
   * Gekoppelt, aber noch nicht fertig: ein Rechner ohne Tailscale weist sich
   * mit einem selbst ausgestellten Zertifikat aus, und dem muss dieses Gerät
   * erst vertrauen. Ohne diesen Zwischenschritt stünde er in der Liste und
   * ließe sich nicht verbinden — ohne dass irgendwo steht, warum.
   */
  const [awaitingTrust, setAwaitingTrust] = useState<
    { device: Device; devices: Device[] } | undefined
  >(undefined)

  const platform = getPlatform()

  const scan = async (): Promise<void> => {
    setError(undefined)

    try {
      setTarget(parsePairingUri(await platform.qr.scan()))
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    }
  }

  if (awaitingTrust !== undefined) {
    return (
      <CertificateTrustStep
        device={awaitingTrust.device}
        onDone={() => onPaired(awaitingTrust.devices, awaitingTrust.device)}
      />
    )
  }

  if (target !== undefined) {
    return (
      <NameStep
        target={target}
        onBack={() => setTarget(undefined)}
        onPaired={(devices, device) => {
          if (device.caFingerprint === undefined) {
            onPaired(devices, device)

            return
          }

          setAwaitingTrust({ device, devices })
        }}
      />
    )
  }

  if (manual) {
    return (
      <ManualStep
        onBack={() => setManual(false)}
        onTarget={(entered) => {
          setError(undefined)
          setTarget(entered)
        }}
      />
    )
  }

  return (
    <div className="token-prompt">
      <h1>Gerät koppeln</h1>
      <p>
        Am Rechner unter „Geräte“ auf „Code anzeigen“ klicken. Der Code gilt fünf Minuten
        und lässt sich nur einmal verwenden.
      </p>

      {error !== undefined && <p className="error-text">{error}</p>}

      {platform.capabilities.camera && (
        <button type="button" onClick={() => void scan()}>
          QR-Code scannen
        </button>
      )}

      <button type="button" className="secondary" onClick={() => setManual(true)}>
        Von Hand eingeben
      </button>

      <button type="button" className="secondary" onClick={onCancel}>
        Abbrechen
      </button>
    </div>
  )
}

/**
 * Die einzige Seite, auf der es etwas zu entscheiden gibt: wie dieses Gerät am
 * Rechner heißt, und wie der Rechner hier heißt.
 */
function NameStep({
  target,
  onPaired,
  onBack,
}: {
  target: PairingTarget
  onPaired: (devices: Device[], paired: Device) => void
  onBack: () => void
}): React.JSX.Element {
  const [label, setLabel] = useState(defaultLabel())
  const [alias, setAlias] = useState(suggestAlias(target.host))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  const submit = async (): Promise<void> => {
    setBusy(true)
    setError(undefined)

    try {
      const paired = await pairWithAgent({
        host: target.host,
        port: target.port,
        code: target.code,
        label: label.trim(),
      })

      // Der eigene Name geht nirgendwo hin — er steht neben den Zugangsdaten
      // dieses Geräts und sonst nirgends.
      const device =
        alias.trim().length > 0 ? { ...paired, alias: alias.trim() } : paired

      onPaired(saveLocalDevice(device), device)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  const ready = label.trim().length > 0

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
      <h1>Fast fertig</h1>
      <p>
        Der Rechner unter <code>{target.host}</code> ist erkannt. Fehlen nur noch die
        Namen.
      </p>

      {error !== undefined && <p className="error-text">{error}</p>}

      <label className="field-label" htmlFor="pair-label">
        Name dieses Geräts — so steht es am Rechner in der Liste
      </label>
      <input
        id="pair-label"
        value={label}
        onChange={(event) => setLabel(event.target.value)}
        placeholder="z. B. Handy"
      />

      <label className="field-label" htmlFor="pair-alias">
        Name für den Rechner — gilt nur hier und ist jederzeit änderbar
      </label>
      <input
        id="pair-alias"
        value={alias}
        onChange={(event) => setAlias(event.target.value)}
        placeholder="z. B. Arbeitsrechner"
      />

      <button type="submit" disabled={!ready || busy}>
        {busy ? 'Koppeln…' : 'Koppeln'}
      </button>

      <button type="button" className="secondary" onClick={onBack} disabled={busy}>
        Zurück
      </button>
    </form>
  )
}

/** Adresse, Port und Code von Hand — für alles ohne Kamera. */
function ManualStep({
  onTarget,
  onBack,
}: {
  onTarget: (target: PairingTarget) => void
  onBack: () => void
}): React.JSX.Element {
  const [host, setHost] = useState('')
  const [code, setCode] = useState('')

  const ready = host.trim().length > 0 && code.trim().length === 6

  return (
    <form
      className="token-prompt"
      onSubmit={(event) => {
        event.preventDefault()

        if (ready) {
          onTarget({ host: host.trim(), port: DEFAULT_AGENT_PORT, code: code.trim() })
        }
      }}
    >
      <h1>Von Hand eingeben</h1>
      <p>Beides steht am Rechner unter „Geräte“, neben dem QR-Code.</p>

      <input
        value={host}
        onChange={(event) => setHost(event.target.value)}
        placeholder="Adresse des Rechners"
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

      <button type="submit" disabled={!ready}>
        Weiter
      </button>

      <button type="button" className="secondary" onClick={onBack}>
        Zurück
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
