import { useEffect, useMemo, useRef, useState } from 'react'
import { AgentClient } from './lib/agentClient.ts'
import { HubClient, HubError } from './lib/hubClient.ts'
import { InputChannel } from './lib/inputChannel.ts'
import { storage } from './lib/storage.ts'
import type { ConnectionState, Device } from './lib/types.ts'
import { DeviceListView } from './views/DeviceListView.tsx'
import { KeyboardView } from './views/KeyboardView.tsx'
import { MediaView } from './views/MediaView.tsx'
import { PowerView } from './views/PowerView.tsx'
import { ScreenView } from './views/ScreenView.tsx'
import { ShortcutsView } from './views/ShortcutsView.tsx'
import { Sidebar, type Page } from './views/Sidebar.tsx'
import { TouchpadView } from './views/TouchpadView.tsx'
import { MenuIcon } from './views/icons.tsx'

export function App(): React.JSX.Element {
  const [hubToken, setHubToken] = useState(storage.getHubToken())
  const [devices, setDevices] = useState<Device[]>([])
  const [selected, setSelected] = useState<Device | undefined>(undefined)
  const [page, setPage] = useState<Page>('screen')
  const [menuOpen, setMenuOpen] = useState(false)
  const [connection, setConnection] = useState<ConnectionState>('disconnected')
  const [error, setError] = useState<string | undefined>(undefined)

  const hub = useMemo(
    () => (hubToken === undefined ? undefined : new HubClient(hubToken)),
    [hubToken],
  )

  const inputRef = useRef<InputChannel | undefined>(undefined)

  // Geräteliste laden, sobald ein Token vorliegt.
  useEffect(() => {
    if (hub === undefined) {
      return
    }

    hub
      .getDevices()
      .then(setDevices)
      .catch((cause: unknown) => {
        if (cause instanceof HubError && cause.unauthorized) {
          // Token ist falsch oder wurde gewechselt — zurück zur Eingabe.
          storage.setHubToken(undefined)
          setHubToken(undefined)
        }

        setError(cause instanceof Error ? cause.message : String(cause))
      })
  }, [hub])

  // Eingabe-Socket an das gewählte Gerät binden.
  useEffect(() => {
    if (selected === undefined) {
      return
    }

    const channel = new InputChannel(selected, setConnection, setError)
    inputRef.current = channel
    channel.connect()

    return () => {
      channel.disconnect()
      inputRef.current = undefined
    }
  }, [selected])

  const agent = useMemo(
    () => (selected === undefined ? undefined : new AgentClient(selected)),
    [selected],
  )

  if (hubToken === undefined || hub === undefined) {
    return <TokenPrompt onSubmit={(token) => {
      storage.setHubToken(token)
      setHubToken(token)
      setError(undefined)
    }} error={error} />
  }

  if (selected === undefined) {
    return (
      <>
        <ErrorBanner message={error} onDismiss={() => setError(undefined)} />
        <DeviceListView
          hub={hub}
          devices={devices}
          onError={setError}
          onSelect={(device) => {
            storage.setLastDevice(device.id)
            setSelected(device)
          }}
        />
      </>
    )
  }

  const input = inputRef.current

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
        <span className="header-title">{selected.name}</span>
        <span className={`connection ${connection}`}>{describeConnection(connection)}</span>
      </header>

      <ErrorBanner message={error} onDismiss={() => setError(undefined)} />

      <main className={page === 'screen' ? 'app-body screen' : 'app-body'}>
        {/* Die Bildschirmansicht bleibt auch auf den anderen Seiten bestehen,
            nur unsichtbar: sonst würde der Videostrom bei jedem Wechsel neu
            aufgebaut, und das dauert Sekunden. */}
        {input !== undefined && agent !== undefined && (
          <div className="tab-panel" hidden={page !== 'screen'}>
            <ScreenView
              device={selected}
              agent={agent}
              input={input}
              visible={page === 'screen'}
              onError={setError}
            />
          </div>
        )}

        {input === undefined || agent === undefined ? (
          <p className="placeholder">Verbinde…</p>
        ) : page === 'mouse' ? (
          <TouchpadView input={input} />
        ) : page === 'keyboard' ? (
          <KeyboardView input={input} />
        ) : page === 'media' ? (
          <MediaView agent={agent} deviceName={selected.name} onError={setError} />
        ) : page === 'power' ? (
          <PowerView agent={agent} deviceName={selected.name} onError={setError} />
        ) : page === 'shortcuts' ? (
          <ShortcutsView />
        ) : null}
      </main>

      {menuOpen && (
        <Sidebar
          hub={hub}
          devices={devices}
          current={selected}
          page={page}
          onDevice={(device) => {
            storage.setLastDevice(device.id)
            setSelected(device)
            setPage('screen')
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

function TokenPrompt({
  onSubmit,
  error,
}: {
  onSubmit: (token: string) => void
  error: string | undefined
}): React.JSX.Element {
  const [value, setValue] = useState('')

  return (
    <form
      className="token-prompt"
      onSubmit={(event) => {
        event.preventDefault()

        if (value.trim().length > 0) {
          onSubmit(value.trim())
        }
      }}
    >
      <h1>RemoteDesktop</h1>
      <p>Hub-Token eingeben. Es steht in der <code>devices.json</code> auf der NAS.</p>

      {error !== undefined && <p className="error-text">{error}</p>}

      <input
        type="password"
        value={value}
        onChange={(event) => setValue(event.target.value)}
        placeholder="Hub-Token"
        autoCapitalize="off"
        autoCorrect="off"
        spellCheck={false}
      />
      <button type="submit">Anmelden</button>
    </form>
  )
}
