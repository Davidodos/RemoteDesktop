import { useCallback, useEffect, useState } from 'react'
import { probeAll } from '../lib/reachability.ts'
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

export type Page = 'screen' | 'mouse' | 'keyboard' | 'media' | 'power' | 'actions' | 'shortcuts'

const PAGES: { id: Page; label: string; icon: IconComponent }[] = [
  { id: 'screen', label: 'Bildschirm', icon: ScreenIcon },
  { id: 'mouse', label: 'Maus', icon: MouseIcon },
  { id: 'keyboard', label: 'Tastatur', icon: KeyboardIcon },
  { id: 'power', label: 'Power', icon: PowerIcon },
  { id: 'media', label: 'Medien', icon: MediaIcon },
  // Vom Zielrechner, nicht aus dem Speicher dieses Handys — deshalb über den
  // Shortcuts, die nur lokal gelten.
  { id: 'actions', label: 'Aktionen', icon: ShortcutIcon },
  { id: 'shortcuts', label: 'Shortcuts', icon: ShortcutIcon },
]

interface Props {
  devices: Device[]
  current: Device
  page: Page
  onDevice: (device: Device) => void
  onPage: (page: Page) => void
  onClose: () => void
}

/**
 * Die Seitenleiste hinter dem Burger-Menü: verbundenes Gerät, alle bekannten
 * Geräte und die eigenständigen Seiten.
 *
 * Der Online-Zustand wird hier selbst abgefragt statt in der App gehalten — die
 * Leiste ist meist zu, und dann muss auch nichts nachgesehen werden.
 */
export function Sidebar({
  devices,
  current,
  page,
  onDevice,
  onPage,
  onClose,
}: Props): React.JSX.Element {
  const [statuses, setStatuses] = useState<DeviceStatus[]>([])

  const refresh = useCallback((): void => {
    // Ein fehlgeschlagener Online-Check ist kein Grund für eine Meldung — die
    // Geräteliste selbst steht ja da.
    probeAll(devices).then(setStatuses, () => undefined)
  }, [devices])

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
