import { useState } from 'react'
import type { AgentClient } from '../lib/agentClient.ts'
import type { PowerAction } from '../lib/types.ts'

/** Alles außer Sperren verliert ungespeicherte Arbeit — deshalb Rückfrage. */
const POWER_BUTTONS: { action: PowerAction; label: string; confirm: boolean }[] = [
  { action: 'lock', label: 'Sperren', confirm: false },
  { action: 'sleep', label: 'Standby', confirm: true },
  { action: 'restart', label: 'Neustart', confirm: true },
  { action: 'shutdown', label: 'Herunterfahren', confirm: true },
]

interface Props {
  agent: AgentClient
  deviceName: string
  onError: (message: string) => void
}

/** Sperren, Standby, Neustart, Herunterfahren. */
export function PowerView({ agent, deviceName, onError }: Props): React.JSX.Element {
  const [pending, setPending] = useState<PowerAction | undefined>(undefined)
  const [busy, setBusy] = useState(false)

  const run = async (action: PowerAction): Promise<void> => {
    setBusy(true)

    try {
      await agent.power(action)
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
      setPending(undefined)
    }
  }

  return (
    <div className="power-view">
      <div className="power-grid">
        {POWER_BUTTONS.map(({ action, label, confirm }) => (
          <button
            key={action}
            type="button"
            className={action === 'shutdown' ? 'power-button danger' : 'power-button'}
            disabled={busy}
            onClick={() => {
              if (confirm) {
                setPending(action)
                return
              }

              void run(action)
            }}
          >
            {label}
          </button>
        ))}
      </div>

      {pending !== undefined && (
        <div className="dialog-backdrop" role="dialog" aria-modal="true">
          <div className="dialog">
            <p>
              <strong>{labelFor(pending)}</strong> auf {deviceName} ausführen?
            </p>
            <p className="dialog-note">Nicht gespeicherte Arbeit geht verloren.</p>
            <div className="dialog-buttons">
              <button type="button" onClick={() => setPending(undefined)} disabled={busy}>
                Abbrechen
              </button>
              <button
                type="button"
                className="danger"
                disabled={busy}
                onClick={() => void run(pending)}
              >
                {busy ? 'Läuft…' : 'Ausführen'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function labelFor(action: PowerAction): string {
  return POWER_BUTTONS.find((entry) => entry.action === action)?.label ?? action
}
