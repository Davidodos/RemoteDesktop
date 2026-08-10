import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AgentClient } from './lib/agentClient.ts'
import { capabilitiesOf } from './lib/capabilities.ts'
import { deviceLabel } from './lib/deviceNames.ts'
import { collectDevices, localDeviceSource, saveLocalDevice } from './lib/deviceSources.ts'
import { belongsToRemote, toAgentKey } from './lib/hardwareKeyboard.ts'
import { InputChannel } from './lib/inputChannel.ts'
import { protocolMismatch } from './lib/protocol.ts'
import { isSelfConnection, selfConnectionMessage } from './lib/selfConnection.ts'
import { buildSurfaceBoard } from './lib/surfaceBoard.ts'
import { rememberSite, siteChanged } from './lib/wake.ts'
import { getPlatform } from './platform/index.ts'
import { storage } from './lib/storage.ts'
import type { AgentInfo, ConnectionState, Device } from './lib/types.ts'
import { DeviceListView } from './views/DeviceListView.tsx'
import { KeyboardView } from './views/KeyboardView.tsx'
import { MediaView } from './views/MediaView.tsx'
import { PairingView } from './views/PairingView.tsx'
import { PowerView } from './views/PowerView.tsx'
import { ScreenView } from './views/ScreenView.tsx'
import { SettingsView } from './views/SettingsView.tsx'
import { ShareView } from './views/ShareView.tsx'
import { ActionsView } from './views/ActionsView.tsx'
import { ShortcutsView } from './views/ShortcutsView.tsx'
import { Sidebar, pageAvailable, type Page } from './views/Sidebar.tsx'
import { TouchpadView } from './views/TouchpadView.tsx'
import { MenuIcon } from './views/icons.tsx'

