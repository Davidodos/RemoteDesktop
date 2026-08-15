# RemoteDesktop

Handy-App (Android) zur Fernsteuerung von PC und Laptop (beide Windows 10/11).

Veröffentlichen und Updates: **`docs/RELEASE.md`**
Architektur: **`docs/ARCHITEKTUR.md`** · Phasenplan bis Phase 8: **`docs/TASKS.md`**

**V4 (läuft):** beide Richtungen — ein Handy lässt sich ebenso steuern wie ein
PC — und ein Dateimanager. Arbeitsanweisung in **`docs/TASKS-V4.md`**.
Stand 14.08.2026: **Phasen 27–30 erledigt** (Handy liefert Bild und nimmt
Eingaben an), dazu 31a und 31c–31f. **31e ist gebaut**: eine Kopplung ist
immer beidseitig (Steckbrief-Austausch in einem Aufruf statt weitergereichtem
Code, der Entwurf aus 31b ist ersetzt), der Handy-Host läuft nur, solange die
App offen ist, und jede Verbindung wird am Handy einzeln bestätigt. **Als Nächstes: Phase 31g** — ein Geräte-Tab statt dreier (Fernsteuerung +
Geräte zusammen, Netz unter die Einstellungen), je Gerät Plattform und
„zuletzt verbunden", dazu Entfernen auf beiden Seiten, ein Verbindungstest in
beide Richtungen und ein Verbinden-Knopf, der ausgegraut ist, wenn nichts
geht.
Leitsatz: das Handy wird ein Agent und spricht dasselbe Protokoll; was ein Gerät
kann, sagt es selbst über `capabilities` in `/api/info`.

**V3 (abgeschlossen):** gemeinsame Oberfläche für alle Teile, Tailscale optional.
Arbeitsanweisung in **`docs/TASKS-V3.md`**. Stand 08.08.2026: **Phasen 21–26
erledigt**, dazu die Nachträge vom 07. und 08.08. (Live-Aktualisierung im
Fenster, Rollen, Tailnet-Name im QR-Code, Geräteverwaltung in der App;
Einrichtungsassistent im Fenster statt Häkchen im Installer, ein Datenordner
`{app}\data`, Zertifikat wird vor der Kopplung bestätigt); offen ist die
erneute Prüfung am echten Gerät. Netzmodi und die
VPN-Anleitung stehen in **`docs/NETZ.md`**.

**V2-Umbau zur echten App (abgeschlossen):** Begründungen in **`docs/PLAN-V2.md`**,
Arbeitsanweisung in **`docs/TASKS-V2.md`**. Nächste Phase umsetzen:
`/naechste-phase`. Stand 04.08.2026: **Phasen 9–16 erledigt** — der V2-Umbau ist
durch. Phasen 9–13 sind am echten Gerät durchgeprüft; offen sind nur noch
Hardware-Punkte und die Sammlung „Aufräumarbeiten zum Schluss" in `TASKS-V2.md`,
die jetzt dran wäre. Phasen 17–20 (Tailscale ablösen) sind zurückgestellt; die
Entscheidung darüber steht nach Phase 16 an.

Toolchain im Container: `~/.bashrc` setzt `PATH` auf `~/.dotnet` und
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (libicu fehlt, kein root).
`cd app && npm test` · `cd agent.Tests && dotnet test` · `cd setup.Tests && dotnet test`.

Android baut hier ebenfalls — JDK 21 in `~/.jdk`, SDK in `~/android-sdk`
(seit 31.07.2026). `export JAVA_HOME=~/.jdk/jdk-21.0.12+8 PATH="$JAVA_HOME/bin:$PATH"
ANDROID_HOME=~/android-sdk`, dann `cd clients/android && npm run apk`.
Ausführen lässt sich die APK hier nicht — nur bauen und ihren Inhalt prüfen.
Der Kotlin-Anteil (`clients/android/.../surfaces/`) hat einen eigenen Testlauf:
`cd clients/android/android && ./gradlew testDebugUnitTest`.

## Aufbau

| Ordner | Stack | Läuft auf |
|---|---|---|
| `agent/` | C# / .NET 8, geplante Aufgabe in der Benutzersitzung | PC + Laptop, Port 8443 |
| `setup/` | C# / .NET 8, Klassenbibliothek | Einrichtungslogik für Installer **und** Fenster |
| `installer/` | Inno Setup 6 | legt nur Dateien ab — eingerichtet wird im Fenster |
| `desktop/` | C# / WinForms + WebView2, Tray | PC + Laptop — `RemoteDesktop.exe`, die einzige .exe, die ein Mensch startet |
| `assets/` | SVG | Quelle des Symbols für alle drei Plattformen |
| `waker/` | Node/TS + Express, Docker | NAS, Port 3080 |
| `app/` | React + Vite | Oberfläche für Handy **und** Windows-Fenster |
| `clients/android/` | Capacitor + Java/Kotlin, APK | Handy — zeigt `app/dist`, und ist seit V4 unter `.../host/` selbst ein Agent |

