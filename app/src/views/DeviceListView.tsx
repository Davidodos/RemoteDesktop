import { useCallback, useEffect, useState } from 'react'
import { AgentClient } from '../lib/agentClient.ts'
import { deviceLabel } from '../lib/deviceNames.ts'
import { collectPeers } from '../lib/bothWays.ts'
import { renameLocalDevice } from '../lib/deviceSources.ts'
import { describeReport, testConnection, type ConnectionReport } from '../lib/connectionTest.ts'
import { removeDevice } from '../lib/removeDevice.ts'
import {
  canUpdateRemotely,
  compareVersions,
  describeMatch,
  fetchVersion,
  ownVersion,
  type VersionMatch,
} from '../lib/versions.ts'
import { onlineIds, probeAll } from '../lib/reachability.ts'
import { explainMissingCandidate, findWakeCandidate } from '../lib/wake.ts'
import { ComputerIcon, PencilIcon, PhoneIcon } from './icons.tsx'
import type { Device, DeviceStatus } from '../lib/types.ts'

/** Nach dem Wecken dauert das Hochfahren; so lange häufiger nachfragen. */
const WAKE_POLL_INTERVAL_MS = 5000
const WAKE_POLL_DURATION_MS = 90_000

/**
 * Normale Aktualisierung der Geräteliste.
 *
 * <p>
 * **Fünfzehn Sekunden waren zu lang.** Wer einen Rechner hochfährt und
 * daneben in der Liste steht, wartet dabei bis zu einer Viertelminute auf einen
 * Punkt, der grün wird — lange genug, um an der Liste zu zweifeln statt am
 * Rechner. Die Abfrage selbst kostet je Gerät eine Anfrage an `/health` mit
 * drei Sekunden Zeitlimit, und alle laufen nebeneinander.
 * </p>
 */
const IDLE_POLL_INTERVAL_MS = 4000

