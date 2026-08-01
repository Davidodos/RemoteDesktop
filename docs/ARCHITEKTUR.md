# RemoteDesktop — Architektur

Handy-App zur Fernsteuerung von PC und Laptop (beide Windows 10/11).
Ein einziges UI — kein Wechsel zwischen Apps.

## Topologie

```
        ┌─────────────────────────────┐
        │  Handy (Android APK)        │
        │  Capacitor + React          │
        └──────────┬──────────────────┘
                   │ Tailscale (LAN = direkt, unterwegs = DERP/direkt)
        ┌──────────┴──────────┬────────────────────┐
        │                     │                    │
  ┌─────▼──────┐     ┌────────▼───────┐   ┌────────▼────────┐
  │ NAS: Waker │     │ PC  (Agent)    │   │ Laptop (Agent)  │
  │ Docker     │     │ :8443 HTTPS    │   │ :8443 HTTPS     │
  │ :3080      │     │ 3 Monitore     │   │ 1 Monitor       │
  └────────────┘     └────────────────┘   └─────────────────┘
   WOL + siteId        Screen + Input       Screen + Input
   Kopplung            Power/Media/Aktionen Power/Media/Aktionen
                       WOL + siteId         WOL + siteId
```

**Warum ein Waker auf der NAS?** Wenn der PC ausgeschaltet ist, ist er im
Tailnet offline — Wake-on-LAN muss von einem Gerät im selben LAN-Broadcast
kommen. Die NAS läuft ohnehin 24/7. Seit Phase 14 kann das auch ein wacher
Agent auf dem zweiten Rechner; der Waker ist der Fall „alles aus".

**Er führt dabei keine Geräteliste.** Bis Phase 13 hieß dieser Dienst *Hub*,
lieferte die PWA aus und hielt eine `devices.json` mit den Agent-Tokens aller
Rechner — ein Geheimnis, hinter dem alles lag. Beides ist weg: die App bringt
ihre gekoppelten Geräte selbst mit, und die MAC des zu weckenden Rechners steht
in der Anfrage. Wer wen wecken darf, ergibt sich aus der Standort-Kennung
`siteId = sha256(gatewayMac)`, die jeder Knoten meldet.

**Warum Direktverbindung für Video/Input?** Video über die NAS zu proxien
kostet Latenz und CPU. Beide Agents holen sich per `tailscale cert` ein echtes
Let's-Encrypt-Zertifikat für ihren `*.ts.net`-Namen → die App darf ohne
Mixed-Content-Block direkt per WSS zum Agent verbinden. Im LAN routet Tailscale
direkt, ohne Umweg über die Cloud.

## Netzwerk: LAN und unterwegs sind derselbe Code

Tailscale auf Handy, PC, Laptop und NAS. Die App spricht ausschließlich
MagicDNS-Namen (`pc.<tailnet>.ts.net`) an. Tailscale wählt automatisch die
direkte LAN-Route, wenn beide im selben Netz sind. Es gibt **keine**
Fallunterscheidung „zuhause vs. unterwegs" im Code, und **kein**
Port-Forwarding an der FritzBox.

## Komponenten

### 1. Agent (`agent/`) — C# / .NET 8, Windows-Dienst

Läuft auf PC und Laptop. Self-contained Single-File-`.exe`, installiert sich als
Windows-Dienst + Tray-Icon.

**Warum C# und nicht Node/TS?** Praktisch jede geforderte Funktion ist ein
direkter Win32-API-Aufruf: `SendInput` (Maus/Tastatur), `keybd_event` mit
`VK_MEDIA_*` (Mediensteuerung), `EnumDisplayMonitors` (Monitor-Auswahl),
Desktop Duplication API (Bildschirmerfassung), `SetSuspendState` (Sleep).
In Node bräuchte das durchgehend fragile Native-Module; in C# ist es
`[DllImport("user32.dll")]`.

**API** (HTTPS + WebSocket auf Port 8443):

| Endpoint | Zweck |
|---|---|
| `GET /api/info` | Hostname, Monitorliste (Index, Auflösung, Position, primär) |
| `WS /ws/screen?monitor=N` | Bildschirm-Stream (siehe Video-Stufenplan) |
| `WS /ws/input` | Maus-, Tastatur-, Scroll-Events (bidirektional, niedrige Latenz) |
| `POST /api/power` | `sleep` \| `shutdown` \| `restart` \| `lock` |
| `POST /api/media` | `playpause` \| `next` \| `prev` \| `volup` \| `voldown` \| `mute` |

Auth: Pre-Shared-Token pro Gerät im `Authorization`-Header. Tailscale-ACL ist
die erste Schicht, das Token die zweite — ein kompromittiertes Gerät im Tailnet
soll nicht automatisch den PC übernehmen können.

### 2. Waker (`waker/`) — Node.js / TypeScript, Docker auf der NAS

