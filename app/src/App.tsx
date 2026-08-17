import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AgentClient } from './lib/agentClient.ts'
import { collectPeers } from './lib/bothWays.ts'
import { capabilitiesOf, isTouchTarget } from './lib/capabilities.ts'
import {
  describeHotkey,
  hotkeyMatches,
  parseHotkey,
  serializeHotkey,
  type Hotkey,
} from './lib/hotkey.ts'
import { touchInputFor } from './lib/touchTyping.ts'
import { deviceLabel } from './lib/deviceNames.ts'
import {
  collectDevices,
  localDeviceSource,
  rememberContact,
  saveLocalDevice,
} from './lib/deviceSources.ts'
import { belongsToRemote, toAgentKey } from './lib/hardwareKeyboard.ts'
import { useIdentity } from './lib/ownName.ts'
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
import { ConnectionRequestView } from './views/ConnectionRequestView.tsx'
import { FirstRunView } from './views/FirstRunView.tsx'
import { PairingView } from './views/PairingView.tsx'
import { PowerView } from './views/PowerView.tsx'
import { ScreenView } from './views/ScreenView.tsx'
import { SettingsView } from './views/SettingsView.tsx'
import { ShareView } from './views/ShareView.tsx'
import { ActionsView } from './views/ActionsView.tsx'
import { ShortcutsView } from './views/ShortcutsView.tsx'
import { Sidebar, pageAvailable, type Page } from './views/Sidebar.tsx'
import { TakeoverSetup } from './views/TakeoverSetup.tsx'
import { TouchpadView } from './views/TouchpadView.tsx'
import { MenuIcon } from './views/icons.tsx'

/**
 * Die Rückfrage „darf dieses Gerät jetzt verbinden?" liegt über allem.
 *
 * Sie hier und nicht in einer Seite zu zeigen ist keine Kosmetik: am anderen
 * Ende wartet jemand, und die Frage läuft nach einer halben Minute ab. Eine
 * Karte, die man erst aufsuchen muss, wäre in den meisten Fällen abgelaufen,
 * bevor sie jemand sieht.
 */
export function App(): React.JSX.Element {
  return (
    <>
      <Shell />
      <ConnectionRequestView />
    </>
  )
}

/**
 * Wie oft in den eigenen Eingang gesehen wird.
 *
 * Ruhig, denn hier verfällt nichts: der Blick gilt nur dem Fall, dass jemand am
 * anderen Gerät koppelt, während dieses Fenster offen dasteht.
 */
const PEER_POLL_INTERVAL_MS = 10_000

