import { useCallback, useEffect, useRef, useState } from 'react'
import type { AgentClient } from '../lib/agentClient.ts'
import { ScreenChannel } from '../lib/screenChannel.ts'
import { storage } from '../lib/storage.ts'
import { WebRtcChannel } from '../lib/webrtcChannel.ts'
import {
  MIN_ZOOM,
  POINTER_ZOOM,
  applyPinch,
  clampScale,
  focusPointer,
  movePointer,
  panBy,
  toNormalized,
  type Point,
  type Viewport,
} from '../lib/screenGestures.ts'
import type { InputChannel } from '../lib/inputChannel.ts'
import type {
  ConnectionState,
  Device,
  Monitor,
  QualityMode,
  ScreenStats,
} from '../lib/types.ts'
import { MediaView } from './MediaView.tsx'
import { PowerView } from './PowerView.tsx'
import {
  KeyboardIcon,
  MediaIcon,
  MouseIcon,
  PowerIcon,
  ShortcutIcon,
  TextIcon,
} from './icons.tsx'
import { KeyboardControls } from './keyboard/KeyboardControls.tsx'
import { GesturePad } from './screen/GesturePad.tsx'
import { ShortcutSheet } from './screen/ShortcutSheet.tsx'
import { TextSheet } from './screen/TextSheet.tsx'
import { StreamSettings, type Transport } from './screen/StreamSettings.tsx'

/** Bildrate, die bei beiden Übertragungswegen angefragt wird. */
const TARGET_FPS = 30

const RESET_VIEWPORT: Viewport = { scale: MIN_ZOOM, offsetX: 0, offsetY: 0 }

/** Startpunkt des Zeigers beim Einschalten des Overlays. */
const CENTER: Point = { x: 0.5, y: 0.5 }

/** Was gerade unter dem Bild eingeblendet ist. */
type Sheet = 'none' | 'keyboard' | 'text' | 'shortcuts' | 'media' | 'power'

interface Props {
  device: Device
  agent: AgentClient
  input: InputChannel
  /**
   * Ob die Ansicht gerade sichtbar ist. Sie bleibt beim Tab-Wechsel bestehen,
   * damit der Bildstrom nicht jedes Mal neu aufgebaut werden muss.
   */
  visible: boolean
  onError: (message: string) => void
}

/** Beschriftung eines Monitor-Tabs. */
interface MonitorTab {
  index: number
  label: string
  size: string | undefined
}

/**
 * Baut die Tab-Leiste aus der Monitorliste des Agents.
 *
 * Kommt die Liste nicht durch, reicht die Anzahl aus der ersten Nachricht des
 * Bild-Sockets — dann fehlen nur die Auflösungen. Ohne diesen Rückfall stünde
 * man bei einem Problem mit `/api/info` ohne jede Möglichkeit da, den Monitor
 * zu wechseln.
 */
function describeTabs(monitors: Monitor[], count: number): MonitorTab[] {
  if (monitors.length > 0) {
    return monitors.map((monitor) => ({
      index: monitor.index,
      label: monitor.primary ? 'Haupt' : `Monitor ${monitor.index + 1}`,
      size: `${monitor.width}×${monitor.height}`,
    }))
  }

  return Array.from({ length: count }, (_, index) => ({
    index,
    label: `Monitor ${index + 1}`,
    size: undefined,
  }))
}

/**
 * Der Bildschirm des entfernten Rechners.
 *
 * Ohne Overlay klickt Tippen an der berührten Stelle, langes Drücken ist ein
 * Rechtsklick, zwei Finger scrollen oder zoomen. Das Zeiger-Overlay macht
 * daraus ein Touchpad mit Lupe, das Tastatur-Overlay blendet die Tastatur ein —
 * in beiden Fällen bleibt das Bild sichtbar.
 */