Fügt sich in den bestehenden Dockhand-Stack ein
(`/volume1/docker/dockhand/data/stacks/NAS/remotedesktop/`). Bis Phase 13 hieß
er *Hub* und konnte deutlich mehr; siehe oben, warum das weg ist.

- `POST /api/wol` → Magic Packet ins LAN, MAC aus der Anfrage
  (`network_mode: host` nötig)
- `GET /api/info` → `siteId` und `canWake`; daran erkennt der Client, für
  welches Netz er zuständig ist
- Kopplung wie beim Agent: Code, öffentlicher Schlüssel, Challenge-Response
- **Keine** Gerätekonfiguration, **keine** PWA-Auslieferung, **keine** Tokens

### 3. App (`app/`) — React + Vite, PWA

Auf dem Homescreen installierbar (`display: standalone` — die
Android-Navigationsleiste bleibt sichtbar).

**Aufbau:** Die Bildschirmansicht ist die Startseite. Navigiert wird über ein
Burger-Menü oben links (Seitenleiste: verbundenes Gerät, alle bekannten Geräte
mit Online-Punkt, dann die eigenständigen Seiten). Unter dem Bild sitzt eine
schmale Leiste mit einfarbigen Symbolen, die Overlays über dem Bild ein- und
ausblenden: Bildschirmtastatur, Texteingabe (Handy-Tastatur für längere Texte
und die Zwischenablage), Shortcuts, Maus, Medien, Power. Die Texteingabe blendet
ihr Feld als flache Zeile unter der Symbolreihe ein — dort, wo gleich die
Handy-Tastatur aufgeht.

Die Texteingabe schickt **jeden Anschlag sofort** (`lib/softKeyboard.ts` wertet
`beforeinput` aus, weil Android bei `keydown` nur den Platzhalter 229 meldet).
Ein Sammeln mit Senden-Knopf wäre für lange Texte bequemer, aber dann ließe
sich Gesendetes nicht mehr mit der Rücktaste löschen. Zwischenstände beim
Wischen über die Tastatur und die Autokorrektur werden verworfen — sonst käme
jedes Wort doppelt an.

Tastatur- und Text-Overlay schalten die Zeigersteuerung mit ein — tippen und
klicken, ohne das Overlay dafür zuzuklappen.

**Views:**
- **Geräteauswahl** — PC / Laptop, mit Online-Status und WOL-Button
- **Bildschirm** — Live-Bild, Monitor-Auswahl als Dropdown (der Standard je
  Gerät steckt hinter dem Zahnrad und im localStorage), direkte
  Touch-auf-Klick-Abbildung (Tap = Linksklick, Long-Press = Rechtsklick,
  Zwei-Finger-Scroll = Mausrad, Pinch = Zoom ins Bild). Statistik und
  Bildeinstellungen sitzen in derselben Zeile wie die Monitor-Tabs, damit der
  Rest dem Bild gehört. Zwei Overlays legen sich über das Bild, ohne es zu
  verdecken:
  - **Zeiger** — die Fläche wird zum Touchpad: Wischen schiebt einen virtuellen
    Mauszeiger, das Bild folgt ihm wie eine Lupe (Zoom 2,5×, per Pinch
    änderbar). Die App führt die Zeigerposition selbst und schickt sie absolut,
    weil der Agent nicht verrät, wo der Zeiger steht; beim Einschalten wird er
    deshalb in die Bildmitte gesetzt. Da der Stream den echten Mauszeiger nicht
    enthält (`draw_mouse=0`), zeichnet die App eine eigene Marke. Ziehen geht
    per Doppeltipp, bei dem der Finger liegen bleibt — wie auf einem
    Notebook-Trackpad; ein Tipp mit zwei Fingern ist ein Rechtsklick. Ob zwei
    Finger zoomen oder scrollen, wird an der **seit dem Aufsetzen** gemessenen
    Änderung entschieden (`decideTwoFinger`) — pro Ereignis bewegen sich Finger
    nur wenige Pixel, damit war die Zoom-Schwelle praktisch nie erreichbar.
  - **Tastatur** — dieselbe Bildschirmtastatur wie im Tastatur-Tab. Sie
    schaltet die Zeigersteuerung mit ein, damit man tippen und gleichzeitig
    klicken kann. Das Bild wird dabei **beschnitten statt verkleinert**
    (`.screen-stage.crop`): volle Breite, Ausschnitt folgt dem Zeiger