function Shell(): React.JSX.Element {
  const [devices, setDevices] = useState<Device[]>([])
  const [selected, setSelected] = useState<Device | undefined>(undefined)
  const [pairing, setPairing] = useState(false)
  const [page, setPage] = useState<Page>('devices')
  const [menuOpen, setMenuOpen] = useState(false)
  const [connection, setConnection] = useState<ConnectionState>('disconnected')
  const [error, setError] = useState<string | undefined>(undefined)

  /**
   * Der eigene Name und der Erststart. Beide liegen nativ — siehe
   * `platform/identity.ts`.
   */
  const { state: identity, rename, finishFirstRun } = useIdentity()

  /**
   * Die Selbstauskunft des verbundenen Geräts. Daran hängt, welche Ansichten es
   * überhaupt gibt — ein Handy hat kein „Ein/Aus" und keine Aktionen.
   */
  const [info, setInfo] = useState<AgentInfo | undefined>(undefined)

  /**
   * Was das verbundene Gerät kann — gemerkt und nicht bei jedem Zeichnen neu
   * gerechnet: an dieser Liste hängt die Tastaturbehandlung, und eine
   * Abhängigkeit, die sich bei jedem Rendern erneuert, hängte den Anschlag jedes
   * Mal neu ein.
   */
  const abilities = useMemo(() => capabilitiesOf(info), [info])

  /** Ob am anderen Ende ein Finger erwartet wird — dann geht Text hinaus. */
  const touchTarget = isTouchTarget(abilities)

  /**
   * **Der Vollzugriff.** Solange er läuft, gehört diesem Rechner keine einzige
   * Eingabe mehr: Maus und Tastatur landen eins zu eins drüben. Geschaltet wird
   * er mit {@link hotkey}, und der ist die einzige Ausnahme.
   */
  const [takeover, setTakeover] = useState(false)

  /**
   * Das Kürzel dafür. `undefined` heißt: noch nie vergeben — dann wird beim
   * ersten Verbinden danach gefragt. Wo es die Übernahme nicht gibt (am Handy),
   * bleibt es dabei, und gefragt wird nie.
   */
  const [hotkey, setHotkey] = useState<Hotkey | undefined>(undefined)
  const [hotkeyLoaded, setHotkeyLoaded] = useState(false)
  /** Wer „Später" gesagt hat, wird in dieser Sitzung nicht noch einmal gefragt. */
  const [hotkeySkipped, setHotkeySkipped] = useState(false)

  const inputRef = useRef<InputChannel | undefined>(undefined)

  /** Zuletzt gemeldeter Fehlschlag beim Abholen — er wiederholt sich sonst im Takt. */
  const reported = useRef<string | undefined>(undefined)

  /**
   * Der Fingerabdruck des eigenen Agents. Daran — und ausdrücklich nicht am
   * Namen — erkennt die App, dass ein Ziel dieser Rechner selbst ist.
   */
  const eigenerFingerabdruck = useRef<string | undefined>(undefined)

  /**
   * Der eigene Name — der Notbehelf der Sperre, wo ein Fingerabdruck fehlt.
   *
   * In einem Ref und nicht als Abhängigkeit von `select`: die Sperre wird im
   * Augenblick des Verbindens gelesen, nicht beim Zeichnen. Seit der Name
   * wählbar ist, wäre `machineName` hier falsch — der Agent meldet unter
   * `/api/info` den gewählten und nicht mehr den von Windows.
   */
  const eigenerName = useRef<string | undefined>(undefined)

  useEffect(() => {
    eigenerName.current = identity?.name
  }, [identity])

  // Das Kürzel liegt nativ (siehe `platform/hotkey.ts`) und wird einmal beim
  // Start geholt. Antwortet die Umgebung nicht, gilt „noch keins" — dann fragt
  // die App eben noch einmal nach, statt eine Übernahme anzubieten, die sich
  // nicht beenden ließe.
  useEffect(() => {
    const setting = getPlatform().hotkey

    if (!setting.available) {
      setHotkeyLoaded(true)
      return
    }

    let current = true

    void setting.read().then(
      (stored) => {
        if (current) {
          setHotkey(parseHotkey(stored))
          setHotkeyLoaded(true)
        }
      },
      () => current && setHotkeyLoaded(true),
    )

    return () => {
      current = false
    }
  }, [])

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

  /**
   * Die andere Richtung: der eigene Ausweis hin, die Steckbriefe der anderen
   * her.
   *
   * <p>
   * **Und zwar im Takt.** Einmal beim Start genügte nicht, und das war ein
   * Denkfehler: der Normalfall ist, dass das Fenster **offen ist, während
   * drüben gekoppelt wird** — man scannt am Handy den QR-Code, den dieses
   * Fenster gerade anzeigt. Danach passierte hier nichts mehr. Der zweite
   * Abholpunkt, die Geräteliste, half nicht: solange nichts gekoppelt ist,
   * zeigt die App die Startkarte und rendert die Liste gar nicht.
   * </p>
   *
   * <p>
   * Es ist nicht der alte Takt zurück. Der jagte einem Kopplungscode
   * hinterher, der nach fünf Minuten verfiel — deshalb alle fünf Sekunden.
   * Hier verfällt nichts; nachgesehen wird nur, weil jemand hinsehen muss,
   * damit etwas erscheint. Der Aufruf geht an den eigenen Rechner und kostet
   * nichts, solange nichts da ist.
   * </p>
   */
  useEffect(() => {
    let current = true

    const look = (): void => void collectPeers().then(
      (all) => {
        if (all !== undefined && current) {
          setDevices(all)
        }
      },
      (failure: unknown) => {
        // Sichtbar, aber nur einmal je Fehlerbild: es still abzufangen war die
        // naheliegende Wahl — es ist ja eine Zugabe — und die falsche. Eine
        // Gegenrichtung, die stumm scheitert, sieht genauso aus wie eine, die
        // nie angeboten wurde.
        const message = failure instanceof Error ? failure.message : String(failure)

        if (current && message !== reported.current) {
          reported.current = message
          setError(`Gekoppelte Geräte ließen sich nicht übernehmen: ${message}`)
        }
      },
    )

    const tick = (): void => {
      // Der eigene Fingerabdruck, für die Sperre gegen Selbstverbindung. Er
      // kommt aus demselben Steckbrief, der beim Koppeln mitgeht, und wird hier
      // nur mitgenommen — eine eigene Anfrage dafür wäre eine zweite Quelle für
      // dieselbe Angabe.
      void getPlatform()
        .node.profile()
        .then(
          (self) => (eigenerFingerabdruck.current = self?.agentFingerprint),
          () => undefined,
        )

      look()
    }

    tick()

    const timer = window.setInterval(tick, PEER_POLL_INTERVAL_MS)

    return () => {
      current = false
      window.clearInterval(timer)
    }
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
        if (!current) {
          return
        }

        setInfo(fresh)

        // Aus derselben Antwort kommt beides: dass dieses Gerät gerade
        // erreichbar war, und was es ist. Ein Gerät, das vor Phase 31g
        // gekoppelt wurde, erfährt seine Plattform erst hier.
        const updated = rememberContact(selected.id, fresh.platform)

        if (updated !== undefined) {
          applyDevices(updated)
        }
      },
      () => undefined,
    )

    return () => {
      current = false
    }
  }, [selected, applyDevices])

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

    if (
      isSelfConnection(
        { name: probe.hostname, fingerprint: device.fingerprint },
        {
          name: eigenerName.current,
          fingerprint: eigenerFingerabdruck.current,
        },
      )
    ) {
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

  /**
   * Die Verbindung beenden — der Weg zurück in die Geräteliste.
   *
   * <p>
   * **Warum es den braucht:** es gab ihn nicht. Man kam in eine Sitzung hinein
   * und aus ihr nur wieder heraus, indem man ein anderes Gerät wählte oder die
   * App schloss. Am Rechner, wo seit 31j weder Symbolreihe noch Burger-Menü in
   * der Sitzung stehen, wäre das eine Sackgasse gewesen.
   * </p>
   */
  const disconnect = useCallback((): void => {
    setTakeover(false)
    setSelected(undefined)
    setInfo(undefined)
    setPage('devices')
    storage.setLastDevice(undefined)
  }, [])

  // Ein Gerätewechsel beendet die Übernahme. Sie gilt einer Verbindung, nicht
  // der App — sonst hinge die Maus nach dem Wechsel an einem Rechner, den
  // niemand mehr sieht.
  useEffect(() => setTakeover(false), [selected])

  /**
   * Die echte Tastatur — vier Fälle, in dieser Reihenfolge.
   *
   * <ol>
   * <li><b>Das Umschaltkürzel.</b> Es bleibt immer hier. Ginge es mit hinaus,
   *   gäbe es aus der Übernahme keinen Weg zurück.</li>
   * <li><b>Übernahme.</b> Alles hinaus, auch aus Eingabefeldern heraus — es
   *   gibt in diesem Zustand keine eigenen mehr, das Bild füllt den
   *   Bildschirm.</li>
   * <li><b>Ein Handy.</b> Dort ist ein Buchstabe kein Anschlag, sondern Text.
   *   Siehe `lib/touchTyping.ts` — davor stand bei jedem Zeichen eine
   *   Fehlermeldung.</li>
   * <li><b>Ein Rechner ohne Übernahme.</b> Wie bisher: was in ein Feld dieser
   *   App gehört, bleibt hier.</li>
   * </ol>
   */
  useEffect(() => {
    const input = inputRef.current
    const platform = getPlatform()

    if (selected === undefined || input === undefined || !platform.capabilities.physicalKeyboard) {
      return
    }

    const forward = (event: KeyboardEvent, down: boolean): void => {
      if (hotkey !== undefined && hotkeyMatches(event, hotkey)) {
        event.preventDefault()

        // Nur beim Drücken: das Loslassen desselben Griffs schaltete ihn sonst
        // sofort wieder zurück.
        if (down) {
          setTakeover((running) => !running)
        }

        return
      }

      if (takeover) {
        event.preventDefault()

        const key = toAgentKey(event.code)

        if (key === undefined) {
          return
        }

        if (down) {
          input.keyDown(key)
        } else {
          input.keyUp(key)
        }

        return
      }

      if (!belongsToRemote(event.target)) {
        return
      }

      if (touchTarget) {
        forwardToTouch(event, down)
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

    /**
     * Ein Handy nimmt Text an, keine Anschläge. Zwei Sonderfälle stehen hier
     * und nicht in `touchTyping.ts`: das Einfügen braucht die Zwischenablage,
     * und die gibt es nur über die Plattform.
     */
    const forwardToTouch = (event: KeyboardEvent, down: boolean): void => {
      if (!down) {
        // Geschickt wird beim Drücken. Ein zweites Mal beim Loslassen wäre
        // jeder Buchstabe doppelt.
        return
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'v') {
        event.preventDefault()

        void platform.clipboard.readText().then(
          (text) => text.length > 0 && input.typeText(text),
          () =>
            setError(
              'Die Zwischenablage ließ sich nicht lesen. Einmal ins Fenster klicken und '
              + 'erneut einfügen.',
            ),
        )

        return
      }

      const touch = touchInputFor(event)

      // Auch das Verschluckte wird abgefangen: F5 soll nicht nebenbei dieses
      // Fenster neu laden, nur weil drüben nichts damit anzufangen ist.
      event.preventDefault()

      if (touch === undefined) {
        return
      }

      if (touch.kind === 'text') {
        input.typeText(touch.text)
      } else {
        input.combo(touch.key)
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
  }, [selected, hotkey, takeover, touchTarget])

  // **Der erste Start** — Name und Freigabe, genau einmal. Solange die Antwort
  // von der Plattform noch aussteht, wird nichts gezeigt: eine Erststartfrage,
  // die für einen Bilddurchlauf aufblitzt, wäre schlimmer als eine, die kurz
  // auf sich warten lässt. Am Rechner meldet die Plattform `firstRunDone`
  // immer als erledigt — dort führt der Assistent des Fensters.
  if (identity === undefined) {
    return <p className="placeholder">Einen Moment…</p>
  }

  if (!identity.firstRunDone) {
    return (
      <FirstRunView
        suggestion={identity.name}
        rename={rename}
        onDone={() => void finishFirstRun()}
      />
    )
  }

  if (pairing) {
    return (
      <PairingView
        onCancel={() => setPairing(false)}
        onPaired={(all, paired, warnung) => {
          setDevices(all)
          setPairing(false)
          setError(warnung)

          // Der Name kommt aus `/api/info` des frisch gekoppelten Agents —
          // eine zweite Anfrage braucht es hier nicht.
          if (
            isSelfConnection(
              { name: paired.name, fingerprint: paired.fingerprint },
              {
                name: eigenerName.current,
                fingerprint: eigenerFingerabdruck.current,
              },
            )
          ) {
            setError(selfConnectionMessage(paired.name))
            return
          }

          storage.setLastDevice(paired.id)
          setSelected(paired)
          setPage('screen')
        }}
      />
    )
  }

  const input = inputRef.current
  const platform = getPlatform()

  /**
   * Ob dieses Fenster eine Maus hat — dann führt es durch die Sitzung mit Maus
   * und Tastatur und braucht weder Symbolreihe noch Burger-Menü dafür.
   */
  const withMouse = platform.capabilities.pointerLock

  /**
   * Ob jetzt nach dem Kürzel gefragt wird.
   *
   * Beim **ersten** Verbinden zu einem Rechner, und nur dann: das Kürzel
   * betrifft die Übernahme, und die gibt es nur zwischen zwei Rechnern. Bei
   * einem Handy hätte die Frage keinen Gegenstand.
   */
  const askForHotkey =
    platform.hotkey.available &&
    hotkeyLoaded &&
    hotkey === undefined &&
    !hotkeySkipped &&
    selected !== undefined &&
    !touchTarget

  /**
   * Die Seite, die wirklich zu sehen ist.
   *
   * <p>
   * Ohne verbundenes Gerät gibt es nur drei: die Liste, die Einstellungen und
   * die Freigabe. Mit einem bleibt der Wunsch in `page` stehen, auch wenn das
   * eben gewählte Gerät ihn nicht erfüllen kann — wer von einem Handy zurück
   * auf den PC wechselt, landet wieder auf „Ein/Aus", statt eine Seite neu
   * suchen zu müssen.
   * </p>
   *
   * <p>
   * Entschieden wird das hier und nicht in einem Effekt: sonst wäre ein
   * Bilddurchlauf lang die Seite zu sehen, die es dort gar nicht gibt.
   * </p>
   */
  const view: Page =
    selected === undefined
      ? page === 'settings' || page === 'share'
        ? page
        : 'devices'
      : pageAvailable(page, abilities)
        ? page
        : 'screen'

  return (
    <div className="app">
      {/* **Die Kopfzeile steht immer da.** Vorher entstand sie erst mit einer
          Verbindung — und damit war der einzige durchgehende Weg durch die App
          genau dann weg, wenn man ihn braucht: bevor etwas gekoppelt ist. */}
      <header className="app-header">
        {/* **Am Rechner gibt es das Menü in einer Sitzung nicht.** Alles
            darin — Maus, Tastatur, Shortcuts — liegt dort unter den Händen,
            und was bleibt (Medien, Ein/Aus, Aktionen) steht in der
            Geräteliste, einen Klick auf „Trennen" entfernt. Am Handy ist es
            der einzige Weg irgendwohin und deshalb immer da. */}
        {!(withMouse && selected !== undefined) && (
          <button
            type="button"
            className="icon-button"
            onClick={() => setMenuOpen(true)}
            aria-label="Menü öffnen"
          >
            <MenuIcon />
          </button>
        )}
        <span className="header-title">
          {selected === undefined ? 'RemoteDesktop' : deviceLabel(selected)}
        </span>
        {selected !== undefined && (
          <span className={`connection ${connection}`}>{describeConnection(connection)}</span>
        )}
        {/* Der Weg aus einer Sitzung heraus. Er steht in der Kopfzeile, weil
            die immer da ist — auch dann, wenn mit der Verbindung etwas nicht
            stimmt und alles andere ins Leere führt. */}
        {selected !== undefined && (
          <button type="button" className="link-button" onClick={disconnect}>
            Trennen
          </button>
        )}
      </header>

      <ErrorBanner message={error} onDismiss={() => setError(undefined)} />

      <main className={view === 'screen' ? 'app-body screen' : 'app-body'}>
        {/* Die Bildschirmansicht bleibt auch auf den anderen Seiten bestehen,
            nur unsichtbar: sonst würde der Videostrom bei jedem Wechsel neu
            aufgebaut, und das dauert Sekunden. */}
        {selected !== undefined && input !== undefined && agent !== undefined && (
          <div className="tab-panel" hidden={view !== 'screen'}>
            <ScreenView
              device={selected}
              agent={agent}
              input={input}
              abilities={abilities}
              visible={view === 'screen'}
              takeover={takeover}
              {...(hotkey === undefined ? {} : { takeoverHint: describeHotkey(hotkey) })}
              onTakeoverEnd={() => setTakeover(false)}
              onError={setError}
            />
          </div>
        )}

        {/* Die Geräteseite braucht keine Verbindung — sie ist der Weg zurück,
            gerade auch wenn die Verbindung hakt. */}
        {view === 'share' ? (
          <ShareView onBack={() => setPage('settings')} />
        ) : view === 'settings' ? (
          <SettingsView onShare={() => setPage('share')} />
        ) : view === 'devices' ? (
          <DeviceListView
            devices={devices}
            {...(selected === undefined ? {} : { current: selected })}
            onDevices={applyDevices}
            onError={setError}
            onPair={() => setPairing(true)}
            onSelect={(device) => {
              void select(device).then(() => setPage('screen'))
            }}
          />
        ) : input === undefined || agent === undefined ? (
          <p className="placeholder">Verbinde…</p>
        ) : view === 'mouse' ? (
          <TouchpadView input={input} />
        ) : view === 'keyboard' ? (
          <KeyboardView input={input} />
        ) : view === 'media' ? (
          <MediaView agent={agent} deviceName={selected!.name} onError={setError} />
        ) : view === 'power' ? (
          <PowerView agent={agent} deviceName={selected!.name} onError={setError} />
        ) : view === 'actions' ? (
          <ActionsView agent={agent} deviceName={selected!.name} onError={setError} />
        ) : view === 'shortcuts' ? (
          <ShortcutsView />
        ) : null}
      </main>

      {askForHotkey && (
        <TakeoverSetup
          onChoose={(chosen) => {
            setHotkey(chosen)

            void platform.hotkey.write(serializeHotkey(chosen)).catch((failure: unknown) => {
              // Gemerkt ist es trotzdem — für diese Sitzung. Dass es den
              // nächsten Start nicht überlebt, muss dastehen: sonst sucht
              // morgen jemand nach einem Kürzel, das er vergeben hat.
              setError(
                'Das Kürzel gilt nur für diesen Lauf — gespeichert wurde es nicht: '
                + (failure instanceof Error ? failure.message : String(failure)),
              )
            })
          }}
          onLater={() => setHotkeySkipped(true)}
        />
      )}

      {menuOpen && (
        <Sidebar
          devices={devices}
          {...(selected === undefined ? {} : { current: selected })}
          page={view}
          abilities={selected === undefined ? [] : abilities}
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