export function ScreenView({
  device,
  agent,
  input,
  visible,
  onError,
}: Props): React.JSX.Element {
  const [monitors, setMonitors] = useState<Monitor[]>([])
  const [monitorCount, setMonitorCount] = useState(0)
  const [active, setActive] = useState(() => storage.getDefaultMonitor(device.id) ?? 0)
  const [defaultMonitor, setDefaultMonitor] = useState(() =>
    storage.getDefaultMonitor(device.id),
  )
  const [connection, setConnection] = useState<ConnectionState>('connecting')
  const [stats, setStats] = useState<ScreenStats | undefined>(undefined)
  const [showStats, setShowStats] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const [unavailable, setUnavailable] = useState<string | undefined>(undefined)
  const [viewport, setViewport] = useState<Viewport>(RESET_VIEWPORT)
  const [quality, setQuality] = useState<QualityMode>('auto')
  const [transport, setTransport] = useState<Transport>(
    () => (storage.getTransport() === 'jpeg' ? 'jpeg' : 'webrtc'),
  )
  const [fellBack, setFellBack] = useState(false)
  // Die Tastatur schaltet den Zeiger mit ein: mit offener Tastatur will man den
  // Ausschnitt verschieben und klicken können, ohne sie wieder zuzuklappen.
  const [pointerOverlay, setPointerOverlay] = useState(false)
  const [sheet, setSheet] = useState<Sheet>('none')
  const [holding, setHolding] = useState(false)
  const [marker, setMarker] = useState<Point>(CENTER)

  // Auch die Handy-Tastatur schaltet den Zeiger mit ein: sonst müsste man zum
  // Klicken jedes Mal die Texteingabe zuklappen.
  const pointerActive = pointerOverlay || sheet === 'keyboard' || sheet === 'text'

  const canvasRef = useRef<HTMLCanvasElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const stageRef = useRef<HTMLDivElement>(null)
  const channelRef = useRef<ScreenChannel | undefined>(undefined)
  const webrtcRef = useRef<WebRtcChannel | undefined>(undefined)

  // Die Handler lesen Zoom und Zeigerposition bei jedem Touch-Event; über die
  // State-Werte hingen sie am letzten Render und wären während einer Geste
  // veraltet.
  const viewportRef = useRef<Viewport>(RESET_VIEWPORT)
  const pointerRef = useRef<Point>(CENTER)
  const holdingRef = useRef(false)

  const updateViewport = useCallback((next: Viewport): void => {
    viewportRef.current = next
    setViewport(next)
  }, [])

  /**
   * Zieht Ausschnitt und Zeigermarke auf die aktuelle Zeigerposition nach —
   * das Bild folgt dem Zeiger wie eine Lupe.
   */
  const alignToPointer = useCallback(
    (scale: number): void => {
      const media = transport === 'webrtc' ? videoRef.current : canvasRef.current
      const stage = stageRef.current

      if (media === null || stage === null) {
        return
      }

      const focus = focusPointer(
        pointerRef.current,
        scale,
        { width: media.offsetWidth, height: media.offsetHeight },
        { width: stage.clientWidth, height: stage.clientHeight },
      )

      updateViewport(focus.viewport)
      setMarker(focus.marker)
    },
    [transport, updateViewport],
  )

  // Beim Gerätewechsel bleibt diese Ansicht bestehen — der Monitor muss also
  // von Hand auf den Standard des neuen Geräts gestellt werden.
  useEffect(() => {
    const preferred = storage.getDefaultMonitor(device.id)

    setDefaultMonitor(preferred)
    setActive(preferred ?? 0)
  }, [device.id])

  // Monitorliste einmal pro Gerät holen — daraus entsteht die Auswahl.
  useEffect(() => {
    agent
      .getInfo()
      .then((info) => setMonitors(info.monitors))
      .catch((cause: unknown) => {
        onError(cause instanceof Error ? cause.message : String(cause))
      })
  }, [agent, onError])

  // Erst H.264 versuchen, dann JPEG. Der Effekt hängt bewusst nicht am
  // Monitor: bei H.264 wechselt der Monitor innerhalb der Verbindung, und ein
  // Neuaufbau würde die Übertragung für eine Sekunde abreißen lassen.
  useEffect(() => {
    const video = videoRef.current

    if (video === null || transport === 'jpeg') {
      return
    }

    let cancelled = false

    const channel = new WebRtcChannel(device, {
      onStats: setStats,
      onLost: () => {
        setFellBack(true)
        setTransport('jpeg')
      },
    })

    void channel
      .connect(video, active, TARGET_FPS)
      .then((connected) => {
        if (cancelled) {
          channel.close()
          return
        }

        webrtcRef.current = connected ? channel : undefined
        setFellBack(!connected)
        setTransport(connected ? 'webrtc' : 'jpeg')
      })
      .catch(() => {
        setFellBack(true)
        setTransport('jpeg')
      })

    return () => {
      cancelled = true
      channel.close()
      webrtcRef.current = undefined
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- siehe oben: der
    // Monitorwechsel läuft absichtlich ohne Neuaufbau.
  }, [device, transport])

  // JPEG-Stream — sowohl als Rückfall als auch, wenn ihn jemand ausdrücklich
  // auswählt.
  useEffect(() => {
    const canvas = canvasRef.current

    if (canvas === null || transport !== 'jpeg') {
      return
    }

    setUnavailable(undefined)
    updateViewport(RESET_VIEWPORT)

    const channel = new ScreenChannel(device, active, {
      onMeta: (meta) => {
        setUnavailable(undefined)
        setMonitorCount(meta.count)
      },
      onStats: setStats,
      onState: setConnection,
      onError,
      onAvailability: (available, reason) =>
        setUnavailable(available ? undefined : (reason ?? 'Bildschirm nicht verfügbar.')),
    })

    channel.attachCanvas(canvas)
    channel.connect()
    channelRef.current = channel

    return () => {
      channel.disconnect()
      channelRef.current = undefined
    }
  }, [device, active, transport, onError, updateViewport])

  // Nach einem Monitor- oder Transportwechsel stimmen die Bildmaße nicht mehr,
  // und beim Drehen des Handys ebenso wenig — dann muss der Ausschnitt neu auf
  // den Zeiger ausgerichtet werden.
  useEffect(() => {
    if (!pointerActive || !visible) {
      return
    }

    const realign = (): void => alignToPointer(viewportRef.current.scale)

    realign()
    window.addEventListener('resize', realign)

    return () => window.removeEventListener('resize', realign)
  }, [pointerActive, visible, active, transport, sheet, alignToPointer])

  // In der Hosentasche muss kein Bild übertragen werden.
  useEffect(() => {
    const onVisibilityChange = (): void => {
      if (document.hidden) {
        channelRef.current?.pause()
      } else {
        channelRef.current?.resume()
      }
    }

    document.addEventListener('visibilitychange', onVisibilityChange)

    return () => document.removeEventListener('visibilitychange', onVisibilityChange)
  }, [])

  // Eine gehaltene Maustaste darf nicht überleben, wenn die Ansicht verschwindet.
  useEffect(
    () => () => {
      if (holdingRef.current) {
        input.buttonUp('left')
      }
    },
    [input],
  )

  const stageSize = (): { width: number; height: number } => {
    const rect = stageRef.current?.getBoundingClientRect()

    return { width: rect?.width ?? 0, height: rect?.height ?? 0 }
  }

  /** Fingerposition → Position auf dem Monitor, 0..1. */
  const pointToMonitor = (point: Point): Point | undefined => {
    const media = transport === 'webrtc' ? videoRef.current : canvasRef.current
    const rect = media?.getBoundingClientRect()

    return rect === undefined ? undefined : toNormalized(point, rect)
  }

  /**
   * Monitorwechsel. Bei H.264 bleibt die Verbindung stehen und nur die Quelle
   * dahinter wechselt; scheitert das, geht es über den JPEG-Weg weiter.
   */
  const changeMonitor = (index: number): void => {
    setActive(index)
    pointerRef.current = CENTER

    if (pointerActive) {
      // Der Zeiger muss mit auf den neuen Monitor, sonst zeigt die Marke ins
      // Leere.
      input.moveTo(index, CENTER.x, CENTER.y)
    } else {
      updateViewport(RESET_VIEWPORT)
    }

    if (transport !== 'webrtc') {
      return
    }

    void webrtcRef.current?.switchMonitor(index).then((switched) => {
      if (!switched) {
        setTransport('jpeg')
      }
    })
  }

  const moveCursorTo = (point: Point): void => {
    const target = pointToMonitor(point)

    if (target !== undefined) {
      pointerRef.current = target
      input.moveTo(active, target.x, target.y)
    }
  }

  // ---- Fingergesten ---------------------------------------------------

  /** Wischen im Zeiger-Overlay schiebt den Zeiger, nicht das Bild. */
  const panPointer = (dx: number, dy: number): void => {
    const media = transport === 'webrtc' ? videoRef.current : canvasRef.current

    if (media === null) {
      return
    }

    const scale = viewportRef.current.scale
    const next = movePointer(
      pointerRef.current,
      dx,
      dy,
      { width: media.offsetWidth, height: media.offsetHeight },
      scale,
    )

    pointerRef.current = next
    input.moveTo(active, next.x, next.y)
    alignToPointer(scale)
  }

  /**
   * Wischen mit einem Finger. Gibt `true` zurück, wenn daraus kein Klick mehr
   * werden soll.
   */
  const handlePan = (dx: number, dy: number, point: Point): boolean => {
    if (pointerActive) {
      panPointer(dx, dy)
      return false
    }

    // Bei unverändertem Zoom folgt der Mauszeiger dem Finger. Nur wenn es etwas
    // zu verschieben gibt, wird stattdessen das Bild bewegt — sonst käme man an
    // gezoomte Bildränder nie heran.
    if (viewportRef.current.scale === MIN_ZOOM) {
      moveCursorTo(point)
      return false
    }

    updateViewport(panBy(viewportRef.current, dx, dy, stageSize()))
    return true
  }

  const handleZoom = (factor: number, center: Point): void => {
    // Im Overlay bleibt der Zeiger der Mittelpunkt, überall sonst der Punkt
    // zwischen den Fingern.
    if (pointerActive) {
      alignToPointer(clampScale(viewportRef.current.scale * factor))
      return
    }

    const rect = stageRef.current?.getBoundingClientRect()

    if (rect === undefined) {
      return
    }

    updateViewport(
      applyPinch(
        viewportRef.current,
        factor,
        { x: center.x - rect.left, y: center.y - rect.top },
        { width: rect.width, height: rect.height },
      ),
    )
  }

  const handleTap = (point: Point): void => {
    // Im Overlay steht der Zeiger schon da, wo er hin soll.
    if (!pointerActive) {
      moveCursorTo(point)
    }

    if (!holdingRef.current) {
      input.click('left')
    }
  }

  const handleLongPress = (point: Point): void => {
    if (!pointerActive) {
      moveCursorTo(point)
    }

    input.click('right')
  }

  /**
   * Setzt den Zeiger in die Bildmitte und zoomt hinein.
   *
   * Der Agent verrät nicht, wo der Mauszeiger steht — indem die App ihn selbst
   * setzt, weiß sie es ab diesem Moment genau.
   */
  const startPointer = (): void => {
    pointerRef.current = CENTER
    input.moveTo(active, CENTER.x, CENTER.y)
    alignToPointer(POINTER_ZOOM)
  }

  const applyOverlay = (nextPointer: boolean, nextSheet: Sheet): void => {
    setPointerOverlay(nextPointer)
    setSheet(nextSheet)
    navigator.vibrate?.(15)

    if (nextPointer || nextSheet === 'keyboard' || nextSheet === 'text') {
      startPointer()
      return
    }

    releaseHold()
    updateViewport(RESET_VIEWPORT)
  }

  const togglePointer = (): void => applyOverlay(!pointerOverlay, sheet)

  const toggleSheet = (next: Sheet): void =>
    applyOverlay(pointerOverlay, sheet === next ? 'none' : next)

  /**
   * Doppeltipp mit liegendem Finger: linke Taste halten, bis er hochgeht —
   * damit lassen sich Fenster ziehen und Text markieren, ohne dafür einen
   * eigenen Knopf in der Leiste zu brauchen.
   */
  const startHold = (point: Point): void => {
    if (!pointerActive) {
      moveCursorTo(point)
    }

    input.buttonDown('left')
    holdingRef.current = true
    setHolding(true)
  }

  const releaseHold = (): void => {
    if (holdingRef.current) {
      input.buttonUp('left')
      holdingRef.current = false
      setHolding(false)
    }
  }

  const tabs = describeTabs(monitors, monitorCount)

  const mediaTransform =
    `translate(${viewport.offsetX}px, ${viewport.offsetY}px) scale(${viewport.scale})`

  const changeQuality = (mode: QualityMode): void => {
    setQuality(mode)
    channelRef.current?.setQuality(mode)
  }

  return (
    <div className={sheet === 'none' ? 'screen-view' : 'screen-view compact'}>
      <div className="screen-bar">
        {tabs.length > 1 && (
          <select
            className="monitor-select"
            value={active}
            aria-label="Monitor"
            onChange={(event) => changeMonitor(Number(event.target.value))}
          >
            {tabs.map((tab) => (
              <option key={tab.index} value={tab.index}>
                {tab.size === undefined ? tab.label : `${tab.label} · ${tab.size}`}
              </option>
            ))}
          </select>
        )}

        <button
          type="button"
          className="stats-chip"
          onClick={() => setShowStats(!showStats)}
          title="Statistik"
        >
          {describeStats(stats, transport, showStats)}
        </button>

        <button
          type="button"
          className={showSettings ? 'icon-button active' : 'icon-button'}
          onClick={() => setShowSettings(!showSettings)}
          aria-label="Bildeinstellungen"
        >
          ⚙
        </button>
      </div>

      {showSettings && (
        <StreamSettings
          transport={transport}
          quality={quality}
          onTransport={(mode) => {
            setFellBack(false)
            setTransport(mode)
            storage.setTransport(mode)
          }}
          onQuality={changeQuality}
          isDefaultMonitor={defaultMonitor === active}
          onDefaultMonitor={() => {
            storage.setDefaultMonitor(device.id, active)
            setDefaultMonitor(active)
          }}
        />
      )}

      {/* Mit Zeiger-Overlay wird das Bild beschnitten statt verkleinert: es
          behält die volle Breite, der Ausschnitt folgt dem Zeiger. Sonst
          schrumpfte es bei offener Tastatur auf Briefmarkengröße. */}
      <div className={pointerActive ? 'screen-stage crop' : 'screen-stage'} ref={stageRef}>
        <video
          ref={videoRef}
          className="screen-canvas"
          hidden={transport !== 'webrtc'}
          autoPlay
          playsInline
          muted
          style={{ transform: mediaTransform }}
        />

        <canvas
          ref={canvasRef}
          className="screen-canvas"
          hidden={transport === 'webrtc'}
          style={{ transform: mediaTransform }}
        />

        {/* Der Stream zeigt den echten Mauszeiger nicht — im Overlay ist die
            Marke die einzige Rückmeldung, wo geklickt wird. */}
        {pointerActive && (
          <span
            className={holding ? 'pointer-marker holding' : 'pointer-marker'}
            style={{ left: `${marker.x}px`, top: `${marker.y}px` }}
            aria-hidden="true"
          />
        )}

        <GesturePad
          onPan={handlePan}
          onZoom={handleZoom}
          onScroll={(notches) => input.scroll(notches)}
          onTap={handleTap}
          onLongPress={handleLongPress}
          onHoldStart={startHold}
          onHoldEnd={releaseHold}
        />

        {transport === 'jpeg' && connection !== 'connected' && (
          <p className="screen-overlay">
            {connection === 'connecting' ? 'Verbinde…' : 'Getrennt — versuche erneut…'}
          </p>
        )}

        {unavailable !== undefined && <p className="screen-overlay">{unavailable}</p>}

        {fellBack && transport === 'jpeg' && (
          <p className="screen-notice">
            H.264 kam nicht zustande — läuft über JPEG weiter.
          </p>
        )}
      </div>

      {sheet === 'keyboard' && <KeyboardControls input={input} layout="overlay" />}

      {sheet === 'shortcuts' && (
        <div className="screen-sheet">
          <ShortcutSheet input={input} />
        </div>
      )}

      {sheet === 'media' && (
        <div className="screen-sheet">
          <MediaView agent={agent} deviceName={device.name} compact onError={onError} />
        </div>
      )}

      {sheet === 'power' && (
        <div className="screen-sheet">
          <PowerView agent={agent} deviceName={device.name} onError={onError} />
        </div>
      )}

      <div className="icon-bar">
        <button
          type="button"
          className={sheet === 'keyboard' ? 'bar-button active' : 'bar-button'}
          onClick={() => toggleSheet('keyboard')}
          aria-label="Tastatur"
        >
          <KeyboardIcon />
        </button>
        <button
          type="button"
          className={sheet === 'text' ? 'bar-button active' : 'bar-button'}
          onClick={() => toggleSheet('text')}
          aria-label="Text tippen"
        >
          <TextIcon />
        </button>
        <button
          type="button"
          className={sheet === 'shortcuts' ? 'bar-button active' : 'bar-button'}
          onClick={() => toggleSheet('shortcuts')}
          aria-label="Shortcuts"
        >
          <ShortcutIcon />
        </button>
        <button
          type="button"
          className={pointerActive ? 'bar-button active' : 'bar-button'}
          onClick={togglePointer}
          // Mit offener Tastatur ist die Zeigersteuerung ohnehin an.
          disabled={sheet === 'keyboard'}
          aria-label="Maus"
        >
          <MouseIcon />
        </button>
        <button
          type="button"
          className={sheet === 'media' ? 'bar-button active' : 'bar-button'}
          onClick={() => toggleSheet('media')}
          aria-label="Medien"
        >
          <MediaIcon />
        </button>
        <button
          type="button"
          className={sheet === 'power' ? 'bar-button active' : 'bar-button'}
          onClick={() => toggleSheet('power')}
          aria-label="Power"
        >
          <PowerIcon />
        </button>
      </div>

      {/* Das Feld sitzt unter der Symbolreihe — dort, wo die Handy-Tastatur
          gleich aufgeht. */}
      {sheet === 'text' && <TextSheet input={input} />}
    </div>
  )
}

/** Kurzfassung für die Leiste; angetippt zeigt sie alles, was der Agent meldet. */
function describeStats(
  stats: ScreenStats | undefined,
  transport: Transport,
  detailed: boolean,
): string {
  if (stats === undefined) {
    return '– fps'
  }

  if (!detailed) {
    return `${stats.fps} fps`
  }

  return transport === 'webrtc'
    ? `H.264 · ${stats.fps} fps · ${stats.kbps} kbit/s · ${stats.encoder ?? '?'}`
    : `JPEG · ${stats.fps} fps · ${stats.kbps} kbit/s · Q${stats.quality} · ` +
        `${Math.round(stats.scale * 100)} % · ${stats.mode}`
}
