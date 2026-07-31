import { useCallback, useEffect, useState } from 'react'
import type { HubClient } from '../lib/hubClient.ts'
import type { Device, DeviceStatus } from '../lib/types.ts'

/** Nach dem Wecken dauert das Hochfahren; so lange häufiger nachfragen. */
const WAKE_POLL_INTERVAL_MS = 5000
const WAKE_POLL_DURATION_MS = 90_000

/** Normale Aktualisierung der Geräteliste. */
const IDLE_POLL_INTERVAL_MS = 15_000

interface Props {
  /** Fehlt, solange nur selbst gekoppelte Geräte da sind. */
  hub: HubClient | undefined
  devices: Device[]
  onSelect: (device: Device) => void
  onPair: () => void
  onError: (message: string) => void
}

export function DeviceListView({
  hub,
  devices,
  onSelect,
  onPair,
  onError,
}: Props): React.JSX.Element {
  const [statuses, setStatuses] = useState<DeviceStatus[]>([])
  const [waking, setWaking] = useState<string | undefined>(undefined)

  const refresh = useCallback(async (): Promise<void> => {
    if (hub === undefined) {
      return
    }

    try {
      setStatuses(await hub.getStatuses())
    } catch (error) {
      onError(error instanceof Error ? error.message : String(error))
    }
  }, [hub, onError])

  useEffect(() => {
    void refresh()

    const interval = waking === undefined ? IDLE_POLL_INTERVAL_MS : WAKE_POLL_INTERVAL_MS
    const timer = window.setInterval(() => void refresh(), interval)

    return () => window.clearInterval(timer)
  }, [refresh, waking])

  /**
   * `undefined` heißt „nicht nachgesehen": ohne Hub fragt niemand nach dem
   * Online-Zustand. Ein solches Gerät als offline darzustellen wäre eine
   * Behauptung — und der Knopf wäre gesperrt, obwohl der Rechner läuft.
   */
  const isOnline = (id: string): boolean | undefined =>
    hub === undefined ? undefined : (statuses.find((status) => status.id === id)?.online ?? false)

  /**
   * Kennt der Hub den Namen nicht, liegt es an seiner Namensauflösung und
   * nicht am Rechner — sonst sucht man den Fehler beim laufenden Gerät.
   */
  const hasNameProblem = (id: string): boolean =>
    statuses.find((status) => status.id === id)?.reason === 'dns'

  // Sobald das geweckte Gerät antwortet, zurück auf den ruhigen Takt.
  useEffect(() => {
    if (waking !== undefined && isOnline(waking) === true) {
      setWaking(undefined)
    }
  })

  const wake = async (device: Device): Promise<void> => {
    if (hub === undefined) {
      return
    }

    try {
      await hub.wake(device.id)
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

      {devices.map((device) => {
        const online = isOnline(device.id)

        return (
          <div key={device.id} className="device-card">
            <button
              type="button"
              className="device-main"
              disabled={online === false}
              onClick={() => onSelect(device)}
            >
              <span className={online === true ? 'status-dot online' : 'status-dot'} />
              <span className="device-name">{device.name}</span>
              <span className="device-state">
                {online === undefined
                  ? 'gekoppelt'
                  : online
                    ? 'online'
                    : waking === device.id
                      ? 'startet…'
                      : hasNameProblem(device.id)
                        ? 'Name unbekannt'
                        : 'offline'}
              </span>
            </button>

            {hasNameProblem(device.id) && (
              <p className="device-hint">
                Der Hub kann <code>{device.host}</code> nicht auflösen — auf der NAS
                fehlt der Tailscale-DNS. Am Gerät selbst liegt es nicht.
              </p>
            )}

            {online === false && device.canWake && (
              <button
                type="button"
                className="wake-button"
                disabled={waking === device.id}
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

      {hub !== undefined && (
        <button type="button" className="refresh-button" onClick={() => void refresh()}>
          Aktualisieren
        </button>
      )}
    </div>
  )
}
