import { useCallback, useEffect, useState } from 'react'
import { AgentClient } from '../lib/agentClient.ts'
import { deviceLabel } from '../lib/deviceNames.ts'
import { collectPeers } from '../lib/bothWays.ts'
import { renameLocalDevice } from '../lib/deviceSources.ts'
import { testConnection, type ConnectionReport } from '../lib/connectionTest.ts'
import { removeDevice } from '../lib/removeDevice.ts'
import { onlineIds, probeAll } from '../lib/reachability.ts'
import { explainMissingCandidate, findWakeCandidate } from '../lib/wake.ts'
import { lastSeen } from '../lib/lastSeen.ts'
import { ComputerIcon, PhoneIcon } from './icons.tsx'
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

        // Nur, solange das Gerät nicht erreichbar ist. Steht daneben „online",
        // ist „zuletzt verbunden: gerade eben" keine Auskunft, sondern Lärm.
        const zuletzt = lastSeen(device.lastConnectedAt)

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

                {/* Was das Gerät ist — aus der Kopplung, nicht aus einer
                    Anfrage: das Symbol steht auch dann da, wenn das Gerät
                    gerade aus ist. Fehlt die Angabe, ist die Gegenstelle älter
                    als Phase 31g; dann steht dort nichts, statt zu raten. */}
                {device.platform !== undefined && (
                  <span className="device-platform">
                    {device.platform === 'android' ? (
                      <PhoneIcon size={16} />
                    ) : (
                      <ComputerIcon size={16} />
                    )}
                  </span>
                )}

                <span className="device-title">
                  <span className="device-name">{deviceLabel(device)}</span>
                  {!erreichbar && zuletzt !== undefined && (
                    <span className="device-seen">zuletzt verbunden {zuletzt}</span>
                  )}
                </span>
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
                reachable={erreichbar}
                onSelect={onSelect}
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
  reachable,
  onSelect,
  onDevices,
  onClose,
}: {
  device: Device
  reachable: boolean
  onSelect: (device: Device) => void
  onDevices: (devices: Device[]) => void
  onClose: () => void
}): React.JSX.Element {
  const [alias, setAlias] = useState(device.alias ?? '')
  const [note, setNote] = useState<string | undefined>(undefined)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [report, setReport] = useState<ConnectionReport | undefined>(undefined)

  const test = async (): Promise<void> => {
    setBusy(true)
    setNote(undefined)

    try {
      const fresh = await testConnection(device)

      setReport(fresh)
      setNote(describeReport(device, fresh))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (): Promise<void> => {
    setBusy(true)

    try {
      const { devices, rest } = await removeDevice(device)

      onDevices(devices)

      if (rest === undefined) {
        onClose()
        return
      }

      // Nicht schließen: der Satz über den Rest drüben wäre sonst weg, bevor
      // ihn jemand gelesen hat.
      setNote(rest)
      setConfirming(false)
    } finally {
      setBusy(false)
    }
  }

  // Ausgegraut, solange es nichts zu verbinden gibt. Ein Knopf, der nur eine
  // Fehlermeldung erzeugt, ist schlimmer als keiner.
  //
  // Was der Test herausgefunden hat, schlägt die bloße Erreichbarkeit: ein
  // Handy ohne Bedienungshilfe antwortet, lässt sich aber nicht steuern, und
  // ein Rechner ohne laufenden Agent antwortet gar nicht erst.
  const steuerbar =
    report === undefined
      ? reachable
      : report.reachable &&
        (report.capabilities.length === 0 ||
          report.capabilities.includes('screen') ||
          report.capabilities.includes('input'))

  return (
    <div className="device-panel">
      <label className="field-label" htmlFor={`alias-${device.id}`}>
        Name für dieses Gerät — gilt nur hier
      </label>
      <input
        id={`alias-${device.id}`}
        value={alias}
        onChange={(event) => setAlias(event.target.value)}
        placeholder={device.name}
      />

      <p className="device-hint">
        {device.platform === 'android'
          ? 'Ein Handy.'
          : device.platform === 'windows'
            ? 'Ein Rechner mit Windows.'
            : 'Was für ein Gerät das ist, hat es beim Koppeln nicht gesagt — ' +
              'dort läuft eine ältere Fassung.'}{' '}
        {lastSeen(device.lastConnectedAt) === undefined
          ? 'Verbunden war dieses Gerät hier noch nie.'
          : `Zuletzt verbunden ${lastSeen(device.lastConnectedAt)}.`}
      </p>

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

        <button
          type="button"
          className="secondary"
          disabled={!steuerbar || device.waker === true}
          title={
            device.waker === true
              ? 'Ein Waker kann wecken und sonst nichts.'
              : steuerbar
                ? undefined
                : 'Dieses Gerät antwortet gerade nicht oder gibt weder Bild noch Eingabe frei.'
          }
          onClick={() => onSelect(device)}
        >
          Verbinden
        </button>

        <button type="button" className="secondary" disabled={busy} onClick={() => void test()}>
          {busy ? 'Teste…' : 'Verbindung testen'}
        </button>

        <button
          type="button"
          className="danger"
          disabled={busy}
          onClick={() => {
            if (!confirming) {
              setConfirming(true)
              return
            }

            void remove()
          }}
        >
          {confirming ? 'Wirklich entfernen' : 'Entfernen'}
        </button>
      </div>

      {confirming && (
        <p className="device-hint">
          Damit ist die Kopplung <strong>auf beiden Seiten</strong> weg: dieses Gerät
          verschwindet hier aus der Liste, und drüben verliert es das Recht, diesen
          Rechner zu steuern. Ist das andere Gerät gerade aus, wird hier trotzdem
          entfernt — dann bleibt drüben ein Eintrag stehen, und das steht danach hier.
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

/**
 * Aus dem Bericht wird ein Satz, der weiterhilft.
 *
 * „Antwortet nicht" verschweigt, ob es am Netz, am Vertrauen oder an einer
 * fehlenden Freigabe liegt — und genau danach sucht man dann an der falschen
 * Stelle.
 */
function describeReport(device: Device, report: ConnectionReport): string {
  const hin = !report.reachable
    ? `Nicht erreichbar: ${report.failure ?? 'kein Grund genannt'}`
    : report.scopesThere === undefined
      ? `${report.hostname ?? device.name} antwortet, kennt dieses Gerät aber nicht mehr — ` +
        'dort ist die Kopplung weg. Neu koppeln.'
      : `${report.hostname ?? device.name} antwortet. Dieses Gerät darf dort: ` +
        `${report.scopesThere.join(', ') || 'nichts'}.`

  const her =
    device.peerClientId === undefined
      ? 'Umgekehrt gibt es nichts: beim Koppeln hat die Gegenseite keinen Ausweis ' +
        'mitgeschickt, dieses Gerät lässt sich von dort also nicht steuern.'
      : report.scopesHere === undefined
        ? 'Umgekehrt steht dieses Gerät nicht bereit: die Gegenseite steht hier nicht ' +
          'in der Liste der zugelassenen Geräte. Noch einmal koppeln hilft.'
        : `Umgekehrt darf sie hier: ${report.scopesHere.join(', ') || 'nichts'}.`

  return `${hin} ${her}`
}
