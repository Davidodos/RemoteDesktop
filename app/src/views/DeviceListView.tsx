import { useCallback, useEffect, useState } from 'react'
import { AgentClient } from '../lib/agentClient.ts'
import { onlineIds, probeAll } from '../lib/reachability.ts'
import { explainMissingCandidate, findWakeCandidate } from '../lib/wake.ts'
import type { Device, DeviceStatus } from '../lib/types.ts'

/** Nach dem Wecken dauert das Hochfahren; so lange häufiger nachfragen. */
const WAKE_POLL_INTERVAL_MS = 5000
const WAKE_POLL_DURATION_MS = 90_000

/** Normale Aktualisierung der Geräteliste. */
const IDLE_POLL_INTERVAL_MS = 15_000

interface Props {
  devices: Device[]
  onSelect: (device: Device) => void
  onPair: () => void
  onError: (message: string) => void
}

export function DeviceListView({ devices, onSelect, onPair, onError }: Props): React.JSX.Element {
  const [statuses, setStatuses] = useState<DeviceStatus[]>([])
  const [waking, setWaking] = useState<string | undefined>(undefined)
  const [hinweis, setHinweis] = useState<string | undefined>(undefined)

  /**
   * Wer antwortet, fragt die App selbst — es gibt keinen Hub mehr, der das
   * reihum erledigt. Das ist ohnehin die ehrlichere Auskunft: dass die NAS
   * einen Rechner erreicht, heißt nicht, dass das Handy es auch tut.
   */
  const refresh = useCallback(async (): Promise<void> => {
    setStatuses(await probeAll(devices))
  }, [devices])

  useEffect(() => {
    void refresh()

    const interval = waking === undefined ? IDLE_POLL_INTERVAL_MS : WAKE_POLL_INTERVAL_MS
    const timer = window.setInterval(() => void refresh(), interval)

    return () => window.clearInterval(timer)
  }, [refresh, waking])

  const isOnline = (id: string): boolean =>
    statuses.find((status) => status.id === id)?.online ?? false

  // Sobald das geweckte Gerät antwortet, zurück auf den ruhigen Takt.
  useEffect(() => {
    if (waking !== undefined && isOnline(waking)) {
      setWaking(undefined)
    }
  })

  const online = onlineIds(statuses)

  const wake = async (device: Device): Promise<void> => {
    const candidate = findWakeCandidate(device, devices, online)

    if (candidate === undefined) {
      // Kein Fehler, sondern eine Auskunft — siehe `wake.ts`.
      setHinweis(explainMissingCandidate(device))
      return
    }

    try {
      await new AgentClient(candidate.device).wake(device.mac!)
      setWaking(device.id)

      // Falls der Rechner nicht hochkommt, nicht ewig schnell pollen.
      window.setTimeout(() => setWaking(undefined), WAKE_POLL_DURATION_MS)
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    }
  }

  return (
    <div className="device-list">
      <h1>Geräte</h1>

      {hinweis !== undefined && (
        <p className="device-hint" role="status">
          {hinweis}
        </p>
      )}

      {devices.map((device) => {
        const erreichbar = isOnline(device.id)
        const kandidat = findWakeCandidate(device, devices, online)

        return (
          <div key={device.id} className="device-card">
            <button
              type="button"
              className="device-main"
              // Ein Waker lässt sich nicht fernsteuern — er kann nur wecken.
              disabled={!erreichbar || device.waker === true}
              onClick={() => onSelect(device)}
            >
              <span className={erreichbar ? 'status-dot online' : 'status-dot'} />
              <span className="device-name">{device.name}</span>
              <span className="device-state">
                {erreichbar
                  ? device.waker === true
                    ? 'Waker'
                    : 'online'
                  : waking === device.id
                    ? 'startet…'
                    : 'offline'}
              </span>
            </button>

            {!erreichbar && device.waker !== true && (
              <button
                type="button"
                className="wake-button"
                // Ausgegraut statt weg: der Knopf soll erklären, warum er
                // nicht geht, statt kommentarlos zu fehlen.
                disabled={waking === device.id || kandidat === undefined}
                title={kandidat === undefined ? explainMissingCandidate(device) : undefined}
                onClick={() => void wake(device)}
              >
                {waking === device.id ? '…' : 'Wecken'}
              </button>
            )}
          </div>
        )
      })}

      <button type="button" className="pair-button" onClick={onPair}>
        Gerät koppeln
      </button>

      <button type="button" className="refresh-button" onClick={() => void refresh()}>
        Aktualisieren
      </button>
    </div>
  )
}