export function App(): React.JSX.Element {
  const [devices, setDevices] = useState<Device[]>([])
  const [selected, setSelected] = useState<Device | undefined>(undefined)
  const [pairing, setPairing] = useState(false)
  const [page, setPage] = useState<Page>('screen')
  const [menuOpen, setMenuOpen] = useState(false)
  const [connection, setConnection] = useState<ConnectionState>('disconnected')
  const [error, setError] = useState<string | undefined>(undefined)

  /**
   * Die Selbstauskunft des verbundenen Geräts. Daran hängt, welche Ansichten es
   * überhaupt gibt — ein Handy hat kein „Ein/Aus" und keine Aktionen.
   */
  const [info, setInfo] = useState<AgentInfo | undefined>(undefined)

  const inputRef = useRef<InputChannel | undefined>(undefined)

  /**
   * Die Liste hat sich geändert — umbenannt oder entfernt.
   *
   * Ist das verbundene Gerät weg, endet auch die Verbindung: alles andere wäre
   * eine Sitzung zu einem Rechner, den die App gerade vergessen hat.
   */
  const applyDevices = useCallback((next: Device[]): void => {
    setDevices(next)

    setSelected((current) =>
      current === undefined
        ? undefined
        : next.find((device) => device.id === current.id),
    )
  }, [])

  // Geräteliste laden. Seit Phase 14 gibt es nur noch eine Quelle: was dieses
  // Gerät selbst gekoppelt hat. Die Registry auf der NAS ist weg — sie lieferte
  // die Agent-Tokens aller Rechner an jeden aus, der das Hub-Token kannte.
  useEffect(() => {
    void collectDevices([localDeviceSource()]).then(({ devices: found, failures }) => {
      setDevices(found)

      const failure = failures[0]

      if (failure !== undefined) {
        setError(failure instanceof Error ? failure.message : String(failure))
      }
    })
  }, [])

  // Eingabe-Socket an das gewählte Gerät binden.
  useEffect(() => {
    if (selected === undefined) {
      return
    }

    const channel = new InputChannel(selected, setConnection, setError)
    inputRef.current = channel
    channel.connect()

    // Am Handy hält ein Vordergrunddienst die Sitzung offen, sobald die App in
    // den Hintergrund geht; im Browser und im Windows-Fenster passiert hier
    // nichts. Scheitert er, läuft die Sitzung trotzdem — sie stirbt nur beim
    // Wegwischen, und genau das soll der Nutzer wissen.
    const platform = getPlatform()

    void platform.session.begin(selected.name).catch((failure: unknown) => {
      setError(
        `Die Sitzung bleibt im Hintergrund nicht offen: ${
          failure instanceof Error ? failure.message : String(failure)
        }`,
      )
    })

    return () => {
      channel.disconnect()
      inputRef.current = undefined

      // Beim Beenden gibt es nichts zu melden: die Verbindung ist ohnehin weg,
      // und ein Dienst, der sich nicht stoppen lässt, verschwindet spätestens
      // mit der App.
      void platform.session.end().catch(() => undefined)
    }
  }, [selected])

  const agent = useMemo(
    () => (selected === undefined ? undefined : new AgentClient(selected)),
    [selected],
  )

  /**
   * Nachfragen, was das Gerät kann.
   *
   * Diese eine Stelle hält die Auskunft — `select` fragt zwar auch, aber nur um
   * zu entscheiden, ob überhaupt verbunden wird, und über die Kopplung kommt
   * man ganz ohne sie hierher. Zwei Besitzer derselben Angabe wären zwei
   * Gelegenheiten, sie stehen zu lassen, wenn das Gerät wechselt.
   *
   * Bis die Antwort da ist, gilt die Liste von früher (siehe
   * `capabilitiesOf`) — also alles außer den Dateien. Andersherum wäre die
   * Leiste bei jedem Gerätewechsel für einen Moment fast leer.
   */
  useEffect(() => {
    setInfo(undefined)

    if (selected === undefined) {
      return
    }

    let current = true

    // Kommt die Auskunft nicht durch, gibt es hier nichts zu melden: der
    // Eingabe-Socket steht auf demselben Rechner und sagt es ohnehin.
    void new AgentClient(selected).getInfo().then(
      (fresh) => {
        if (current) {
          setInfo(fresh)
        }
      },
      () => undefined,
    )

    return () => {
      current = false
    }
  }, [selected])

  /**
   * Den Steckbrief für Widget, Tile und App-Kürzel nachführen.
   *
   * Er wird beim Verbinden geschrieben und nicht erst, wenn jemand die
   * Aktionen-Seite öffnet — ein Widget, das leer bleibt, bis man die App an der
   * richtigen Stelle besucht hat, wäre keins. Scheitert das Abfragen, bleibt der
   * alte Steckbrief stehen: eine Liste von gestern ist besser als keine, und
   * ausgelöst wird ohnehin nur, was der Rechner im Augenblick des Antippens
   * noch kennt.
   */
  useEffect(() => {
    if (selected === undefined || agent === undefined) {
      return
    }

    void agent.getActions().then(
      (actions) => {
        void getPlatform().surfaces.publish(buildSurfaceBoard(selected, actions, devices))
      },
      () => undefined,
    )
  }, [selected, agent, devices])

  /**
   * Ein Gerät wählen — erst nachfragen, wen man da vor sich hat.
   *
   * Die Auskunft kostet eine Anfrage, verhindert aber, dass ein Rechner sich
   * selbst fernsteuert. Danach wäre er nur noch von außen zu bändigen.
   */
  const select = useCallback(async (device: Device): Promise<void> => {
    if (device.waker === true) {
      setError(`${device.name} kann nur wecken — fernsteuern lässt er sich nicht.`)
      return
    }

    let probe

    try {
      probe = await new AgentClient(device).getInfo()
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
      return
    }

    if (isSelfConnection(probe.hostname, getPlatform().machineName)) {
      setError(selfConnectionMessage(probe.hostname))
      return
    }

    // Solange der Rechner wach ist, ist das der einzige Zeitpunkt, an dem sich
    // Standort und MAC erfahren lassen. Schläft er, sind sie die Grundlage
    // dafür, dass ihn überhaupt jemand wecken kann.
    const aktuell = rememberSite(device, probe)

    if (siteChanged(device, aktuell)) {
      setDevices(saveLocalDevice(aktuell))
    }

    setError(protocolMismatch(probe, device.name))
    storage.setLastDevice(aktuell.id)
    setSelected(aktuell)
  }, [])

  // Echte Tastatur durchreichen, sobald die Umgebung eine hat. Am Handy tut
  // das niemand — dort bleibt es bei der Bildschirmtastatur.
  useEffect(() => {
    const input = inputRef.current

    if (selected === undefined || input === undefined || !getPlatform().capabilities.physicalKeyboard) {
      return
    }

    const forward = (event: KeyboardEvent, down: boolean): void => {
      if (!belongsToRemote(event.target)) {
        return
      }

      const key = toAgentKey(event.code)

      if (key === undefined) {
        return
      }

      // Sonst löst der Browser seine eigenen Kürzel aus — Strg+W schlösse das
      // Fenster, statt am Zielrechner einen Tab zu schließen.
      event.preventDefault()

      if (down) {
        input.keyDown(key)
      } else {
        input.keyUp(key)
      }
    }

    const onDown = (event: KeyboardEvent): void => forward(event, true)
    const onUp = (event: KeyboardEvent): void => forward(event, false)

    window.addEventListener('keydown', onDown)
    window.addEventListener('keyup', onUp)

    return () => {
      window.removeEventListener('keydown', onDown)
      window.removeEventListener('keyup', onUp)
    }
  }, [selected])

  if (pairing) {
    return (
      <PairingView
        onCancel={() => setPairing(false)}
        onPaired={(all, paired) => {
          setDevices(all)
          setPairing(false)

          // Der Name kommt aus `/api/info` des frisch gekoppelten Agents —
          // eine zweite Anfrage braucht es hier nicht.
          if (isSelfConnection(paired.name, getPlatform().machineName)) {
            setError(selfConnectionMessage(paired.name))
            return
          }

          storage.setLastDevice(paired.id)
          setSelected(paired)
        }}
      />
    )
  }

  // Ohne ein einziges gekoppeltes Gerät gibt es nichts zu zeigen. Bis Phase 13
  // stand hier die Abfrage nach dem Hub-Token; mit der Registry auf der NAS ist
  // sie weggefallen — der Weg hinein ist jetzt für jeden Rechner derselbe.
  if (devices.length === 0) {
    return <WelcomeView onPair={() => setPairing(true)} error={error} />
  }

  if (selected === undefined) {
    return (
      <>
        <ErrorBanner message={error} onDismiss={() => setError(undefined)} />
        {/* Ohne verbundenes Gerät gibt es keine Seitenleiste. Damit die
            Einstellungen trotzdem erreichbar bleiben — dort steckt die
            Aktualisierung der App —, schalten die beiden Ansichten hier
            gegenseitig um. */}
        {page === 'share' ? (
          <ShareView onBack={() => setPage('settings')} />
        ) : page === 'settings' ? (
          <SettingsView
            onDevices={() => setPage('devices')}
            onShare={() => setPage('share')}
          />
        ) : (
          <DeviceListView
            devices={devices}
            onDevices={applyDevices}
            onError={setError}
            onPair={() => setPairing(true)}
            onSelect={(device) => void select(device)}
            onSettings={() => setPage('settings')}
          />
        )}
      </>
    )
  }

  const input = inputRef.current
  const abilities = capabilitiesOf(info)

  /**
   * Die Seite, die wirklich zu sehen ist.
   *
   * Der Wunsch bleibt in `page` stehen, auch wenn das eben gewählte Gerät ihn
   * nicht erfüllen kann — wer von einem Handy zurück auf den PC wechselt,
   * landet wieder auf „Ein/Aus", statt eine Seite neu suchen zu müssen.
   * Entschieden wird das hier und nicht in einem Effekt: sonst wäre ein
   * Bilddurchlauf lang die Seite zu sehen, die es dort gar nicht gibt.
   */
  const view = pageAvailable(page, abilities) ? page : 'screen'

  return (
    <div className="app">
      <header className="app-header">
        <button
          type="button"
          className="icon-button"
          onClick={() => setMenuOpen(true)}
          aria-label="Menü öffnen"
        >
          <MenuIcon />
        </button>
        <span className="header-title">{deviceLabel(selected)}</span>
        <span className={`connection ${connection}`}>{describeConnection(connection)}</span>
      </header>

      <ErrorBanner message={error} onDismiss={() => setError(undefined)} />

      <main className={view === 'screen' ? 'app-body screen' : 'app-body'}>
        {/* Die Bildschirmansicht bleibt auch auf den anderen Seiten bestehen,
            nur unsichtbar: sonst würde der Videostrom bei jedem Wechsel neu
            aufgebaut, und das dauert Sekunden. */}
        {input !== undefined && agent !== undefined && (
          <div className="tab-panel" hidden={view !== 'screen'}>
            <ScreenView
              device={selected}
              agent={agent}
              input={input}
              visible={view === 'screen'}
              onError={setError}
            />
          </div>
        )}

        {/* Die Geräteseite braucht keine Verbindung — sie ist der Weg zurück,
            gerade auch wenn die Verbindung hakt. */}
        {view === 'share' ? (
          <ShareView onBack={() => setPage('settings')} />
        ) : view === 'settings' ? (
          <SettingsView
            onDevices={() => setPage('devices')}
            onShare={() => setPage('share')}
          />
        ) : view === 'devices' ? (
          <DeviceListView
            devices={devices}
            current={selected}
            onDevices={applyDevices}
            onError={setError}
            onPair={() => setPairing(true)}
            onSelect={(device) => {
              void select(device).then(() => setPage('screen'))
            }}
            onBack={() => setPage('settings')}
          />
        ) : input === undefined || agent === undefined ? (
          <p className="placeholder">Verbinde…</p>
        ) : view === 'mouse' ? (
          <TouchpadView input={input} />
        ) : view === 'keyboard' ? (
          <KeyboardView input={input} />
        ) : view === 'media' ? (
          <MediaView agent={agent} deviceName={selected.name} onError={setError} />
        ) : view === 'power' ? (
          <PowerView agent={agent} deviceName={selected.name} onError={setError} />
        ) : view === 'actions' ? (
          <ActionsView agent={agent} deviceName={selected.name} onError={setError} />
        ) : view === 'shortcuts' ? (
          <ShortcutsView />
        ) : null}
      </main>

      {menuOpen && (
        <Sidebar
          devices={devices}
          current={selected}
          page={view}
          abilities={abilities}
          onDevice={(device) => {
            void select(device).then(() => setPage('screen'))
          }}
          onPage={setPage}
          onClose={() => setMenuOpen(false)}
        />
      )}
    </div>
  )
}

