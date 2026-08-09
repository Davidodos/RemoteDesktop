import { useEffect, useState } from 'react'
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

      {/* Hier steht nur noch das Update des *Rechners*. Die App zu
          aktualisieren gehört nicht auf die Power-Seite eines einzelnen
          Geräts — es betrifft alle und ist ohne verbundenes Gerät genauso
          nötig. Es steht jetzt unter „Einstellungen". */}
      <AgentUpdateCard agent={agent} deviceName={deviceName} onError={onError} />

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

/**
 * Fassung des Agents und der Knopf, der ihn aktualisiert.
 *
 * Er steht hier und nicht auf einer eigenen Seite, weil das Ergebnis dasselbe
 * ist wie bei den Knöpfen darüber: der Agent beendet sich und kommt neu. Die
 * App verliert dabei die Verbindung und meldet sich von selbst wieder an —
 * ohne das wäre die Funktion unbrauchbar.
 */
function AgentUpdateCard({
  agent,
  deviceName,
  onError,
}: {
  agent: AgentClient
  deviceName: string
  onError: (message: string) => void
}): React.JSX.Element {
  const [version, setVersion] = useState<string | undefined>(undefined)
  const [meldung, setMeldung] = useState<string | undefined>(undefined)
  const [laeuft, setLaeuft] = useState(false)

  useEffect(() => {
    // Ein älterer Agent meldet keine Fassung. Das ist kein Fehler und keine
    // Meldung wert — dann steht dort eben nichts.
    agent.getInfo().then(
      (info) => setVersion(info.version),
      () => undefined,
    )
  }, [agent])

  const pruefen = async (): Promise<void> => {
    setLaeuft(true)
    setMeldung(undefined)

    try {
      const bericht = await agent.update()
      setMeldung(bericht.message)
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    } finally {
      setLaeuft(false)
    }
  }

  return (
    <section className="agent-update">
      <p className="agent-version">
        Agent auf {deviceName}
        {version === undefined ? '' : ` — Fassung ${version}`}
      </p>

      <button type="button" disabled={laeuft} onClick={() => void pruefen()}>
        {laeuft ? 'Prüfe…' : 'Auf Updates prüfen'}
      </button>

      {meldung !== undefined && (
        <p className="agent-update-note" role="status">
          {meldung}
        </p>
      )}
    </section>
  )
}

function labelFor(action: PowerAction): string {
  return POWER_BUTTONS.find((entry) => entry.action === action)?.label ?? action
}
