# RemoteDesktop — Umsetzungsphasen

Reihenfolge ist so gewählt, dass nach Phase 2 schon etwas real Nutzbares auf
dem Handy läuft.

## Phase 0 — Grundgerüst
- [ ] Repo-Struktur: `agent/` (C#), `hub/` (Node/TS), `app/` (React/Vite)
- [ ] `git init`, `.gitignore`, `.env.example`
- [ ] Tailscale-Status prüfen: läuft es auf PC, Laptop, Handy? MagicDNS + HTTPS-Zertifikate im Admin-Panel aktiviert?

## Phase 1 — Agent: Steuerung ohne Bild
Das Fundament, komplett ohne Video — dadurch früh testbar.
- [x] .NET-8-Projekt, Kestrel auf :8443, Token-Auth-Middleware
- [x] `tailscale cert` einbinden (Laden + Anleitung zur Erneuerung in `agent/README.md`)
- [x] `GET /api/info` — Hostname + Monitor-Enumeration (`EnumDisplayMonitors`)
- [x] `POST /api/power` — sleep / shutdown / restart / lock
- [x] `POST /api/media` — Media-Keys via `SendInput` / `VK_MEDIA_*`
- [x] `WS /ws/input` — Maus absolut + relativ, Klicks, Down/Up getrennt (für Halten), Scroll, Tastatur mit Modifiern
- [x] Unit-Tests für Event-Parsing, Tastennamen und Koordinaten-Mapping — 64 Tests, alle grün
- [x] Build und Tests verifiziert; `dotnet publish -c Release -r win-x64 --self-contained` erzeugt eine 92-MB-Single-File-`.exe`
- [x] Autostart als Aufgabe bei der Anmeldung (`agent/README.md`, Schritt 7) — `-User` braucht `$env:USERDOMAIN\$env:USERNAME`, sonst `HRESULT 0x80070057`
- [ ] Tray-Icon fehlt noch (das Fenster läuft sichtbar mit)
- [ ] Auf echter Windows-Hardware gegenprüfen: Monitor-Enumeration, Klick-Genauigkeit bei Display-Skalierung, Media-Keys

## Phase 2 — Hub + PWA-Grundgerüst
- [x] Hub: Express 5 + TypeScript, Geräte-Registry aus `devices.json` (Zod-validiert)
- [x] `POST /api/wol/:device` — Magic Packet ohne Fremd-Dependency, an Port 7 und 9
- [x] `GET /api/devices/status` — Online-Probe, parallel, 2 s Timeout
- [x] Dockerfile (3 Stufen) + Compose für Dockhand-Stack `remotedesktop`
- [x] PWA: Vite 8 + React 19 + Manifest + Service Worker + Icons, Homescreen-installierbar
- [x] Geräteauswahl-View mit Online-Status und WOL-Button, schnelleres Polling nach dem Wecken
- [x] Touchpad-View: relative Maus, Tap/Long-Press, Zwei-Finger-Scroll, drei Buttons, Halte-Toggle
- [x] Tastatur-View: Modifier-Leiste, Sondertasten, F1–F12, Kombinationen, Freitext
- [x] Power- und Medien-View mit Bestätigungsdialog
- [x] „Läuft gerade": Titel, Interpret, Titelbild und Status je App aus der Windows-Medienübersicht; bei mehreren Quellen ist die Ziel-App auswählbar, dann gehen die Befehle direkt dorthin statt über die Medien-Taste
- [x] Hub-Tests (35) und Ende-zu-Ende-Smoke-Test der API grün
- [x] Tests für die App-Logik: jsdom-Umgebung in `vite.config.ts`, 40 Tests für Coalescing, Befehlsreihenfolge und Gesten-Rechnungen
- [x] Dabei gefunden und behoben: ein Klick konnte **vor** der Bewegung ankommen, die ihn positioniert (`InputChannel.flushPendingMove`)
- [x] App und Hub laufen, PC wird online erkannt

**→ Hier ist das erste Mal alles außer Bildschirm nutzbar.**

## Phase 3 — Bildschirm, Stufe 1 (JPEG über WebSocket)
- [x] Desktop Duplication API über Vortice (D3D11 + DXGI), Monitor per Index über den Windows-Gerätenamen
- [x] Neuaufbau der Aufnahme bei Auflösungswechsel, UAC-Dialog und Sperrbildschirm; die App bekommt `unavailable`/`available`
- [x] Dirty- und Move-Rects, auf das JPEG-Raster ausgerichtet und zu höchstens 8 Ausschnitten verschmolzen; ab 60 % geänderter Fläche ein Vollbild
- [x] Ohne Änderung geht nichts raus — Standbild kostet keine Bandbreite
- [x] JPEG-Encoding über GDI+ statt WIC (steckt schon in Windows, Skalierung inklusive), adaptive Qualität und Auflösung in sechs Stufen mit Hysterese
- [x] `WS /ws/screen?monitor=N&fps=30`, Binärprotokoll mit 8-Byte-Kopf, Befehle `refresh`/`pause`/`resume`/`quality`
- [x] PWA: Canvas-Renderer in Monitorauflösung, Monitor-Tabs, Tippen klickt an der berührten Stelle
- [x] Long-Press = Rechtsklick, Zwei-Finger-Scroll, Pinch-Zoom mit Pan, Ziehen-Modus für Drag-and-Drop
- [x] Stream pausiert automatisch, sobald die App in den Hintergrund geht
- [x] Bandbreiten-/FPS-Anzeige (fps, kbit/s, Qualität, Skalierung) zuschaltbar
- [x] 34 neue Unit-Tests für Rect-Verschmelzung, Qualitätsregelung und Frame-Kopf — insgesamt 98 grün
- [ ] **Offen: auf echter Hardware prüfen** — die Aufnahme selbst lässt sich nur auf Windows testen

**→ Hier ist die komplette Wunschliste erfüllt.**

## Phase 4 — Autoklicker und Komfort (zurückgestellt)
- [ ] Autoklicker: Intervall, Klicktyp, Start/Stop, optional Position fixieren
- [ ] Makros: gespeicherte Tastenkombinationen als Buttons
- [ ] Zuletzt genutztes Gerät und Monitor merken
- [ ] Haptisches Feedback bei Klicks

## Phase 5 — Bildschirm, Stufe 2 (WebRTC / H.264)
- [x] ffmpeg `ddagrab` mit ausdrücklich benannter Grafikkarte (`d3d11va=dx:N`) — sonst schwarzes Bild auf Rechnern mit zwei GPUs
- [x] Encoder-Erkennung durch Ausprobieren statt Listen-Parsen: `h264_nvenc` → `h264_qsv` → `h264_amf` → `libx264`, der erste mit echtem Bild gewinnt
- [x] Zerlegung des H.264-Stroms in vollständige Bilder (`AnnexBSplitter`) — teilweise gesendete Bilder ergeben im Browser Klötzchen
- [x] SIPSorcery-WebRTC mit H.264-Passthrough, Signalisierung in einem Schritt über `POST /api/webrtc/offer`, ohne STUN/TURN
- [x] Automatischer Fallback auf Stufe 1 — bei fehlendem ffmpeg, ohne Encoder, bei Zeitüberschreitung oder abgebrochener Verbindung
- [x] Monitor-Wechsel ohne Verbindungsabbruch: nur ffmpeg startet neu, der Videostrom bleibt
- [x] Umschalter H.264 ↔ JPEG in der App, Statistik zeigt den aktiven Encoder
- [ ] **Offen: auf echter Hardware prüfen** — ffmpeg muss auf dem PC installiert sein (`winget install Gyan.FFmpeg`)

## Phase 6 — Härtung
- [x] `DesktopBinder`: hängt den Aufnahme-Thread an den aktuellen Eingabe-Desktop, sobald Windows die Aufnahme entzieht
- [ ] **Teilweise:** Sperrbildschirm und UAC bleiben unzugänglich, solange der Agent ein Benutzerprozess ist. Vollständig ginge das nur mit einem Dienst in Sitzung 0 — siehe `docs/SICHERHEIT.md`
- [x] Reconnect bei Netzwechsel WLAN↔Mobilfunk: `online`-Ereignis erzwingt einen Neuaufbau, dazu Stillstandserkennung (6 s ohne Nachricht) im Bild-Socket
- [x] Security-Review über das gesamte Projekt — `docs/SICHERHEIT.md`; dabei gefunden: der CORS-Preflight lief in die Token-Sperre und legte alle REST-Aufrufe der App lahm
- [x] Agent-Auto-Update: Manifest und Datei über den Hub (hinter dem Hub-Token), SHA-256-Prüfung vor dem Tausch, Austausch über ein Batch-Skript

## Phase 7 — Bedienung auf dem Handy
- [x] Zeiger-Overlay über dem Bildschirmbild: Wischen schiebt einen virtuellen
      Mauszeiger, das Bild folgt ihm als Lupe (2,5×, per Pinch änderbar);
      Tippen klickt, langes Drücken ist ein Rechtsklick, „Halten" zieht
- [x] Eigene Zeigermarke in der App — der Stream enthält den Windows-Zeiger
      nicht (`draw_mouse=0`)
- [x] Tastatur-Overlay über dem Bildschirmbild, umschaltbar zwischen
      Handy-Tastatur und Sondertasten
- [x] ~~Handy-Tastatur über `beforeinput`~~ → ersetzt durch eine eigene
      Bildschirmtastatur mit drei gleich hohen Seiten (abc/?123/Fn); die
      Handy-Tastatur verdeckte das halbe Display und schob das Bild zusammen
- [x] Freie Tastenkombinationen: Knopf „Kombi", beliebig viele Tasten sammeln,
      gemeinsam absenden (`InputChannel.chord` drückt der Reihe nach und löst
      rückwärts)
- [x] Bildschirmseite aufgeräumt: „Ziehen", „1:1", „Auffrischen" und
      „Statistik" raus, Statistik als Chip neben die Monitor-Tabs,
      Transport/Qualität hinter das Zahnrad
- [x] Kein Fullscreen mehr (`display: standalone`) — die Android-Navigations-
      leiste bleibt; `interactive-widget=resizes-content`, damit die
      Handy-Tastatur die Bedienleiste nicht verdeckt
- [x] 19 neue Unit-Tests (Zeiger-Nachführung, Gesten-Einordnung, Übersetzung
      der Tastatur-Ereignisse, Kombinationen) — insgesamt 78 in der App
- [x] Ziehen ohne eigenen Knopf: Doppeltipp mit liegendem Finger hält die linke
      Taste — die Bedienleiste springt dadurch nicht mehr um
- [x] Navigation über ein Burger-Menü mit Seitenleiste (verbundenes Gerät,
      Geräteliste mit Online-Punkt, eigenständige Seiten) statt Tab-Leiste
- [x] Bedienleiste unter dem Bild: vier einfarbige Symbole (Tastatur, Maus,
      Medien, Power) statt beschrifteter Knöpfe
- [x] `PowerMediaView` in `MediaView` und `PowerView` getrennt — beide sind
      jetzt einzeln als Overlay und als eigene Seite nutzbar
- [x] Tastatur: Seitenwechsel als Taste („1/3") auf der Tastatur, feste
      Tastenhöhe gegen die je Seite unterschiedliche Zeilenhöhe der Symbole
- [x] Medien-Steuerung mit einfarbigen Symbolen statt Emojis (Lautstärke als
      Lautsprecher mit unterschiedlich vielen Wellen)
- [x] Texteingabe-Overlay: öffnet die Handy-Tastatur für längere Texte und die
      Zwischenablage, getippt wird am Stück beim Absenden
- [x] Shortcuts: Voreinstellungen (Win+Tab, Task-Manager, Kopieren, Einfügen)
      als Overlay, eigene über Menü → Shortcuts anlegen/bearbeiten/löschen
- [x] Zoom-Erkennung repariert: Entscheidung an der seit dem Aufsetzen
      aufgelaufenen Änderung statt an der von Ereignis zu Ereignis; beim
      Umschalten und beim Anheben eines von zwei Fingern wird neu angesetzt,
      damit das Bild nicht springt
- [x] Agent prüft nur noch beim Start auf eine neue Version (15 s nach dem
      Start), tauscht sich aus und startet neu; ein Merkzettel neben der .exe
      verhindert eine Neustartschleife
- [x] Shortcut-Fläche mit fester Höhe und Rollbalken — weitere Shortcuts
      verkleinern das Bild nicht mehr
- [x] Monitor-Auswahl als Dropdown; Standard-Monitor je Gerät merkbar
      (Zahnrad → „Als Standard")
- [x] Text-Overlay: Zeigersteuerung inklusive, eine einzige flache Zeile aus
      Schließen-Symbol und Feld
- [x] Texteingabe tippt live statt zu sammeln (`softKeyboard.ts` zurückgeholt)
      — mit Senden-Knopf ließ sich Gesendetes nicht mehr löschen
- [ ] **Offen: auf dem Handy prüfen** — vor allem, ob Gboard die Rücktaste wie
      erwartet als `deleteContentBackward` meldet

## Phase 8 — Feinschliff nach dem ersten Praxistest
- [x] Tipp mit zwei Fingern = Rechtsklick (wie auf einem Notebook-Trackpad)
- [x] Eigene Bildschirmtastatur statt der Handy-Tastatur: feste Höhe, drei
      Seiten, Umschaltleiste mit Kombi-Sammler und Vorschau
- [x] Tastatur-Overlay schaltet die Zeigersteuerung mit ein — tippen und
      gleichzeitig klicken, ohne die Tastatur zuzuklappen
- [x] Bild wird bei aktivem Zeiger **beschnitten statt verkleinert**, damit es
      bei flacher Bühne nicht auf Briefmarkengröße schrumpft
- [x] Bildschirmansicht bleibt beim Tab-Wechsel montiert (`visible`-Prop) —
      vorher wurde der Videostrom jedes Mal neu aufgebaut und der Transport
      sprang auf H.264 zurück
- [x] Gewählter Übertragungsweg überlebt den Neustart (`storage.setTransport`);
      ein automatischer Rückfall auf JPEG wird bewusst nicht gemerkt
- [x] Ziehen ohne eigenen Knopf: Doppeltipp mit liegendem Finger hält die linke
      Taste — die Bedienleiste springt dadurch nicht mehr um
- [x] Navigation über ein Burger-Menü mit Seitenleiste (verbundenes Gerät,
      Geräteliste mit Online-Punkt, eigenständige Seiten) statt Tab-Leiste
- [x] Bedienleiste unter dem Bild: vier einfarbige Symbole (Tastatur, Maus,
      Medien, Power) statt beschrifteter Knöpfe
- [x] `PowerMediaView` in `MediaView` und `PowerView` getrennt — beide sind
      jetzt einzeln als Overlay und als eigene Seite nutzbar
- [x] Tastatur: Seitenwechsel als Taste („1/3") auf der Tastatur, feste
      Tastenhöhe gegen die je Seite unterschiedliche Zeilenhöhe der Symbole
- [x] Medien-Steuerung mit einfarbigen Symbolen statt Emojis (Lautstärke als
      Lautsprecher mit unterschiedlich vielen Wellen)
- [x] Texteingabe-Overlay: öffnet die Handy-Tastatur für längere Texte und die
      Zwischenablage, getippt wird am Stück beim Absenden
- [x] Shortcuts: Voreinstellungen (Win+Tab, Task-Manager, Kopieren, Einfügen)
      als Overlay, eigene über Menü → Shortcuts anlegen/bearbeiten/löschen
- [x] Zoom-Erkennung repariert: Entscheidung an der seit dem Aufsetzen
      aufgelaufenen Änderung statt an der von Ereignis zu Ereignis; beim
      Umschalten und beim Anheben eines von zwei Fingern wird neu angesetzt,
      damit das Bild nicht springt
- [x] Agent prüft nur noch beim Start auf eine neue Version (15 s nach dem
      Start), tauscht sich aus und startet neu; ein Merkzettel neben der .exe
      verhindert eine Neustartschleife
- [x] Shortcut-Fläche mit fester Höhe und Rollbalken — weitere Shortcuts
      verkleinern das Bild nicht mehr
- [x] Monitor-Auswahl als Dropdown; Standard-Monitor je Gerät merkbar
      (Zahnrad → „Als Standard")
- [x] Text-Overlay: Zeigersteuerung inklusive, eine einzige flache Zeile aus
      Schließen-Symbol und Feld
- [x] Texteingabe tippt live statt zu sammeln (`softKeyboard.ts` zurückgeholt)
      — mit Senden-Knopf ließ sich Gesendetes nicht mehr löschen
- [ ] **Offen: auf dem Handy prüfen**