function describeConnection(state: ConnectionState): string {
  return state === 'connected' ? 'verbunden' : state === 'connecting' ? 'verbinde…' : 'getrennt'
}

function ErrorBanner({
  message,
  onDismiss,
}: {
  message: string | undefined
  onDismiss: () => void
}): React.JSX.Element | null {
  if (message === undefined) {
    return null
  }

  return (
    <div className="error-banner" role="alert">
      <span>{message}</span>
      <button type="button" onClick={onDismiss} aria-label="Meldung schließen">
        ✕
      </button>
    </div>
  )
}

/**
 * Der Einstieg, solange nichts gekoppelt ist.
 *
 * Vorher stand hier die Abfrage nach dem Hub-Token. Sie ist mit der Registry
 * auf der NAS weggefallen: es gibt kein Geheimnis mehr, das für alle Rechner
 * zugleich gilt — gekoppelt wird bei jedem einzeln.
 */
function WelcomeView({
  onPair,
  error,
}: {
  onPair: () => void
  error: string | undefined
}): React.JSX.Element {
  return (
    <div className="token-prompt">
      <h1>RemoteDesktop</h1>
      <p>
        Noch kein Gerät gekoppelt. Am Rechner den Kopplungscode anzeigen lassen und hier
        eintippen — oder den QR-Code scannen.
      </p>

      {error !== undefined && <p className="error-text">{error}</p>}

      <button type="button" onClick={onPair}>
        Gerät koppeln
      </button>
    </div>
  )
}
