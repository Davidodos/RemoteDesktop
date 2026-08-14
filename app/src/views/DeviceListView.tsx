import { useCallback, useEffect, useState } from 'react'
import { AgentClient } from '../lib/agentClient.ts'
import { deviceLabel } from '../lib/deviceNames.ts'
import { collectPeers } from '../lib/bothWays.ts'
import { forgetLocalDevice, renameLocalDevice } from '../lib/deviceSources.ts'
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
  /** Das gerade verbundene Gerät, falls eines verbunden ist. */
  current?: Device
  onSelect: (device: Device) => void
  onPair: () => void
  onError: (message: string) => void
  /** Nach Umbenennen oder Entfernen: die Liste, wie sie jetzt aussieht. */
  onDevices: (devices: Device[]) => void
  /**
   * Zurück zu den Einstellungen — gesetzt, wenn die Liste von dort aus
   * aufgerufen wurde. Ohne diesen Weg wäre sie eine Sackgasse: in der
   * Seitenleiste steht sie nicht mehr.
   */
  onBack?: () => void
  /** Zu den Einstellungen — solange noch kein Gerät verbunden ist. */
  onSettings?: () => void
}

/**
 * Die Geräteliste — zugleich die Verwaltung.
 *
 * <p>
 * Sie ist der Einstieg, solange nichts verbunden ist, **und** eine Seite im
 * Menü, während etwas verbunden ist. Beides dieselbe Ansicht: es gibt keinen
 * Grund, warum Umbenennen an einer Stelle geht und an der anderen nicht — und
 * zwei Listen, die dasselbe zeigen, laufen irgendwann auseinander.
 * </p>
 */
export function DeviceListView({
  devices,
  current,
  onSelect,
  onPair,
  onError,
  onDevices,
  onBack,
  onSettings,
}: Props): React.JSX.Element {
  const [statuses, setStatuses] = useState<DeviceStatus[]>([])

  const [waking, setWaking] = useState<string | undefined>(undefined)
  const [hinweis, setHinweis] = useState<string | undefined>(undefined)
  const [managed, setManaged] = useState<string | undefined>(undefined)

  /**
   * Beim Öffnen nachsehen, ob sich jemand hier gekoppelt hat.
   *
   * Eine Kopplung geht immer in beide Richtungen; wer sich an diesem Gerät
   * koppelt, hinterlässt dabei seinen Steckbrief. Der liegt auf Platte und hat
   * keine Frist — der Weg hierher ist deshalb kein Takt, sondern genau die
   * Stelle, an der jemand nachschaut, ob das Gerät schon da ist.
   */
  useEffect(() => {
    let alive = true

    void collectPeers().then(
      (all) => {
        if (all !== undefined && alive) {
          onDevices(all)
        }
      },
      (failure: unknown) => {
        if (alive) {
          onError(
            'Gekoppelte Geräte ließen sich nicht übernehmen: ' +
              (failure instanceof Error ? failure.message : String(failure)),
          )
        }
      },
    )

    return () => {
      alive = false
    }
    // Genau einmal beim Öffnen. `onDevices` wechselt bei jedem Rendern des
    // Aufrufers die Kennung und ist als Abhängigkeit eine Endlosschleife.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

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
      <div className="device-list-head">
        {onBack !== undefined && (
          <button type="button" className="link-button" onClick={onBack}>
            ‹ Einstellungen
          </button>
        )}

        <h1>Verbundene Geräte</h1>

        {onSettings !== undefined && (
          <button type="button" className="link-button" onClick={onSettings}>
            Einstellungen ›
          </button>
        )}
      </div>

      {hinweis !== undefined && (
        <p className="device-hint" role="status">
          {hinweis}
        </p>
      )}

      {devices.map((device) => {
        const erreichbar = isOnline(device.id)
        const kandidat = findWakeCandidate(device, devices, online)

        return (
          <div key={device.id} className="device-entry">
            <div className="device-card">
              <button
                type="button"
                className="device-main"
                // Ein Waker lässt sich nicht fernsteuern — er kann nur wecken.
                disabled={!erreichbar || device.waker === true}
                onClick={() => onSelect(device)}
              >
                <span className={erreichbar ? 'status-dot online' : 'status-dot'} />
                <span className="device-name">{deviceLabel(device)}</span>
                <span className="device-state">
                  {device.id === current?.id
                    ? 'verbunden'
                    : erreichbar
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

              <button
                type="button"
                className="manage-button"
                aria-expanded={managed === device.id}
                aria-label={`${deviceLabel(device)} verwalten`}
                onClick={() => setManaged(managed === device.id ? undefined : device.id)}
              >
                ⋯
              </button>
            </div>

            {managed === device.id && (
              <DevicePanel
                device={device}
                onDevices={onDevices}
                onClose={() => setManaged(undefined)}
              />
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

/**
 * Was sich an einem gekoppelten Gerät ändern lässt: sein Name hier, und ob es
 * überhaupt hierbleibt.
 *
 * <p>
 * Beides betrifft ausschließlich dieses Handy. Das Entfernen räumt den Eintrag
 * samt Zugangsdaten weg; die Kopplung am Rechner bleibt bestehen, bis sie dort
 * widerrufen wird. Genau das sagt der Satz darunter — sonst hielte man den
 * Rechner für gesichert, und er wäre es nicht.
 * </p>
 */
function DevicePanel({
  device,
  onDevices,
  onClose,
}: {
  device: Device
  onDevices: (devices: Device[]) => void
  onClose: () => void
}): React.JSX.Element {
  const [alias, setAlias] = useState(device.alias ?? '')
  const [note, setNote] = useState<string | undefined>(undefined)
  const [confirming, setConfirming] = useState(false)
  const [testing, setTesting] = useState(false)

  const test = async (): Promise<void> => {
    setTesting(true)
    setNote(undefined)

    try {
      const info = await new AgentClient(device).getInfo()

      setNote(
        `Erreichbar: ${info.hostname}${info.version === undefined ? '' : ` (Fassung ${info.version})`}.`,
      )
    } catch (failure) {
      setNote(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setTesting(false)
    }
  }

  return (
    <div className="device-panel">
      <label className="field-label" htmlFor={`alias-${device.id}`}>
        Name für diesen Rechner — gilt nur hier
      </label>
      <input
        id={`alias-${device.id}`}
        value={alias}
        onChange={(event) => setAlias(event.target.value)}
        placeholder={device.name}
      />

      <div className="device-panel-actions">
        <button
          type="button"
          className="secondary"
          onClick={() => {
            onDevices(renameLocalDevice(device.id, alias))
            setNote(
              alias.trim().length > 0
                ? `Heißt hier jetzt „${alias.trim()}“.`
                : `Heißt hier wieder „${device.name}“.`,
            )
          }}
        >
          Namen übernehmen
        </button>

        <button type="button" className="secondary" disabled={testing} onClick={() => void test()}>
          {testing ? 'Teste…' : 'Verbindung testen'}
        </button>

        <button
          type="button"
          className="danger"
          onClick={() => {
            if (!confirming) {
              setConfirming(true)
              return
            }

            onDevices(forgetLocalDevice(device.id))
            onClose()
          }}
        >
          {confirming ? 'Wirklich entfernen' : 'Entfernen'}
        </button>
      </div>

      {confirming && (
        <p className="device-hint">
          Damit ist dieser Rechner nur hier weg. Am Rechner selbst bleibt die Kopplung
          bestehen, bis du sie dort unter „Geräte“ widerrufst.
        </p>
      )}

      {note !== undefined && (
        <p className="device-hint" role="status">
          {note}
        </p>
      )}
    </div>
  )
}