- **Touchpad** — Trackpad-Fläche für relative Mausbewegung, getrennte Buttons
  für Links/Rechts/Mitte, Halte-Modus (Toggle „Taste gedrückt halten"),
  Autoklicker mit einstellbarem Intervall
- **Tastatur** — eine **eigene** Bildschirmtastatur (`lib/keyboardLayout.ts`)
  statt der Handy-Tastatur: die schiebt sich über das halbe Display, ist
  unterschiedlich hoch und meldet auf Android bei `keydown` nur den
  Platzhalter 229. Drei Seiten (`abc` QWERTZ, `?123`, `Fn` mit Sondertasten,
  Pfeilen und F1–F12), alle mit **gleich vielen Reihen** und fester Tastenhöhe,
  damit sich beim Umschalten nichts verschiebt. Der Seitenwechsel („1/3") sitzt
  als Taste unten links auf der Tastatur selbst, über ihr steht nur noch der
  Kombi-Knopf mit der Vorschau. Zeichen gehen als Unicode-Text raus, sobald ein
  Modifier feststeckt als echte Tastenkombination. Über „Kombi" lassen sich
  beliebig viele Tasten sammeln und gemeinsam absenden
- **Power & Medien** — die vier Power-Aktionen (mit Bestätigungsdialog) und die
  Mediensteuerung, getrennt in `PowerView` und `MediaView`
- **Shortcuts** — frei benennbare Tastenkombinationen (`lib/shortcuts.ts`, im
  localStorage). Die Tasten werden auf der Bildschirmtastatur ausgewählt, nicht
  getippt: so kann nur entstehen, was der Agent auch auflöst

## Video: Stufenplan

Bildschirmübertragung ist der aufwendigste Teil. Deshalb zwei Stufen, damit
früh etwas Nutzbares steht:

**Stufe 1 — JPEG über WebSocket (MVP).** — umgesetzt
Desktop Duplication API → nur die von Windows gemeldeten Änderungsrechtecke →
JPEG → binäres WS-Frame mit 8-Byte-Kopf (Position und Größe des Ausschnitts).
Kodiert wird über GDI+ statt WIC: es steckt schon in Windows, bringt die
Skalierung mit und liegt bei diesen Ausschnittsgrößen im einstelligen
Millisekundenbereich. Qualität und Auflösung regeln sich in sechs Stufen nach
der gemessenen Frame-Dauer. Für Navigieren, Klicken und Bedienen völlig
ausreichend, für Video-Wiedergabe nicht. Wichtig: Die Eingabe-Latenz hängt
nicht am Bild — Input läuft über einen eigenen Socket.

Details zum Protokoll: `agent/README.md`.

**Stufe 2 — WebRTC / H.264.** — umgesetzt
ffmpeg mit `ddagrab` (Desktop Duplication, `output_idx` wählt den Monitor)
→ `h264_nvenc` / `h264_qsv` / `h264_amf`, sonst `libx264` → SIPSorcery (WebRTC
in C#) mit H.264-Passthrough. Signalisierung über den Agent selbst, Medien
direkt. Kein STUN und kein TURN: beide Enden hängen im selben Tailnet.

Der Agent probiert die Encoder der Reihe nach aus, statt die Liste von ffmpeg
zu glauben — ein vorhandener Encoder heißt nicht, dass er auf diesem Rechner
auch ein Bild liefert. Wer gewonnen hat, steht in der Statistik der App.

ffmpeg ist eine externe Abhängigkeit und wird nicht mitgeliefert. Fehlt es,
bleibt alles beim JPEG-Stream — die App merkt das beim Verbindungsaufbau und
schaltet ohne Zutun um.

Stufe 1 wird nicht weggeworfen — sie bleibt als Fallback für Geräte ohne
Hardware-Encoder und wenn WebRTC keine Verbindung bekommt.

## Sicherheit

- Kein Port-Forwarding, keine Exposition ins Internet. Zugang ausschließlich
  über das Tailnet.
- Tailscale-ACL: nur das Handy darf Port 8443 auf PC/Laptop erreichen.
- Kopplung pro Gerät (Phase 10): eigenes Schlüsselpaar je Client, der Agent
  kennt nur den öffentlichen Teil. Kein geteiltes Token mehr.
- Selbst-Update nur gegen ein Manifest mit gültiger ECDSA-Signatur (Phase 14).
- Der Agent läuft mit den Rechten, die er braucht — Dienst im
  Benutzerkontext für Input-Injection, erhöhte Rechte nur für Power-Aktionen.
- Tokens und Schlüssel niemals im Repo. Ausführlich in `docs/SICHERHEIT.md`.

## Offene Punkte

- **UAC-Dialoge und Sperrbildschirm:** Ein Dienst im Benutzerkontext kann auf
  dem Secure Desktop weder zeichnen noch klicken. Für Strg+Alt+Entf und
  UAC-Prompts braucht es einen zweiten Prozess in Session 0 bzw. den
  `SetThreadDesktop`-Weg. Wird in Phase 5 angegangen, nicht im MVP.
- **HDR-Monitore** liefern über Desktop Duplication ausgewaschene Farben
  (Tone-Mapping nötig). Erst relevant, wenn es auftritt.
