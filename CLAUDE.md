# RemoteDesktop

Handy-App (Android) zur Fernsteuerung von PC und Laptop (beide Windows 10/11).

Architektur: **`docs/ARCHITEKTUR.md`** · Phasenplan bis Phase 8: **`docs/TASKS.md`**

**V2-Umbau zur echten App (läuft):** Begründungen in **`docs/PLAN-V2.md`**,
Arbeitsanweisung in **`docs/TASKS-V2.md`**. Nächste Phase umsetzen:
`/naechste-phase`. Stand 01.08.2026: Phasen 9–14 erledigt, Phasen 9–13 am
echten Gerät durchgeprüft; als Nächstes Phase 15. Liegengebliebenes steht in `TASKS-V2.md`
unter „Aufräumarbeiten zum Schluss" — das wartet bis nach Phase 16 und
blockiert nichts.

Toolchain im Container: `~/.bashrc` setzt `PATH` auf `~/.dotnet` und
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (libicu fehlt, kein root).
`cd app && npm test` · `cd agent.Tests && dotnet test`.

Android baut hier ebenfalls — JDK 21 in `~/.jdk`, SDK in `~/android-sdk`
(seit 31.07.2026). `export JAVA_HOME=~/.jdk/jdk-21.0.12+8 PATH="$JAVA_HOME/bin:$PATH"
ANDROID_HOME=~/android-sdk`, dann `cd clients/android && npm run apk`.
Ausführen lässt sich die APK hier nicht — nur bauen und ihren Inhalt prüfen.

## Aufbau

| Ordner | Stack | Läuft auf |
|---|---|---|
| `agent/` | C# / .NET 8, Windows-Dienst | PC + Laptop, Port 8443 |
| `desktop/` | C# / WinForms + WebView2, Tray | PC + Laptop, zeigt `app/dist` |
| `waker/` | Node/TS + Express, Docker | NAS, Port 3080 |
| `app/` | React + Vite, PWA | Handy (Browser/Homescreen) |
| `clients/android/` | Capacitor + Java, APK | Handy, zeigt `app/dist` |

## Konventionen

- Netzwerk läuft **ausschließlich über Tailscale**. Keine LAN-IPs im Code,
  keine Fallunterscheidung „zuhause vs. unterwegs", kein Port-Forwarding.
  Immer MagicDNS-Namen verwenden.
- Der Agent hat volle Kontrolle über den PC. Jeder neue Endpoint braucht
  Token-Auth — keine Ausnahmen, auch nicht „nur zum Testen".
- Input-Events laufen über einen **eigenen** WebSocket, getrennt vom
  Video-Stream. Die Eingabe-Latenz darf nie am Bild hängen.
- Win32-Aufrufe im Agent gebündelt unter `agent/Native/`, nicht verstreut.
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
