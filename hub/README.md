# Hub — Einrichtung auf der NAS

Liefert die PWA aus, hält die Geräteliste und weckt Rechner per Wake-on-LAN.
Läuft als Dockhand-Stack `remotedesktop`.

## 1. Quellcode auf die NAS

```bash
git clone <repo> /volume1/docker/remotedesktop
```

## 2. Gerätekonfiguration

```bash
cd /volume1/docker/remotedesktop
cp hub/devices.example.json devices.json
```

`devices.json` ausfüllen. Sie enthält die Agent-Tokens und steht deshalb in
`.gitignore`:

| Feld | Woher |
|---|---|
| `hubToken` | selbst erzeugen, ≥32 Zeichen — damit meldest du dich in der App an |
| `broadcastAddress` | Broadcast des LAN, hier `192.168.178.255` |
| `devices[].host` | MagicDNS-Name, z.B. `pc.<tailnet>.ts.net` |
| `devices[].mac` | MAC der Netzwerkkarte, für Wake-on-LAN |
| `devices[].token` | das `REMOTEDESKTOP_TOKEN` des jeweiligen Agents |

Token erzeugen:

```bash
head -c 32 /dev/urandom | base64
```

## 3. Stack anlegen

Die Compose-Vorlage steht in `hub/docker-compose.yml`. Inhalt über die
**Dockhand-Web-UI** als Stack `remotedesktop` anlegen — das Verzeichnis
`/volume1/docker/dockhand/data/stacks/NAS/` ist root-owned und nicht direkt
beschreibbar.

`network_mode: host` ist Pflicht: ohne das landet das WOL-Magic-Packet im
Docker-Netz und erreicht den PC nie.

## 4. Erreichbarkeit über Tailscale

Damit das Handy den Hub per HTTPS erreicht:

```bash
tailscale serve --bg 3080
```

Die App liegt danach unter `https://nas.<tailnet>.ts.net/`. Im Browser öffnen
und über das Menü **Zum Startbildschirm hinzufügen** — danach hat sie ein
eigenes Icon und läuft im Fullscreen.

> Kein Tailscale Funnel verwenden. Der stellt die App ins öffentliche
> Internet — genau das soll dieses Projekt nicht.

## 5. Prüfen

```bash
curl http://localhost:3080/health
# {"status":"ok","devices":2}
```

## Entwicklung

```bash
cd hub && npm install && npm run dev     # Hub auf :3080
cd app && npm install && npm run dev     # PWA auf :5173
npm test                                 # in beiden Ordnern
```

## API

Alles außer `/health` verlangt `Authorization: Bearer <hubToken>`.

| Endpoint | Zweck |
|---|---|
| `GET /health` | Erreichbarkeit, ohne Auth |
| `GET /api/devices` | Geräteliste inkl. Agent-Token |
| `GET /api/devices/status` | Online-Status aller Geräte (TCP-Probe, parallel) |
| `POST /api/wol/:id` | Magic Packet an das Gerät |
| `GET /api/agent/manifest` | Größe und SHA-256 der bereitgestellten Agent-Datei |
| `GET /api/agent/download` | Die Agent-Datei selbst (fürs Selbst-Update) |

### Namensauflösung im Container (wichtig)

`network_mode: host` teilt nur den Netzwerk-Stack, **nicht** die
Namensauflösung: der Container erbt die `resolv.conf` der NAS, und darin steht
kein Tailscale-DNS. Ohne Gegenmaßnahme sind alle MagicDNS-Namen unbekannt und
jedes Gerät gilt als offline — auch wenn es läuft.

Deshalb steht in der Compose:

```yaml
    dns:
      - 100.100.100.100
```

Bewusst nur dieser eine Eintrag. Der musl-Resolver von Alpine fragt alle
Nameserver gleichzeitig und nimmt die erste Antwort — steht der LAN-DNS mit
daneben, gewinnt mal dessen „kenne ich nicht", und die Geräte flackern
zwischen online und offline. MagicDNS beantwortet alles Übrige über seine
eigenen Upstreams.

Der Status-Endpoint unterscheidet die beiden Fälle inzwischen: `reason: "dns"`
heißt, dass die Auflösung auf der NAS klemmt, `reason: "unreachable"`, dass der
Rechner nicht antwortet. Die App zeigt das entsprechend an.

### Warum der Hub die Agent-Tokens ausliefert

Die App verbindet sich für Bild und Eingaben **direkt** zum Agent auf dem PC,
nicht über die NAS — sonst wäre die NAS ein Flaschenhals für den Video-Stream.
Dafür braucht die App die Agent-Tokens. Genau deshalb ist `/api` hinter dem
Hub-Token gesperrt: ohne das könnte jedes Gerät im Tailnet die Tokens abholen.
