# RemoteDesktop

Handy-App (Android) zur Fernsteuerung von PC und Laptop (beide Windows 10/11).

Architektur: **`docs/ARCHITEKTUR.md`** · Phasenplan bis Phase 8: **`docs/TASKS.md`**

**V2-Umbau zur echten App (läuft):** Begründungen in **`docs/PLAN-V2.md`**,
Arbeitsanweisung in **`docs/TASKS-V2.md`**. Nächste Phase umsetzen:
`/naechste-phase`.

Toolchain im Container: `~/.bashrc` setzt `PATH` auf `~/.dotnet` und
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (libicu fehlt, kein root).
`cd app && npm test` · `cd agent.Tests && dotnet test`.

## Aufbau

| Ordner | Stack | Läuft auf |
|---|---|---|
| `agent/` | C# / .NET 8, Windows-Dienst | PC + Laptop, Port 8443 |
| `hub/` | Node/TS + Express, Docker | NAS, Port 3080 |
| `app/` | React + Vite, PWA | Handy (Browser/Homescreen) |

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

Der Hub wird als Dockhand-Stack `remotedesktop` betrieben.
Compose liegt unter `/volume1/docker/dockhand/data/stacks/NAS/remotedesktop/`
(root-owned → Änderungen über die Dockhand-Web-UI), Build-Kontext zeigt auf
`/volume1/docker/remotedesktop`.
Der Container braucht `network_mode: host`, sonst erreicht das WOL-Magic-Packet
den LAN-Broadcast nicht.
