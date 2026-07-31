import { useCallback, useEffect, useState } from 'react'
import type { HubClient } from '../lib/hubClient.ts'
import type { Device, DeviceStatus } from '../lib/types.ts'
import {
  type IconComponent,
  KeyboardIcon,
  MediaIcon,
  MouseIcon,
  PowerIcon,
  ScreenIcon,
  ShortcutIcon,
} from './icons.tsx'

/** Solange die Leiste offen ist, den Online-Zustand frisch halten. */
const POLL_INTERVAL_MS = 10_000

export type Page = 'screen' | 'mouse' | 'keyboard' | 'media' | 'power' | 'shortcuts'

const PAGES: { id: Page; label: string; icon: IconComponent }[] = [
  { id: 'screen', label: 'Bildschirm', icon: ScreenIcon },
  { id: 'mouse', label: 'Maus', icon: MouseIcon },
  { id: 'keyboard', label: 'Tastatur', icon: KeyboardIcon },
  { id: 'power', label: 'Power', icon: PowerIcon },
  { id: 'media', label: 'Medien', icon: MediaIcon },
  { id: 'shortcuts', label: 'Shortcuts', icon: ShortcutIcon },
]

interface Props {
  /** Fehlt, solange nur selbst gekoppelte Geräte da sind — dann gibt es keinen
   *  Online-Zustand abzufragen. */
  hub: HubClient | undefined
  devices: Device[]
  current: Device
  page: Page
  onDevice: (device: Device) => void
  onPage: (page: Page) => void
  onClose: () => void
}

/**
 * Die Seitenleiste hinter dem Burger-Menü: verbundenes Gerät, alle Geräte des
 * Hubs und die eigenständigen Seiten.
 *
 * Der Online-Zustand wird hier selbst abgefragt statt in der App gehalten — die
 * Leiste ist meist zu, und dann muss auch nichts nachgesehen werden.
 */
export function Sidebar({
  hub,
  devices,
  current,
  page,
  onDevice,
  onPage,
  onClose,
}: Props): React.JSX.Element {
  const [statuses, setStatuses] = useState<DeviceStatus[]>([])

  const refresh = useCallback((): void => {
    hub
      ?.getStatuses()
      .then(setStatuses)
      // Ein fehlgeschlagener Online-Check ist kein Grund für eine Meldung —
      // die Geräteliste selbst steht ja da.
      .catch(() => undefined)
  }, [hub])

  useEffect(() => {
    refresh()

    const timer = window.setInterval(refresh, POLL_INTERVAL_MS)

    return () => window.clearInterval(timer)
  }, [refresh])

  const isOnline = (id: string): boolean =>
    statuses.find((status) => status.id === id)?.online ?? false

  return (
    <div className="sidebar-backdrop" onClick={onClose} role="presentation">
      {/* Klicks in der Leiste dürfen sie nicht gleich wieder schließen. */}
      <nav
        className="sidebar"
        onClick={(event) => event.stopPropagation()}
        aria-label="Hauptmenü"
      >
        <div className="sidebar-device">
          <span className="status-dot online" />
          <span className="device-name">{current.name}</span>
        </div>

        <span className="sidebar-label">Geräte</span>
        {devices.map((device) => (
          <button
            key={device.id}
            type="button"
            className={device.id === current.id ? 'sidebar-entry active' : 'sidebar-entry'}
            onClick={() => {
              onDevice(device)
              onClose()
            }}
          >
            <span className={isOnline(device.id) ? 'status-dot online' : 'status-dot'} />
            <span className="device-name">{device.name}</span>
          </button>
        ))}

        <span className="sidebar-label">Ansichten</span>
        {PAGES.map(({ id, label, icon: Glyph }) => (
          <button
            key={id}
            type="button"
            className={id === page ? 'sidebar-entry active' : 'sidebar-entry'}
            onClick={() => {
              onPage(id)
              onClose()
            }}
          >
            <Glyph />
            <span className="device-name">{label}</span>
          </button>
        ))}
      </nav>
    </div>
  )
}