interface Props {
  devices: Device[]
  /** Das gerade verbundene Gerät, falls eines verbunden ist. */
  current?: Device
  onSelect: (device: Device) => void
  onPair: () => void
  onError: (message: string) => void
  /** Nach Umbenennen oder Entfernen: die Liste, wie sie jetzt aussieht. */
  onDevices: (devices: Device[]) => void
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
}: Props): React.JSX.Element {
  const [statuses, setStatuses] = useState<DeviceStatus[]>([])

  /**
   * Die Fassung je Gerät — und die eigene, gegen die verglichen wird.
   *
   * Gehalten wird sie hier und nicht im Gerät: sie beschreibt einen Augenblick
   * und nicht die Kopplung. Ein Gerät, das gerade aktualisiert wird, hat für ein
   * paar Sekunden gar keine.
   */
  const [versions, setVersions] = useState<Record<string, string>>({})
  const [mine, setMine] = useState<string | undefined>(undefined)

  const [waking, setWaking] = useState<string | undefined>(undefined)

  /** Welches Gerät gerade umbenannt wird — höchstens eines. */
  const [renaming, setRenaming] = useState<string | undefined>(undefined)
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

  // Die eigene Fassung ändert sich nicht, solange diese App läuft: ein Update
  // startet sie neu.
  useEffect(() => {
    let alive = true

    void ownVersion().then((found) => {
      if (alive) {
        setMine(found)
      }
    })

    return () => {
      alive = false
    }
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

  /**
   * Die Fassung eines Geräts holen — einmal, sobald es antwortet.
   *
   * <p>
   * **Nicht im Takt.** Der Aufruf geht über `/api/info` und damit über eine
   * Anmeldung; ihn alle fünfzehn Sekunden für jedes Gerät zu wiederholen wäre
   * eine Menge Verkehr für eine Zahl, die sich zwischen zwei Updates nie ändert.
   * Neu geholt wird sie, wenn ein Gerät zurückkommt — und genau dann ist sie
   * auch interessant, weil ein Update es gerade neu gestartet haben könnte.
   * </p>
   */
  const forget = useCallback((id: string): void => {
    setVersions((known) => {
      const rest = { ...known }

      delete rest[id]

      return rest
    })
  }, [])

  useEffect(() => {
    let alive = true

    const fehlend = devices.filter(
      (device) =>
        device.waker !== true &&
        isOnline(device.id) &&
        versions[device.id] === undefined,
    )

    for (const device of fehlend) {
      void fetchVersion(device).then((found) => {
        if (alive && found !== undefined) {
          setVersions((known) => ({ ...known, [device.id]: found }))
        }
      })
    }

    return () => {
      alive = false
    }
    // `isOnline` liest aus `statuses`; die Abhängigkeit steht deshalb dort.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [devices, statuses, versions])

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
        <h1>Geräte</h1>
      </div>

      {/* Ohne Warnfarbe. „Noch kein Gerät gekoppelt" ist keine Störung, sondern
          der Anfang — und ein roter Balken um diesen Satz ließ den ersten Start
          der App aussehen wie einen fehlgeschlagenen. */}
      {devices.length === 0 && (
        <p className="device-note">Noch kein Gerät gekoppelt.</p>
      )}

      {hinweis !== undefined && (
        <p className="device-hint" role="status">
          {hinweis}
        </p>
      )}

      {devices.map((device) => {
        const erreichbar = isOnline(device.id)
        const kandidat = findWakeCandidate(device, devices, online)

        const fassung = versions[device.id]
        const stand: VersionMatch = compareVersions(mine, fassung)

        return (
          <div key={device.id} className="device-entry">
            <div className="device-card">
              {/* Der anklickbare Teil endet hinter dem Namen — der Stift
                  daneben ist ein eigener Knopf, und ein Knopf in einem Knopf
                  gibt es im HTML nicht. */}
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
                  {/* Die Fassung nur, wenn sie etwas sagt. Steht dort dieselbe
                      wie hier, ist das die Antwort auf eine Frage, die niemand
                      gestellt hat — außer man hat gerade eine gestellt, weil
                      sich das Gerät seltsam verhält. Deshalb steht sie da, aber
                      unauffällig; und wenn sie abweicht, fällt sie auf. */}
                  {fassung !== undefined && (
                    <span
                      className={
                        stand === 'older' ? 'device-version outdated' : 'device-version'
                      }
                    >
                      {describeMatch(stand, fassung)}
                    </span>
                  )}
                </span>
              </button>

              {/* Der Stift steht am Namen und nicht am Rand der Karte: er
                  ändert genau das Wort links von sich, und wo er dafür stehen
                  muss, ist damit auch schon beantwortet. */}
              <button
                type="button"
                className="rename-button"
                aria-expanded={renaming === device.id}
                aria-label={`${deviceLabel(device)} umbenennen`}
                title="Namen ändern"
                onClick={() => setRenaming(renaming === device.id ? undefined : device.id)}
              >
                <PencilIcon size={14} />
              </button>

              {/* Zustand und Weckknopf übereinander und nicht nebeneinander:
                  der Knopf gehört zu genau dem Wort über ihm — „offline" ist
                  der Grund, warum es ihn gibt. Nebeneinander schob er bei jedem
                  schlafenden Gerät die Karte auseinander, und die Liste sah bei
                  jeder Zeile anders aus. */}
              <span className="device-status">
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

                {/* **Bei einem Handy gar nicht.** Ausgegraut heißt „geht gerade
                    nicht" und lädt dazu ein, den Grund zu suchen; hier gibt es
                    keinen zu finden: ein Handy hört im Schlaf auf kein Magic
                    Packet, und daran ändert auch kein Netz etwas. */}
                {!erreichbar && device.waker !== true && device.platform !== 'android' && (
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
              </span>

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

            {renaming === device.id && (
              <RenameRow
                device={device}
                onDevices={onDevices}
                onClose={() => setRenaming(undefined)}
              />
            )}

            {managed === device.id && (
              <DevicePanel
                device={device}
                reachable={erreichbar}
                match={stand}
                onSelect={onSelect}
                onDevices={onDevices}
                onUpdated={() => forget(device.id)}
                onClose={() => setManaged(undefined)}
              />
            )}
          </div>
        )
      })}

      {/* Ein Knopf für beide Richtungen: die Seite dahinter bietet dieses
          Gerät an und nimmt zugleich die Daten eines anderen entgegen. Zwei
          Knöpfe hätten eine Entscheidung verlangt, die vorher niemand treffen
          kann — beim Koppeln tun immer beide Seiten etwas. */}
      {/* Kein „Aktualisieren" daneben: die Liste fragt von allein nach, im
          Takt von {@link IDLE_POLL_INTERVAL_MS} und nach dem Wecken schneller.
          Ein Knopf, der nur vorzieht, was ohnehin gleich passiert, sieht aus
          wie eine Bedingung — als ginge es ohne ihn nicht. */}
      <button type="button" className="pair-button" onClick={onPair}>
        Gerät koppeln
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
  match,
  onSelect,
  onDevices,
  onUpdated,
  onClose,
}: {
  device: Device
  reachable: boolean
  /** Ob dort dieselbe Fassung läuft wie hier — daran hängt der Update-Knopf. */
  match: VersionMatch
  onSelect: (device: Device) => void
  onDevices: (devices: Device[]) => void
  /** Nach einem angestoßenen Update: die gemerkte Fassung ist nichts mehr wert. */
  onUpdated: () => void
  onClose: () => void
}): React.JSX.Element {
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

  /**
   * Den Rechner drüben aktualisieren — von hier aus.
   *
   * <p>
   * **Warum das von hier aus gehen muss.** Ein Rechner aktualisiert sich beim
   * Start, und ein Rechner, der wochenlang durchläuft, startet nicht. Wer ihn
   * gerade steuert, sitzt aber nicht vor ihm — sonst bräuchte er die
   * Fernsteuerung nicht. Der Weg dorthin führte bis jetzt über einen Gang ins
   * Nebenzimmer.
   * </p>
   *
   * <p>
   * Windows fragt dabei nichts: der Agent startet den Installer mit den Rechten,
   * die er ohnehin hat. Ein Handy geht nicht und steht deshalb gar nicht erst
   * zur Wahl — siehe {@link canUpdateRemotely}.
   * </p>
   */
  const update = async (): Promise<void> => {
    setBusy(true)
    setNote(undefined)

    try {
      const report = await new AgentClient(device).updateApp()

      setNote(report.message)

      if (report.status === 'installing') {
        // Die gemerkte Fassung gilt nicht mehr, und die neue steht erst fest,
        // wenn der Rechner wieder da ist.
        onUpdated()
      }
    } catch (failure) {
      setNote(failure instanceof Error ? failure.message : String(failure))
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
      <div className="device-panel-actions">
        <button
          type="button"
          className="secondary"
          disabled={!steuerbar || device.waker === true}
          title={
            device.waker === true
              ? 'Ein Waker kann nur wecken.'
              : steuerbar
                ? undefined
                : 'Antwortet nicht oder gibt weder Bild noch Eingabe frei.'
          }
          onClick={() => onSelect(device)}
        >
          Verbinden
        </button>

        <button type="button" className="secondary" disabled={busy} onClick={() => void test()}>
          {busy ? 'Teste…' : 'Verbindung testen'}
        </button>

        {/* Nur, wenn es etwas zu aktualisieren gibt. Ein Knopf, der bei
            gleichem Stand dasteht, lädt dazu ein, ihn zu drücken — und der
            Rechner wäre danach eine Minute lang weg, ohne dass sich etwas
            geändert hätte. */}
        {reachable && canUpdateRemotely(device, match) && (
          <button
            type="button"
            className="secondary"
            disabled={busy}
            title="Lädt den Installer und führt ihn dort aus. Der Rechner ist danach kurz weg."
            onClick={() => void update()}
          >
            {busy ? 'Aktualisiere…' : 'Aktualisieren'}
          </button>
        )}

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
          Die Kopplung geht auf beiden Seiten weg.
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
 * Der Name dieses Geräts — hier und sonst nirgends.
 *
 * Er gehört diesem Gerät und nicht der Kopplung: drüben ändert sich davon
 * nichts, und kein anderes Gerät erfährt davon. Ein leeres Feld ist deshalb
 * kein Fehler, sondern die Rückkehr zu dem Namen, den die Gegenseite selbst
 * meldet.
 */
function RenameRow({
  device,
  onDevices,
  onClose,
}: {
  device: Device
  onDevices: (devices: Device[]) => void
  onClose: () => void
}): React.JSX.Element {
  const [alias, setAlias] = useState(device.alias ?? '')

  return (
    <form
      className="device-rename"
      onSubmit={(event) => {
        event.preventDefault()
        onDevices(renameLocalDevice(device.id, alias))
        onClose()
      }}
    >
      <label className="field-label" htmlFor={`alias-${device.id}`}>
        Name — gilt nur hier
      </label>

      {/* Das Feld steht allein in seiner Zeile. Neben zwei Knöpfen blieb ihm am
          Handy so wenig Breite, dass vom getippten Namen nichts mehr zu sehen
          war — und ein Feld, in dem man den eigenen Text nicht liest, ist
          keines. */}
      <input
        id={`alias-${device.id}`}
        type="text"
        value={alias}
        onChange={(event) => setAlias(event.target.value)}
        placeholder={device.name}
        autoFocus
      />

      <div className="device-rename-row">
        <button type="submit" className="secondary">
          Übernehmen
        </button>

        <button type="button" className="secondary" onClick={onClose}>
          Abbrechen
        </button>
      </div>
    </form>
  )
}

