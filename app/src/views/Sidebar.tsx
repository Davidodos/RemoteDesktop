import { useCallback, useEffect, useState } from 'react'
import type { Capability } from '../lib/capabilities.ts'
import { probeAll } from '../lib/reachability.ts'
import type { Device, DeviceStatus } from '../lib/types.ts'
import { deviceLabel } from '../lib/deviceNames.ts'
import { getPlatform } from '../platform/index.ts'
import {
  type IconComponent,
  KeyboardIcon,
  MediaIcon,
  MouseIcon,
  PowerIcon,
  ScreenIcon,
  SettingsIcon,
  ShortcutIcon,
} from './icons.tsx'

/** Solange die Leiste offen ist, den Online-Zustand frisch halten. */
const POLL_INTERVAL_MS = 10_000

export type Page =
  | 'screen'
  | 'mouse'
  | 'keyboard'
  | 'media'
  | 'power'
  | 'actions'
  | 'shortcuts'
  /**
   * Die Geräteliste als Seite — erreichbar, **während** etwas verbunden ist.
   * Vorher führte der Weg zurück nur über das Trennen, und wer ein zweites
   * Gerät koppeln oder eines umbenennen wollte, musste die Sitzung aufgeben.
   *
   * In der Leiste steht sie nicht mehr selbst: der Weg führt über die
   * Einstellungen, wo auch alles andere steht, was die App betrifft.
   */
  | 'devices'
  | 'settings'
  /**
   * Die andere Richtung: dieses Gerät steuerbar machen. Erreichbar über die
   * Einstellungen, weil sie dieses Gerät betrifft und nicht das verbundene.
   */
  | 'share'

/**
 * Die Ansichten und die Fähigkeit, die jede von ihnen voraussetzt.
 *
 * Seit V4 steht am anderen Ende nicht mehr zwangsläufig ein Windows-Rechner.
 * Was das Gerät nicht kann, steht hier gar nicht erst — eine ausgegraute
 * Schaltfläche wäre nur eine Frage, auf die es keine Antwort gibt.
 */
const PAGES: { id: Page; label: string; icon: IconComponent; needs: Capability }[] = [
  { id: 'screen', label: 'Bildschirm', icon: ScreenIcon, needs: 'screen' },
  { id: 'mouse', label: 'Maus', icon: MouseIcon, needs: 'input' },
  { id: 'keyboard', label: 'Tastatur', icon: KeyboardIcon, needs: 'input' },
  { id: 'power', label: 'Power', icon: PowerIcon, needs: 'power' },
  { id: 'media', label: 'Medien', icon: MediaIcon, needs: 'media' },
  // Vom Zielrechner, nicht aus dem Speicher dieses Handys — deshalb über den
  // Shortcuts, die nur lokal gelten.
  { id: 'actions', label: 'Aktionen', icon: ShortcutIcon, needs: 'actions' },
  // Ein Shortcut ist eine gespeicherte Tastenkombination. Wo keine Tasten
  // ankommen, ist er ein Knopf, der nichts tut.
  { id: 'shortcuts', label: 'Shortcuts', icon: ShortcutIcon, needs: 'keys' },
]

/**
 * Ob diese Ansicht am verbundenen Gerät überhaupt etwas bewirkt.
 *
 * `devices` und `settings` stehen absichtlich nicht in {@link PAGES}: sie
 * betreffen die App und nicht das Gerät, und sie müssen gerade dann erreichbar
 * bleiben, wenn mit dem Gerät etwas nicht stimmt.
 */
export function pageAvailable(page: Page, abilities: readonly Capability[]): boolean {
  const entry = PAGES.find((candidate) => candidate.id === page)

  return entry === undefined || abilities.includes(entry.needs)
}

interface Props {
  devices: Device[]
  /** Das verbundene Gerät — `undefined`, solange keines verbunden ist. */
  current?: Device
  page: Page
  /** Was das verbundene Gerät kann — siehe `lib/capabilities.ts`. */
  abilities: readonly Capability[]
  onDevice: (device: Device) => void
  onPage: (page: Page) => void
  onClose: () => void
}

/**
 * Die Seitenleiste hinter dem Burger-Menü: die gekoppelten Geräte und der Weg
 * in die Einstellungen. Ist ein Gerät verbunden, kommen dessen Ansichten dazu.
 *
 * <p>
 * **Sie gibt es immer** — vorher erst, sobald etwas verbunden war. Damit war
 * der eine Weg, den die App durchgehend anbietet, genau dann verschwunden, wenn
 * man ihn braucht: bevor überhaupt etwas gekoppelt ist.
 * </p>
 *
 * <p>
 * Der Online-Zustand wird hier selbst abgefragt statt in der App gehalten — die
 * Leiste ist meist zu, und dann muss auch nichts nachgesehen werden.
 * </p>
 */
export function Sidebar({
  devices,
  current,
  page,
  abilities,
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
        {current !== undefined && (
          <div className="sidebar-device">
            <span className="status-dot online" />
            <span className="device-name">{deviceLabel(current)}</span>
          </div>
        )}

        <span className="sidebar-label">Geräte</span>

        {devices.length === 0 && (
          <span className="sidebar-label">Noch keins gekoppelt</span>
        )}

        {devices.map((device) => (
          <button
            key={device.id}
            type="button"
            className={device.id === current?.id ? 'sidebar-entry active' : 'sidebar-entry'}
            onClick={() => {
              onDevice(device)
              onClose()
            }}
          >
            <span className={isOnline(device.id) ? 'status-dot online' : 'status-dot'} />
            <span className="device-name">{deviceLabel(device)}</span>
          </button>
        ))}

        {/* Im Fenster nicht: dort steht „Einstellungen" in der nativen Leiste
            daneben, und alles dahinter — Autostart, Updates, Einrichtung,
            Netz — gehört ihr und nicht dieser Seite. Zwei Wege zu zwei
            verschiedenen Einstellungsseiten wären einer zu viel. */}
        {getPlatform().name !== 'webview2' && (
          <>
            <span className="sidebar-label">App</span>

            <button
              type="button"
              className={
                page === 'settings' || page === 'share'
                  ? 'sidebar-entry active'
                  : 'sidebar-entry'
              }
              onClick={() => {
                onPage('settings')
                onClose()
              }}
            >
              <SettingsIcon />
              <span className="device-name">Einstellungen</span>
            </button>
          </>
        )}

        {current !== undefined && <span className="sidebar-label">Ansichten</span>}
        {PAGES.filter(({ needs }) => abilities.includes(needs)).map(({ id, label, icon: Glyph }) => (
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
