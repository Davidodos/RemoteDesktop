import { useEffect, useState } from 'react'
import type { AgentClient } from '../lib/agentClient.ts'
import type { AgentActionSummary } from '../lib/types.ts'

interface Props {
  agent: AgentClient
  deviceName: string
  onError: (message: string) => void
}

/**
 * Die Aktionen, die der Zielrechner anbietet.
 *
 * Anders als die Tastenkombinationen unter „Kürzel“ kommen sie vom Rechner und
 * nicht aus dem Speicher dieses Handys: sie gelten für jeden Client, überleben
 * eine Neuinstallation der App und können mehr als Tasten. Bearbeitet werden
 * sie nur am Rechner selbst — über das Netz gibt es dafür keinen Weg, und das
 * ist der Grund, warum diese Liste überhaupt sicher ist.
 */
export function ActionsView({ agent, deviceName, onError }: Props): React.JSX.Element {
  const [actions, setActions] = useState<AgentActionSummary[] | undefined>(undefined)
  const [busy, setBusy] = useState<string | undefined>(undefined)
  const [asking, setAsking] = useState<AgentActionSummary | undefined>(undefined)

  useEffect(() => {
    let current = true

    void agent.getActions().then(
      (found) => {
        if (current) {
          setActions(found)
        }
      },
      (failure: unknown) => {
        if (current) {
          // Eine leere Liste statt `undefined`: sonst stünde für immer
          // „Lade…“ da, obwohl längst klar ist, dass nichts kommt.
          setActions([])
          onError(failure instanceof Error ? failure.message : String(failure))
        }
      },
    )

    return () => {
      current = false
    }
  }, [agent, onError])

  const invoke = async (action: AgentActionSummary): Promise<void> => {
    setAsking(undefined)
    setBusy(action.id)

    try {
      await agent.invokeAction(action.id)
    } catch (failure) {
      onError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(undefined)
    }
  }

  if (actions === undefined) {
    return <p className="placeholder">Lade Aktionen…</p>
  }

  if (actions.length === 0) {
    return (
      <p className="placeholder">
        {deviceName} bietet keine Aktionen an. Sie stehen in der{' '}
        <code>actions.json</code> auf jenem Rechner.
      </p>
    )
  }

  return (
    <div className="actions">
      <ul className="action-list">
        {actions.map((action) => (
          <li key={action.id}>
            <button
              type="button"
              disabled={busy !== undefined}
              onClick={() => {
                // Der Merker kommt vom Rechner und ist eine Bitte, keine
                // Sperre — der Agent führt auch ohne Rückfrage aus. Er schützt
                // vor dem verrutschten Daumen, nicht vor einem bösen Client.
                if (action.confirm) {
                  setAsking(action)
                  return
                }

                void invoke(action)
              }}
            >
              <span className="action-label">{action.label}</span>
              <span className="action-type">{action.type}</span>
            </button>
          </li>
        ))}
      </ul>

      {asking !== undefined && (
        <div className="action-confirm" role="alertdialog">
          <p>
            „{asking.label}“ auf {deviceName} ausführen?
          </p>
          <button type="button" onClick={() => void invoke(asking)}>
            Ausführen
          </button>
          <button type="button" className="secondary" onClick={() => setAsking(undefined)}>
            Abbrechen
          </button>
        </div>
      )}
    </div>
  )
}