## Konventionen

- Netzwerk: **vier Modi, keine Fallunterscheidung im Code** (`setup/NetworkProfile.cs`
  — Heimnetz · Tailscale · Headscale · anderer VPN-Anbieter). Die Adresse ist in
  jedem Modus Pflicht. Keine Adresse steht im Quelltext; sie
  kommt aus `setup.json`. Kein Port-Forwarding, keine Sonderbehandlung
  „zuhause vs. unterwegs" — der Modus entscheidet, nicht der Ort. Anleitung:
  `docs/NETZ.md`.
- Der Agent hat volle Kontrolle über den PC. Jeder neue Endpoint braucht
  Token-Auth — keine Ausnahmen, auch nicht „nur zum Testen".
- Ein Widerruf wirkt **sofort und rückwirkend**: `agent/Auth/LiveConnections.cs`
  trennt die laufenden WebSockets, `WebRtcRegistry.CloseOwnedAsync` den
  Videostrom. Jede neue Dauerverbindung meldet sich dort an — sonst überlebt sie
  ihre eigene Berechtigung, weil sie nur beim Aufbau geprüft wird.
- Der Einrichtungsassistent (`desktop/Pages/SetupPage.cs`) fragt **eine Sache je
  Schritt** und lässt „Weiter" erst zu, wenn diese Sache steht. Er steht nicht in
  der Seitenleiste: beim ersten Start ruft ihn das Fenster auf, danach führt der
  Weg über „Einstellungen".
- Input-Events laufen über einen **eigenen** WebSocket, getrennt vom
  Video-Stream. Die Eingabe-Latenz darf nie am Bild hängen.
- Win32-Aufrufe im Agent gebündelt unter `agent/Native/`, nicht verstreut.
- Die Windows-Oberfläche ist **ein** Fenster mit Seiten (`desktop/ShellWindow.cs`,
  `desktop/Pages/`). Kein zweites Fenster, kein `MessageBox.Show` — Rückfragen
  stehen in der Karte, Meldungen in der Statuszeile.
- Die Farben der Oberfläche stehen in `desktop/Ui/Theme.cs` und sind dieselben
  wie in `app/src/styles.css`. Ändert sich eine, gehört sie an beiden Stellen
  nachgezogen.
- Ein Symbol für alles: `assets/icon.svg` → `node scripts/icons.mjs` erzeugt
  `.ico` und Android-Mipmaps. Nie eine der erzeugten Dateien von Hand anfassen.
- **Kein Service Worker.** Die App liegt in der APK und neben der `.exe`; über
  HTTP serviert sie niemand. Ein Worker hatte nichts zwischenzuspeichern, was
  nicht ohnehin lokal lag, verzögerte aber jedes Update um einen Start
  (`app/src/serviceWorker.ts` meldet nur noch ab, was ältere Fassungen
  hinterlassen haben).
- Der Agent läuft als **geplante Aufgabe in der Sitzung des Benutzers**, nicht
  als Dienst (`setup/AgentTask.cs`). Ein Dienst sitzt in Sitzung 0 und hat dort
  weder Bildschirm noch Desktop — Bild und Eingabe scheitern dort grundsätzlich.
  Preis: ohne angemeldeten Benutzer ist der Rechner nicht erreichbar.
- Aller Zustand liegt in **einem** Ordner: `{app}\data` (`setup/AgentPaths.cs`) —
  Schlüssel, Zertifikate, `clients.json`, `setup.json`. Nichts davon gehört
  neben die `.exe`, und nichts nach `ProgramData`.
- Keine Tokens, MACs oder Tailnet-Namen im Repo — `.env` bzw. `devices.json`
  (beide in `.gitignore`).

## NAS-Deployment

Der Waker wird als Dockhand-Stack `remotedesktop` betrieben.
Compose liegt unter `/volume1/docker/dockhand/data/stacks/NAS/remotedesktop/`
(root-owned → Änderungen über die Dockhand-Web-UI), Build-Kontext zeigt auf
`/volume1/docker/remotedesktop`.
Der Container braucht `network_mode: host` — sonst erreicht das WOL-Magic-Packet
den LAN-Broadcast nicht, und die Standort-Kennung käme aus der ARP-Tabelle der
Docker-Bridge statt aus der des LANs.
Seit Phase 14 gibt es **keine `devices.json`** mehr: der Waker führt keine
Geräteliste, liefert keine PWA aus und kennt keine Agent-Tokens.
