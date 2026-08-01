# Waker — Einrichtung auf der NAS

Weckt schlafende Rechner per Wake-on-LAN und sagt, in welchem Netz er steht.
Mehr nicht. Läuft als Dockhand-Stack `remotedesktop`.

Bis Phase 13 stand hier der **Hub**: er lieferte die PWA aus, führte die
Geräteliste und gab die Agent-Tokens **aller** Rechner an jeden heraus, der das
Hub-Token kannte. Alle drei Aufgaben sind weg — die App bringt ihre Geräte
selbst mit (Kopplung, Phase 10), es gibt eine APK und ein Windows-Fenster statt
einer ausgelieferten PWA, und die MAC des zu weckenden Rechners steht in der
Anfrage.

**Es gibt deshalb keine `devices.json` mehr.** Ein zweiter Standort heißt:
Container starten, einmal koppeln, fertig.

## Warum es überhaupt einen Waker braucht

Ein schlafender Rechner führt keine Software aus. Die Netzwerkkarte lauscht im
Stromsparmodus auf ein Ethernet-Frame — es läuft kein IP-Stack, und auf ARP
antwortet niemand. Also muss ein **waches Gerät im selben Netzsegment** das
Frame aussenden. Ein Handy im Mobilfunknetz kann das grundsätzlich nicht.

Ein wacher Agent auf einem zweiten Rechner kann es ebenfalls (seit Phase 14,
`POST /api/wol`). Der Waker ist der Fall „alles aus": die NAS läuft ohnehin
durch.

## 1. Quellcode auf die NAS

```bash
git clone <repo> /volume1/docker/remotedesktop
mkdir -p /volume1/docker/remotedesktop/waker-config
```

## 2. Stack anlegen

Compose-Vorlage: `waker/docker-compose.yml`. Anzupassen ist genau eine Zeile —
`BROADCAST_ADDRESS` auf den Broadcast des eigenen LANs (hier
`192.168.178.255`).

`network_mode: host` ist **Pflicht**, und zwar aus zwei Gründen:

- ohne host-Netz landet das Magic Packet im Docker-Netz statt im LAN,
- und die Standort-Kennung käme aus der ARP-Tabelle der Docker-Bridge — dann
  stünde der Waker rechnerisch in einem anderen Netz als der PC daneben.

## 3. Zertifikat

Die App läuft unter `https://` — im WebView2-Fenster ebenso wie in der APK. Ein
Browser lässt von dort keine `http://`-Anfrage durch, der Waker braucht also
ein eigenes Zertifikat, genau wie jeder Agent:

```bash
tailscale cert --cert-file /volume1/docker/remotedesktop/waker-config/cert.crt \
               --key-file  /volume1/docker/remotedesktop/waker-config/cert.key \
               nas.<tailnet>.ts.net
```

Ohne die beiden Variablen startet der Waker trotzdem, sagt es aber im Log und
ist dann nur von der Maschine selbst aus brauchbar.

## 4. Koppeln

Der Waker ist für die App ein gekoppeltes Gerät wie ein Agent, nur eines, das
außer Wecken nichts kann. Kopplungscode holen — **auf der NAS selbst**, über
das Netz antwortet der Aufruf mit 403:

```bash
docker exec remotedesktop-waker \
  wget -qO- --no-check-certificate --post-data='' https://localhost:3080/api/pair/code
```

Der Code steht auch im Log (`docker logs remotedesktop-waker`). Er gilt fünf
Minuten und funktioniert genau einmal.

In der App: *Gerät koppeln* → Adresse der NAS und den Code eintippen.

## Standort-Kennung

Jeder Knoten meldet `siteId = sha256(gatewayMac)` — die MAC-Adresse seines
Standard-Gateways, gehasht. Gleiches LAN heißt gleiches Gateway, unabhängig
davon, welche IP der DHCP gerade vergeben hat; ein anderer Standort heißt eine
andere Router-MAC. Subnetz und Gateway-IP taugen dafür nicht,
`192.168.178.0/24` gibt es millionenfach.

Der Client merkt sich MAC und `siteId` eines Rechners, solange dieser wach ist.
Schläft er, sucht der Client unter allen bekannten Knoten einen mit **derselben
`siteId`** und schickt ihm `POST /api/wol { mac }`. Findet er keinen, bleibt
der Weckknopf aus — mit Begründung, nicht mit einem Fehler.

Prüfen, was der Waker ermittelt hat:

```bash
curl -k https://localhost:3080/health
# {"status":"ok","siteId":"c158…"}
```

Steht dort `null`, läuft der Container vermutlich ohne `network_mode: host`.
Notnagel für Systeme, auf denen sich die Gateway-MAC nicht lesen lässt:
`SITE_ID` von Hand setzen — sie muss dann mit dem übereinstimmen, was der Agent
im selben Netz meldet (`/api/info`).

## Umgebung

| Variable | Vorgabe | Wofür |
|---|---|---|
| `WAKER_PORT` | `3080` | Port |
| `BROADCAST_ADDRESS` | `255.255.255.255` | Ziel des Magic Packets |
| `CLIENTS_PATH` | `/config/clients.json` | gekoppelte Geräte, nur öffentliche Schlüssel |
| `SITE_ID` | — | überschreibt die ermittelte Kennung |
| `CERTIFICATE_PATH` · `KEY_PATH` | — | Zertifikat aus `tailscale cert`; ohne sie nur Klartext |

## Endpunkte

| Route | Zugang | Zweck |
|---|---|---|
| `GET /health` | offen | Läuft er, und welchen Standort meldet er |
| `GET /api/info` | offen | `role`, `siteId`, `canWake` — daran erkennt der Client ihn |
| `POST /api/wol` | gekoppelt | `{ "mac": "aa:bb:cc:dd:ee:ff" }`, höchstens 10-mal pro Minute |
| `POST /api/pair/code` | nur lokal | Kopplungscode |
| `POST /api/pair` | offen | Kopplung mit Code, Name und öffentlichem Schlüssel |
| `POST /api/session/challenge` · `POST /api/session` | offen | Anmeldung per Unterschrift |
| `DELETE /api/clients/{id}` | nur lokal | Widerruf |

Die Begrenzung auf zehn Weckversuche pro Minute ist kein Schutz vor Missbrauch
durch Gekoppelte — WOL kann ohnehin nur einschalten. Sie verhindert, dass der
Dienst als Paket-Verstärker taugt.

## Multi-Arch-Image

```bash
docker buildx build --platform linux/amd64,linux/arm64 \
  -f waker/Dockerfile -t remotedesktop-waker:latest .
```

arm64 ist für einen Raspberry Pi an einem zweiten Standort gedacht. Es gibt
nichts zu kompilieren, was sich pro Architektur unterscheidet — das Image
enthält nur JavaScript.
