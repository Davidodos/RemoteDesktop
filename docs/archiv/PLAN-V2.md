# RemoteDesktop V2 — Umbau zur echten App

Stand: 30. Juli 2026. Ausgangspunkt ist der fertige Stand nach Phase 8:
Agent (C#/.NET 8) auf PC und Laptop, Hub (Node/Docker) auf der NAS, PWA
(React 19 / Vite 8) auf dem Handy. 98 Agent-Tests und 78 App-Tests grün, alles
außer der Hardware-Gegenprüfung erledigt.

Dieses Dokument bewertet die Umbau-Ideen einzeln, sagt bei jeder, was tatsächlich
geht und was nicht, und leitet daraus einen Phasenplan ab.

**Entschieden (30.07.2026): Tailscale bleibt der Netzwerkpfad.** Der Umbau auf
einen eigenen Rendezvous-Server ist durchgerechnet und umsetzbar (Abschnitt 4a),
kostet aber mehr als alles andere zusammen und macht einen öffentlich
erreichbaren Dienst zur Pflicht. Gebaut werden deshalb zuerst die Phasen 9–16 —
sie liefern **alle gewünschten Funktionen**. Für die Veröffentlichung ist
Tailscale ohnehin der bessere Weg, sofern ein Assistent die Einrichtung
übernimmt (Abschnitt 4b).

## Kurzfassung

| Idee | Urteil | Kern |
|---|---|---|
| Native Android-App statt PWA | **Ja** | Als Capacitor-Hülle um die bestehende React-App, nicht als Kotlin-Neubau |
| Windows-Gerät als Client | **Ja** | WebView2-Fenster um dieselbe React-App, im Agent-Prozess; löst zugleich das offene Tray-Icon |
| Hub abschaffen | **Nein, er wandelt sich** | PWA-Auslieferung, Registry und Token-Weitergabe fallen weg — dafür kommt `rdcoord` (Account + Signalisierung + TURN + WOL) |
| Flexible Shortcuts (Skripte, Programme) | **Ja** | Aktionen werden **am Agent** deklariert, der Client ruft nur IDs auf |
| Widgets, Quick-Settings, App-Shortcuts | **Ja** | Setzt die Aktionen aus dem Punkt darüber voraus, sonst gibt es nichts anzuzeigen |
| Updates per Knopfdruck über GitHub | **Ja** | Agent voll automatisch, Android immer mit einem Tipp Bestätigung — anders erlaubt es Android nicht |
| Account statt Pro-Gerät-Token | **Ja** | Konto auf `rdcoord`, aber die Autorisierung liegt beim Agent, nicht beim Server |
| Tailscale komplett raus | **Zurückgestellt** | Technisch gelöst (Abschnitt 4a), kostet aber 27–35 Tage und einen öffentlich erreichbaren Dienst. Für eine öffentliche App ist Tailscale sogar der bessere Weg |
| WOL direkt vom Client | **Nein** | Ein schlafender PC führt keine Software aus. Es braucht immer ein waches Gerät **im Netz des Ziels** — verlässt der PC das Heimnetz, hilft die NAS nicht mehr |
| App öffentlich für jeden | **Ja** | Agent + Client veröffentlichen, Tailscale-Einrichtung per Assistent, `rdcoord` als optionales Self-Hosting-Image. **Keinen Server für Fremde auf der NAS** |

## Zielbild

```
   ┌──────────────────────────┐        ┌───────────────────────────────┐
   │ Handy (Android APK)      │        │ Laptop  (Agent + Client)      │
   │ Capacitor + React        │        │ Dienst · WebView2-UI · Tray   │
   │ Widget · QS-Tile · FGS   │        │ actions.json · clients.json   │
   └────────────┬─────────────┘        └───────────┬───────────────────┘
                │                                  │
                │   Tailscale — vom Assistenten eingerichtet
                │   (direkt im LAN, DERP unterwegs)
                │                                  │
   ┌────────────┴──────────────────────────────────┴───────────────────┐
   │                                                                   │
   │   ┌────────────────────────────────┐   ┌───────────────────────┐  │
   │   │ PC  (Agent + Client)           │   │ NAS / anderer Agent   │  │
   │   │ Kopplung per QR · Aktionen     │   │ weckt im eigenen LAN  │  │
   │   │ Geräteschlüssel (ECDSA P-256)  │   │                       │  │
   │   └────────────────────────────────┘   └───────────────────────┘  │
   └───────────────────────────────────────────────────────────────────┘
                                │
                    GitHub Releases (Agent-.exe, APK, signiertes Manifest)
```

Kein Dienst steht mehr zwischen Client und Agent: keine Geräte-Registry, keine
Token-Weitergabe, keine PWA-Auslieferung. Die Erlaubnis, einen Rechner zu
steuern, erteilt **ausschließlich der Agent** aus seiner lokalen
`clients.json` — bei der Kopplung, mit Bestätigung am Zielrechner.

Wecken ist eine Fähigkeit **des Netzes**, in dem das Ziel steht (Abschnitt 3):
irgendein wacher Knoten dort sendet das Magic Packet. Zu Hause die NAS oder der
andere Rechner; anderswo gar keiner — dann ist der Knopf ausgegraut.

> Die zurückgestellte Variante ohne Tailscale — `rdcoord` als Konto- und
> Signalisierungsdienst mit TURN — steht vollständig in Abschnitt 4a.

---

## 1. Native Android-App

### Empfehlung: Capacitor-Hülle, kein Kotlin-Neubau

Der Wert der App steckt nicht im Gerüst, sondern in vier Dateien, die teuer
erarbeitet wurden: `screenGestures.ts` (Zwei-Finger-Einordnung nach
aufgelaufener Änderung, Zeiger-Nachführung), `softKeyboard.ts` (Android meldet
bei `keydown` nur 229, also `beforeinput`; Wisch-Zwischenstände verwerfen),
`keyboardLayout.ts` (drei gleich hohe Seiten) und `inputChannel.ts` (Coalescing
plus `flushPendingMove` gegen den Klick, der vor seiner Bewegung ankommt). Dazu
78 Tests.

Ein Compose-Neubau löst jedes dieser Probleme erneut — und die Android-Eigenheit
mit `keydown` 229 verschwindet dabei nicht, sie wird nur zu einer anderen
Eigenheit. Capacitor behält den Code komplett und gibt trotzdem alles, was an
der PWA fehlt.

### Was das konkret bringt

- **Echte APK**: eigenes Icon, kein Browser-Rahmen, keine PWA-Installations-Hürde
- **Foreground-Service**: der Input-Socket überlebt das Wegwischen; heute wird
  ein Browser-Tab im Hintergrund gedrosselt und der Stream pausiert
- **Kotlin-Anbindung** für Widget, Quick-Settings-Tile, App-Shortcuts,
  Paket-Installer, QR-Scanner — alles Dinge, die einer PWA verwehrt sind
- **Kein Herkunfts-Zwang mehr**: heute muss der Agent ein `tailscale cert`
  vorhalten, weil die PWA von der NAS über HTTPS kommt und der Browser sonst
  Mixed Content blockt

Der Video-Weg bleibt unverändert: die Android-WebView spricht WebRTC und
dekodiert H.264 über MediaCodec in Hardware. Ein nativer Client mit libwebrtc
würde vielleicht 20–40 ms sparen — das ist eine spätere Optimierung, kein
Argument für einen Neubau.

### Einschränkungen

- Die WebView bleibt eine WebView. Wer heute mit der Bild-Latenz unzufrieden
  ist, wird es danach auch sein.
- Das `tailscale cert` fällt **erst mit Phase 20** weg — und die ist zurückgestellt.
  Bis dahin bleibt der heutige Direktweg samt Zertifikat und dessen
  90-Tage-Erneuerung. Danach gibt es keine Web-PKI mehr, sondern
  Geräteschlüssel — das erledigt WebRTC über DTLS mit.
- Android 14+ verlangt für jeden Foreground-Service einen deklarierten Typ.
  `connectedDevice` passt; bei einer Play-Store-Veröffentlichung würde das
  begründet werden müssen, beim Sideload nicht.
- iOS wäre über dasselbe Capacitor-Projekt technisch fast geschenkt, praktisch
  aber nicht: 99 $/Jahr Entwicklerkonto, kein Sideload, alle sieben Tage neu
  signieren. Bewusst außen vor.
- **Android als Agent ist ausgeschlossen.** Ein Handy fernsteuern hieße
  MediaProjection für das Bild und Eingabe-Injektion — letztere gibt es ohne
  Root oder Accessibility-Missbrauch nicht. Kein Thema.

---

## 2. Windows-Gerät als Client

### Empfehlung: WebView2-Fenster, dieselbe React-App

Der Agent ist schon .NET 8 auf Windows. Ein WinForms-Projekt mit
`Microsoft.Web.WebView2` bekommt ein Fenster, das die gebaute React-App aus
`app/dist` lädt, plus das Tray-Icon, das in Phase 1 offen geblieben ist. Beides
in einem Zug.

Am Desktop wird die Bedienung dabei **einfacher**, nicht schwerer:

- **Pointer Lock** (Chromium, also in WebView2 vorhanden) gibt echte relative
  Mausbewegung — das Zeiger-Overlay mit seiner selbstgeführten Position ist
  dort nicht nötig
- `keydown` liefert am Desktop richtige `code`-Werte. Die eigene
  Bildschirmtastatur wird zur Ausnahme statt zur Regel; die physische Tastatur
  geht direkt durch
- Zwischenablage in beide Richtungen wird möglich (Clipboard-API mit
  Fenster-Fokus), was am Handy an der Berechtigung scheitert

### Projektaufteilung

`Microsoft.NET.Sdk.Web` und `UseWindowsForms` im selben `.csproj` ist
unangenehm. Sauberer:

```
agent/          Sdk.Web — Dienst, Aufnahme, Input, API   (bleibt wie es ist)
desktop/        WinForms + WebView2 + Tray, referenziert agent/
```

Ausgeliefert wird **ein** Installer mit zwei Programmen. Der Dienst startet
weiterhin bei der Anmeldung, das UI-Fenster nur auf Wunsch aus dem Tray.

### Einschränkungen

- **WebView2-Runtime** muss vorhanden sein. Auf Windows 11 ist sie es, auf
  Windows 10 in der Regel über Edge. Fehlt sie, braucht es eine verständliche
  Fehlermeldung mit Download-Link — kein stiller Absturz.
- **Selbstverbindung sperren.** Ein Rechner, der sich selbst als Ziel wählt,
  zeigt sein eigenes Fenster im eigenen Fenster und rekursiv weiter. Der Client
  vergleicht den Hostnamen aus `/api/info` mit `Environment.MachineName` und
  verweigert.
- Der Sperrbildschirm des **Ziel**rechners bleibt auch für einen
  Windows-Client unerreichbar — das ändert sich durch die Client-Plattform
  nicht (siehe `docs/SICHERHEIT.md`).
- Unsignierte .exe heißt SmartScreen-Warnung bei jedem neuen Build. Ein
  Code-Signing-Zertifikat kostet dreistellig pro Jahr. Für zwei eigene Rechner
  ist die Warnung hinnehmbar; sie muss nur dokumentiert sein, damit sie nicht
  jedes Mal wie ein Fehler wirkt.
- Zwei Rechner mit Agent **und** Client heißt: wer einen übernimmt, erreicht
  über die gespeicherten Zugänge auch den anderen. Dagegen helfen die
  clienteigenen Schlüssel und Scopes aus Abschnitt 4 (einzeln widerrufbar),
  nicht die Architektur.

---

## 3. Wake-on-LAN: warum es immer einen Mittelmann braucht

Das ist keine Entwurfsentscheidung, sondern Physik. **Ein schlafender Rechner
führt keine Software aus.** Die Netzwerkkarte lauscht im Stromsparmodus auf ein
Ethernet-Frame — es läuft kein IP-Stack, es gibt keine offene Verbindung, und
auf ARP antwortet niemand. Also muss ein **waches Gerät im selben
L2-Segment** das Frame aussenden. Ein Client im Mobilfunknetz kann das
grundsätzlich nicht, und im heimischen WLAN nur unzuverlässig (Android verwirft
Broadcasts je nach Gerät, dazu WLAN-Client-Isolation).

Der übliche „WOL über WAN"-Trick — Port-Forward auf UDP 9 plus ein statischer
ARP-Eintrag, damit der Router weiß, wohin — scheitert an Consumer-Hardware: die
FritzBox macht weder Directed Broadcast noch statische ARP-Einträge.

**Der Mittelmann muss aber kein Dienst von dir sein.** Vier Kandidaten:

1. **Ein anderer Agent.** Ist der Laptop wach, weckt er den PC. `wol.ts` nach
   C# portieren, `POST /api/wol` am Agent, `canWake` in der Fähigkeitsliste.
   Deckt den häufigen Fall — nicht aber „alles aus".
2. **`rdcoord` auf der NAS** (Abschnitt 4a). Die NAS läuft 24/7 und steht im
   LAN. `wol.ts`, `probe.ts` und `auth.ts` existieren und sind getestet;
   `network_mode: host` bleibt nötig. Kostet nichts extra, weil `rdcoord` für
   die Signalisierung ohnehin gebraucht wird.
3. **FritzBox per TR-064** (`Hosts:1` / `X_AVM-DE_WakeUpHost` mit der MAC), oder
   ihre eingebaute Funktion „Rechner bei Zugriff aus dem Internet aufwecken".
   Der einzige Weg ganz ohne eigenen Dienst — dafür AVM-spezifisch, TR-064 muss
   freigeschaltet sein, und die Box-Zugangsdaten müssten irgendwo liegen. **An
   der echten Box verifizieren, bevor darauf gebaut wird.**
4. Proxmox (dns02) — läuft ebenfalls 24/7 im LAN, wäre ein Rückfall, wenn die
   NAS einmal weg ist.

**Vorschlag: 1 und 2.** Wecken wird eine Fähigkeit, die jeder Knoten melden
kann; der Client fragt den ersten erreichbaren Knoten im LAN des Ziels. Weg 3
bleibt als Option, wenn `rdcoord` später auf einen VPS zieht und trotzdem
jemand im LAN wecken muss.

### Wenn der Rechner das Netz wechselt

Nimmst du den PC mit zu den Eltern, kann die NAS ihn **nicht mehr wecken** — ein
Magic Packet kommt über keinen Router, und Tailscale hilft nicht, weil auf einem
schlafenden Rechner kein Tailscale läuft. Daraus folgt der wichtigste Satz
dieses Abschnitts:

> **Wecken ist eine Eigenschaft des Netzes, in dem der Rechner steht — nicht des
> Rechners.**

Für einen wandernden Rechner gibt es vier Wege, nach Aufwand sortiert:

1. **Geplantes Aufwachen.** Aufgabenplanung mit „Computer zum Ausführen dieser
   Aufgabe reaktivieren" oder RTC-Alarm im BIOS. Der PC wacht stündlich kurz
   auf, meldet sich, und es entsteht ein Zeitfenster. Kostet nichts, braucht
   kein zusätzliches Gerät — der pragmatischste Weg.
2. **Smart-Plug plus BIOS „Restore on AC Power Loss = Power On".** Steckdose
   übers Internet einschalten, der Rechner bootet. ~10 €, funktioniert von
   überall, ohne eigenes Gerät im fremden Netz. Vorher über den Agent sauber
   herunterfahren.
3. **Irgendein waches Gerät im Zielnetz** mit Agent oder als
   Tailscale-Subnet-Router. Muss selbst wach sein — dasselbe Problem eine Ebene
   tiefer.
4. **Intel vPro / AMT** (oder BMC/IPMI auf Serverboards). Der einzige echte
   „Power-on über IP", weil die Management-Engine bestromt bleibt. Braucht
   passende Hardware und Einrichtung.

### Folge für den Entwurf

Der Client fragt nicht „kann dieses Gerät geweckt werden?", sondern **„gibt es
im Netz des Ziels einen erreichbaren Knoten, der wecken kann?"**. Ja → Knopf.
Nein → Knopf ausgegraut mit Begründung, nicht mit einem Fehler. Das ist ohnehin
zwingend, sobald die App öffentlich ist (Abschnitt 4b): die meisten Nutzer haben
genau einen Rechner und damit niemanden im LAN, der ihn wecken könnte.

### Mehrere Standorte, ein Knopf

Sobald es Waker an mehreren Orten gibt — zu Hause die NAS, bei den Eltern ein
Raspberry Pi —, darf der Nutzer **nicht** auswählen müssen, welcher zuständig
ist. Die Zuordnung muss sich selbst ergeben. Sie tut es, wenn jeder Knoten sagt,
in welchem Netz er steht.

**Standort-Kennung.** Jeder Knoten — Agent wie Waker — ermittelt die
**MAC-Adresse seines Standard-Gateways** aus der eigenen ARP-Tabelle und meldet
`siteId = sha256(gatewayMac)`. Das ist die verlässlichste LAN-Kennung, die ohne
Konfiguration zu haben ist: gleiches LAN heißt gleiches Gateway, unabhängig von
DHCP-Leases; ein anderer Standort heißt eine andere Router-MAC. Subnetz und
Gateway-IP taugen nicht — `192.168.178.0/24` haben zu viele.

**Ablauf, ohne einen einzigen Konfigurationseintrag:**

1. Solange der PC wach ist, meldet er dem Client in `/api/info` seine eigene MAC
   und seine `siteId`. Der Client merkt sich beides.
2. Jeder Waker meldet `siteId` und `canWake: true`.
3. Schläft der PC, sucht der Client unter allen erreichbaren Knoten einen mit
   **derselben `siteId`** und schickt ihm `POST /api/wol { mac }`.
4. Wandert der PC an einen anderen Ort, meldet er beim nächsten Mal eine neue
   `siteId` — und ab da wird automatisch der Waker dort gefragt.

Damit ist der Knopf in der App immer derselbe, und **der Waker braucht keine
Gerätekonfiguration**: keine `devices.json`, keine MAC-Liste, nichts, was bei
einem neuen Rechner nachgepflegt werden müsste. Die MAC steht in der Anfrage.
Ein zweiter Standort heißt: Container starten, einmal koppeln, fertig.

**Reihenfolge der Kandidaten**, wenn mehrere passen: erst ein Waker (läuft
durch), dann ein wacher Agent am selben Ort, dann — optional — der Client
selbst, falls er gerade im selben WLAN hängt. Letzteres ist ein
Gratis-Rückfall, aber nichts, worauf man baut (Android verwirft Broadcasts je
nach Gerät).

**Absicherung.** `POST /api/wol` wird wie jeder andere Endpoint über die
Kopplung aus Abschnitt 4 autorisiert — ein Waker ist ein gekoppeltes Gerät wie
ein Agent. Der Schaden wäre gering (WOL kann nur einschalten, und nur im
eigenen LAN), aber ein offener Broadcast-Sender im Netz ist trotzdem nichts,
was man stehen lässt. Dazu ein einfaches Limit, damit der Dienst nicht als
Paket-Verstärker taugt.

**Der Waker selbst** bleibt winzig: WOL, `siteId`, Kopplung, Statusauskunft.
Multi-Arch-Image (amd64 für die NAS, arm64 für einen Pi), keine
Gerätekonfiguration, `network_mode: host` wegen des Broadcasts.

---

## 4. Account statt Pro-Gerät-Token

Ein Account ist umsetzbar und richtig — mit einer Bedingung: **er darf über die
Erlaubnis, einen Rechner zu steuern, nicht allein entscheiden.** Sonst ist der
Kontodienst der Generalschlüssel zu deinen PCs, und wer ihn übernimmt, sitzt an
beiden Rechnern.

### Aufteilung: Identität beim Server, Autorisierung beim Agent

| Wer | Weiß | Kann |
|---|---|---|
| `rdcoord` | Konto, Liste der Geräte samt Public Keys, wer online ist | Verbindungen vermitteln, Nutzdaten notfalls weiterleiten. **Keinen Rechner steuern, keinen Verkehr mitlesen** |
| Agent | Welche Client-Public-Keys er zugelassen hat (`clients.json`) | Steuerung erlauben oder verweigern |

Umsetzung:

- Jeder Agent und jeder Client erzeugt bei der Einrichtung ein **eigenes
  Schlüsselpaar** (ECDSA P-256 — in .NET 8 und in der WebCrypto-API eingebaut,
  keine Abhängigkeit). Der private Schlüssel verlässt das Gerät nie; der
  öffentliche geht an `rdcoord`.
- **Anmeldung** am Konto per **Passkey (WebAuthn)** plus einem
  Wiederherstellungscode. Damit speichert der Server überhaupt kein Passwort —
  die Alternative wäre Argon2id samt Sperrlogik und E-Mail-Versand.
- **Kopplung**: der Agent zeigt in seinem Fenster (Abschnitt 2) einen
  6-stelligen Code, 5 Minuten gültig, einmal verwendbar, dazu einen QR-Code.
  Der Client scannt, `rdcoord` stellt die Verbindung her, der Agent nimmt den
  Public Key des Clients in `clients.json` auf. Kein Token zum Abtippen.
- **Der entscheidende Punkt:** trägt jemand über ein übernommenes Konto ein
  fremdes Gerät ein, lehnt der Agent es ab — er vertraut seiner lokalen Liste,
  nicht der Auskunft des Servers. Ein neues Gerät braucht **immer** die
  Bestätigung am Zielrechner.

Damit ist auch der Wunsch „neues Gerät, nur einloggen" fast erfüllt: nach dem
Anmelden sieht der Client alle Agenten des Kontos — freischalten muss er sich
bei jedem einmal. Bei drei Rechnern ist das dreimal ein QR-Code, einmal im
Leben. Das ist der Preis dafür, dass ein Kontoeinbruch nicht gleich alles
kostet, und ich halte ihn für richtig.

### Was sonst noch mitkommt

- **`clients.json` speichert nur Public Keys und Hashes**, keine Klartext-
  Tokens. Damit ist der Befund „Tokens liegen im Klartext" aus
  `docs/SICHERHEIT.md` für die Agent-Seite erledigt.
- **Widerruf** wird erst dadurch möglich: Handy verloren → einen Eintrag
  löschen, die übrigen Clients merken nichts. Heute müsste das Token überall
  getauscht werden.
- **Scopes** je Client: das Widget braucht `actions`, aber nicht `power`. Der
  Windows-Client des Laptops darf `screen` und `input`, aber kein `shutdown`.
- **Authentifiziert wird innerhalb der Verbindung**, per signiertem Challenge
  über den Data-Channel. Nicht über den DTLS-Fingerprint aus dem SDP — der
  kommt von `rdcoord` und ist damit ausdrücklich nicht vertrauenswürdig. Wer
  das verwechselt, hat einen Server gebaut, der sich selbst zum Client machen
  kann.

---

## 4a. Ohne Tailscale: was `rdcoord` leisten muss

> **Zurückgestellt (30.07.2026).** Dieser Abschnitt bleibt vollständig stehen,
> weil die Analyse gilt und die Entscheidung nach Phase 16 erneut ansteht. Gebaut wird er vorerst nicht — Begründung am Ende des
> Abschnitts und in 4b.

Das ist der teuerste und riskanteste Teil des Plans. Er ist umsetzbar, aber die
Rechnung sollte offen auf dem Tisch liegen.

### Zwei Dinge, die man nicht verwechseln darf

**Signalisierung ist unvermeidbar.** Niemand kennt die aktuelle öffentliche
Adresse und den NAT-Port deines PCs — die IP wechselt, der Port wird vom NAT
vergeben. Eine immer erreichbare Stelle muss beide Seiten zusammenbringen.
Genau das tut Tailscales Koordinationsserver heute.

**Der Nutz-Traffic kann direkt laufen.** Sobald beide Seiten voneinander
wissen, baut ICE per Hole-Punching eine echte P2P-Verbindung — grob in 80–90 %
der Fälle. Der Rest braucht einen Relay. Es scheitert bei symmetrischem NAT
(in Mobilfunknetzen häufig), bei **CGNAT auf der Heimseite** und hinter
strikten Firmen-Firewalls.

### Was Tailscale kostenlos erledigt und dann bei dir liegt

| Tailscale heute | Danach selbst |
|---|---|
| Koordinationsserver | `rdcoord`, **öffentlich erreichbar auf 443** |
| DERP-Relay | coturn als TURN-Server für die 10–20 % ohne P2P |
| WireGuard-Krypto + Node-Identität | Kontodienst, Geräteschlüssel, gegenseitige Auth (Abschnitt 4) |
| Routbare Adressen | ICE/STUN in Agent und Client |

### Der eigentliche Preis

Heute ist aus dem Internet **nichts** erreichbar — kein Port-Forwarding, keine
öffentliche IP, so steht es in `CLAUDE.md`. Danach steht ein öffentlicher
Dienst im Netz, der die Vordertür zur vollständigen Kontrolle über beide
Rechner ist. Das ist die Verschlechterung, und sie ist nicht wegzudiskutieren.
Abgemildert wird sie nur durch den Entwurf aus Abschnitt 4: `rdcoord` als
dummer Briefkasten, der Verbindungen vermitteln, aber nichts befehlen kann.
Bleibt trotzdem: er kann dich aussperren, und er ist ab dann Ziel von
Anmeldeversuchen aus dem ganzen Internet. Rate-Limiting, Sperrlogik und
zeitnahe Updates sind ab dann Pflichtaufgabe, nicht Kür.

### Der elegante Teil: du musst nicht wählen

Der Agent spricht **schon WebRTC** — SIPSorcery mit H.264-Passthrough, heute
ohne STUN und TURN, weil beide Enden im Tailnet hängen. Und:

> **Eine Tailscale-Route ist für ICE nur ein weiterer Kandidat.**

Läuft Tailscale, gewinnt der direkte Kandidat und alles ist so schnell wie
heute. Läuft es nicht, greifen STUN und TURN. Gebaut wird also nicht
„Tailscale raus", sondern **„Tailscale nicht mehr nötig"** — und das kostet
keine Zeile extra, weil ICE es mitbringt. Für die Übergangszeit ist das
zugleich das Sicherheitsnetz: solange `rdcoord` noch nicht steht, läuft alles
weiter wie heute.

### Der Haken, der wirklich Arbeit macht

Ohne Tailscale gibt es keine routbare Adresse — also funktionieren
`/ws/input`, `/ws/screen` und **alle REST-Endpunkte** nicht mehr so, wie sie
gebaut sind. Alles muss in die WebRTC-Sitzung wandern:

| Heute | Danach |
|---|---|
| `WS /ws/input` | Data-Channel `input`, **ordered + reliable** |
| `WS /ws/screen` (JPEG-Rückfall) | Data-Channel `screen`, binär |
| `GET /api/info`, `POST /api/power`, `/api/media`, `/api/actions` | Request/Response-Umschlag über Data-Channel `control` |
| `POST /api/webrtc/offer` | Signalisierung über `rdcoord` |

Zwei Dinge sind dabei wichtig:

- **Der Data-Channel für Eingaben muss `ordered` und `reliable` sein.**
  Unordered mit `maxRetransmits: 0` wäre latenzärmer, kippt aber die Garantie
  aus `InputChannel.flushPendingMove`, dass ein Klick nie vor der Bewegung
  ankommt, die ihn positioniert — genau der Fehler, der in Phase 2 gefunden
  wurde.
- **Der Umschlag wird so gebaut, dass `agentClient.ts` und `inputChannel.ts`
  nur ihren Transport tauschen.** Dieselbe Schnittstelle, andere Unterlage —
  sonst wird aus dem Transport-Umbau ein App-Umbau.

Das ist ein Protokoll-Umbau, kein Verkabeln: grob **3–4 Wochen allein dafür**,
mehr als alle anderen Phasen zusammen.

### Zwei Risiken, die vor dem Bauen geprüft werden müssen

1. **Bringt SIPSorcery ICE mit TURN und SCTP-Data-Channels?** ICE und STUN sind
   vorhanden; TURN-Unterstützung und Data-Channels in Version 10.x müssen
   nachgewiesen werden, nicht angenommen. Hält es nicht, sind die Auswege ein
   Pion-Sidecar (Go) oder ein Wechsel des WebRTC-Stacks — beides teuer. **Diese
   Prüfung ist eine eigene Phase und kommt vor allem anderen.**
2. **Hat der Anschluss eine öffentliche IPv4?** Bei CGNAT ist kein
   Port-Forward möglich, dann fällt „`rdcoord` auf der NAS" weg und es muss ein
   VPS sein. Prüfen: FritzBox → Internet → Online-Monitor, WAN-Adresse mit
   einer externen „Wie ist meine IP"-Auskunft vergleichen. Sind sie
   verschieden, ist es CGNAT. Nebenbei: haben beide Enden **IPv6**, wird
   Hole-Punching deutlich zuverlässiger — deutsche Anschlüsse und Mobilfunk
   sind meist Dual-Stack, das spielt uns in die Hand.

### NAS oder VPS?

|  | NAS (`dockhand`-Stack) | VPS (~5 €/Monat) |
|---|---|---|
| Kosten | 0 | ~60 €/Jahr |
| Port 443 offen | ja, zu Hause | ja, bei Fremden |
| CGNAT | tödlich | irrelevant |
| TURN-Bandbreite | **kostet nichts extra** — das Ziel steht ohnehin zu Hause, der Relay-Hop ist NAS→Handy, also derselbe Upload wie bei P2P | zählt doppelt: Heim-Upload **und** VPS-Traffic |
| Ausfall des Heimanschlusses | dann ist der PC ohnehin unerreichbar | Laptop unterwegs bliebe steuerbar |
| WOL | im LAN, direkt möglich | braucht zusätzlich Weg 1 oder 3 aus Abschnitt 3 |
| Wartung | eigene Kiste, bekannte Umgebung, `nginx`/NPM und Let's Encrypt stehen schon | eine weitere Maschine zu pflegen |

**Empfehlung: NAS**, sofern die IPv4 öffentlich ist. Das Argument, das dabei am
meisten zählt: **der Zielrechner steht sowieso zu Hause.** Ein Ausfall des
Heimanschlusses macht `rdcoord` unerreichbar — aber dann wäre der PC es auch.
Der Kontodienst zu Hause führt also für den Hauptfall keinen neuen
Ausfallpunkt ein. Nur „Laptop unterwegs vom Handy unterwegs" wäre betroffen,
und das ist der seltene Fall.

Der Umzug auf einen VPS bleibt später eine Konfigurationsfrage, keine
Umbaufrage — vorausgesetzt, `rdcoord` bekommt von Anfang an seinen Hostnamen
aus der Konfiguration und nicht aus dem Code.

---

## 4b. Die App öffentlich machen

Das eigentliche Ziel hinter allen Vorschlägen: jeder soll Agent und Client
installieren können und ohne Zusatzaufwand alles nutzen. Das ändert die
Bewertung — und zwar zugunsten von Tailscale, nicht dagegen.

Es gibt drei Modelle. Sie unterscheiden sich weniger in der Technik als in dem,
was du dir dauerhaft auflädst.

### A — Nutzer bringen ihr eigenes Tailscale

Du veröffentlichst Agent und Client, sonst nichts. Keine Infrastruktur, keine
laufenden Kosten, kein fremdes Konto in deiner Obhut, keine Rechtspflichten.
Der Tailscale-Free-Tier (3 Nutzer, 100 Geräte) deckt jeden Privatnutzer
mühelos.

Die Reibung „erst noch Tailscale installieren" lässt sich fast wegautomatisieren:
der Tailscale-Client ist BSD-lizenziert und darf mitgeliefert werden. Der
Installer bringt ihn mit, der Einrichtungsassistent ruft `tailscale up` auf, der
Nutzer klickt einmal „mit Google anmelden". Aus „installiere zwei Programme und
verstehe VPN" werden drei Schritte in einem Assistenten. **Damit ist „ohne
zusätzlichen Aufwand" praktisch erfüllt** — für den Bruchteil des Aufwands von
Abschnitt 4a.

Zu prüfen bleibt die Redistributionsfrage: der Client ist offen, die
Koordination ist Tailscales Dienst; jeder Nutzer braucht ein eigenes Konto. Für
eine kostenlos veröffentlichte App unproblematisch, vor einer kommerziellen
Nutzung nachlesen.

### B — `rdcoord` als selbst hostbares Image

Für Nutzer, die kein Tailscale wollen: `rdcoord` als Docker-Image
veröffentlichen. Du betreibst eine Instanz für dich, andere ihre eigene. Keine
Fremddaten bei dir, keine fremde Bandbreite über deinen Anschluss.

Daraus folgt eine Entscheidung, die **jetzt** fallen muss und nicht später:
**die Coord-Adresse kommt aus der Konfiguration, nie aus dem Code.** Der Client
kennt drei Möglichkeiten — Tailscale, eigener Coord, öffentlicher Coord — und
der Nutzer wählt bei der Einrichtung. Nachträglich eingezogen ist das teuer.

### C — Du betreibst einen Rendezvous-Server für alle

Technisch dasselbe wie B, in der Verantwortung etwas völlig anderes. **Nicht auf
der NAS.** Was daran hängt:

- **Bandbreite.** TURN für Fremde heißt: dein Heim-Upload überträgt die
  Videoströme anderer Leute. Eine 1080p-Sitzung sind grob 5 Mbit/s. Bei 100
  Nutzern und 10–20 % Relay-Anteil sprengt das jeden Privatanschluss. Die
  *Kosten* wären dabei nicht das Problem — ein VPS mit 20 TB kostet ~5 €/Monat
  und trägt rund 9.000 Sitzungsstunden. Dein Anschluss ist das Problem.
- **Recht (DE).** Fremde Konten sind personenbezogene Daten: DSGVO,
  Datenschutzerklärung, Auskunft und Löschung, Meldepflicht binnen 72 Stunden.
  Ein öffentlich angebotener Dienst braucht ein Impressum (§ 5 DDG). Dazu
  untersagen die meisten Privatanschluss-AGB öffentliche Server. *(Kein
  Rechtsrat — nur die Liste, die dann abzuarbeiten wäre.)*
- **Haftung.** Wird dein Coord übernommen, sind fremde Rechner betroffen, und du
  warst der Weg dorthin.
- **Missbrauch.** Ein öffentlicher Fernsteuerungs-Vermittler zieht
  Support-Betrüger an. **RustDesk** ist der direkte Vorbildfall: dieselbe
  Produktidee, öffentliche Relays plus Self-Hosting — und die öffentlichen
  Relays sind dort der teure und missbrauchsanfällige Teil. Das ist keine
  Spekulation, sondern dokumentierte Erfahrung.

### Empfehlung: A und B, kein C

Tailscale als empfohlener Weg mit einem Assistenten, der die Einrichtung
übernimmt; `rdcoord` als optionales Image für die, die es anders wollen. Damit
ist „jeder installiert und nutzt alles" erfüllt, ohne dass du Infrastruktur für
Fremde betreibst.

Willst du später doch einen öffentlichen Server, dann auf einem VPS und als
eigene, bewusste Entscheidung mit Impressum, Datenschutzerklärung und
Missbrauchsprozess — nicht als Nebenprodukt eines Hobbyprojekts.

### Warum der Entwurf aus Abschnitt 4 alle drei trägt

Weil `rdcoord` ein dummer Briefkasten ist und die Erlaubnis ausschließlich beim
Agent liegt, ist es sicherheitstechnisch **gleichgültig, wessen Coord ein Nutzer
benutzt**. Auch deiner könnte niemanden steuern. Genau deshalb war das schon die
richtige Entscheidung, als es nur um dich ging — und deshalb ist der Wechsel
zwischen den Modellen später eine Konfigurationsfrage.

### Was ein öffentliches Publikum sonst noch verlangt

- **Wecken als Netz-Fähigkeit** (Abschnitt 3). Die meisten Nutzer haben einen
  einzigen Rechner und niemanden im LAN, der ihn wecken könnte. Der Knopf muss
  dann erklärt ausgegraut sein, nicht fehlschlagen.
- **Kein `devices.json` von Hand.** Einrichtung ausschließlich über den
  Assistenten und Kopplung per QR.
- **Fehlermeldungen ohne Vorwissen.** „Hub nicht erreichbar. Läuft Tailscale?"
  aus `hubClient.ts` ist für dich richtig und für Fremde nutzlos.
- **Lizenz und Repo-Hygiene.** Vor der Veröffentlichung: Lizenzdatei, README für
  Fremde, und die Prüfung, dass keine Tokens, MACs oder Tailnet-Namen in der
  Historie liegen (`.gitignore` deckt nur die Zukunft ab).
- **Englisch.** Code und Doku sind durchgängig deutsch. Für eine öffentliche App
  ist das eine bewusste Entscheidung — entweder deutsch bleiben und die
  Zielgruppe eingrenzen, oder die Oberfläche zweisprachig machen. Der
  Kommentarstil im Code darf deutsch bleiben.

---

## 5. Flexible Aktionen: Skripte, Programme, Custom Actions

Das ist der wirkungsvollste Punkt der ganzen Liste — und der einzige, bei dem
ein Fehler im Entwurf teuer wird. Beliebige Befehle über das Netz ausführen
ist Remote-Code-Execution, absichtlich gebaut.

### Die eine Regel: deklariert wird am Agent, aufgerufen wird per ID

Der Client schickt **niemals** eine Kommandozeile. Er schickt
`{"action": "obs-aufnahme"}`. Was das bedeutet, steht in `actions.json` neben
der `appsettings.json` auf dem Zielrechner:

```jsonc
[
  { "id": "obs-aufnahme", "label": "OBS aufnehmen", "icon": "record",
    "type": "process", "file": "C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe",
    "args": ["--startrecording"], "workingDirectory": "…" },

  { "id": "backup", "label": "Backup", "icon": "archive",
    "type": "script", "file": "C:\\Scripts\\backup.ps1", "confirm": true },

  { "id": "monitor-2", "label": "Nur Monitor 2", "type": "keys",
    "chord": ["LWin", "P"] },

  { "id": "jira", "label": "Jira öffnen", "type": "url",
    "url": "https://…" },

  { "id": "abendmodus", "type": "sequence",
    "steps": [{ "action": "monitor-2" }, { "delayMs": 500 }, { "action": "obs-aufnahme" }] }
]
```

Daraus folgt alles Weitere:

- `args` ist ein **Array**, nie eine Zeichenkette. Kein Shell-Aufruf, keine
  Interpolation, also keine Einschleusung. Dieselbe Linie hält der Agent schon
  bei ffmpeg (siehe `docs/SICHERHEIT.md`, „ffmpeg wird als Prozess gestartet").
- `type: "script"` startet eine **hinterlegte Datei** mit
  `powershell -NoProfile -ExecutionPolicy Bypass -File <pfad>` — keinen über
  das Netz gelieferten Skripttext.
- Client-gelieferte Parameter gibt es in V1 **nicht**. Später denkbar mit
  ausdrücklich deklarierten, typisierten Feldern und Wertelisten — aber nicht
  als offene Freitextübergabe.
- `confirm: true` verlangt eine Rückfrage im Client. `elevated: true` wäre
  möglich, würde aber ein UAC-Fenster auf dem Sperrdesktop öffnen, das aus der
  Ferne niemand bestätigen kann — deshalb erst einmal weglassen.

### Bearbeiten: lokal, nicht über das Netz

`GET /api/actions` ist lesend und über das Netz verfügbar — sonst könnte kein
Client Knöpfe bauen. `POST /api/actions/{id}/invoke` führt aus. **Schreiben
geht nur lokal**, im Agent-Fenster auf dem betreffenden Rechner. Ein
Schreib-Endpoint über das Netz wäre gleichbedeutend mit „jeder gültige Token
darf beliebigen Code hinterlegen" und macht die Regel oben wertlos.

Das ist eine echte Einschränkung: neue Aktionen anlegen heißt, an den Rechner
zu gehen (oder ihn per Client fernzusteuern — was zulässig ist, weil dann ein
Mensch die Bestätigung sieht). Wenn dir das zu unbequem wird, ist der
Kompromiss eine Freigabe-Rückfrage als Windows-Toast auf dem Zielrechner. Das
gehört nach V2, nicht hinein.

### Was dadurch aufgeräumt wird

Die heutigen Shortcuts liegen im `localStorage` des Handys (`shortcuts.ts`) und
sind nur Tastenkombinationen. Nach dem Umbau kommen sie vom Zielgerät, gelten
für jeden Client, überleben eine Neuinstallation der App und können mehr als
Tasten. Die alten Shortcuts bleiben als lokale Ergänzung erhalten — sie
brauchen keinen Zugriff auf den Rechner und funktionieren auch bei einem Agent
ohne `actions.json`.

---

## 6. Widgets, Quick-Settings, App-Shortcuts

Setzt Abschnitt 5 voraus: ohne serverseitig deklarierte Aktionen hat ein Widget
nichts anzuzeigen.

| Fläche | Umsetzung | Grenze |
|---|---|---|
| **Home-Widget** | `AppWidgetProvider` + `RemoteViews`-Raster; Tipp → `PendingIntent` → `WorkManager` → `POST /api/actions/{id}/invoke` | `RemoteViews` kann nur wenige Layouts. Knopfraster ja, Video nein |
| **Quick-Settings-Tile** | `TileService`, ab Android 13 per `requestAddTileService` selbst platzierbar | Praktisch 1–2 Tiles. Gut für „PC wecken" / „PC schlafen" |
| **App-Shortcuts** (Icon lange drücken) | `ShortcutManager`, dynamisch aus der Aktionsliste | 4–5 sichtbar. Billigster Weg, deckt vermutlich den Großteil des Bedürfnisses |
| **Foreground-Service** | Laufende Sitzung mit Benachrichtigung offenhalten | Braucht Typ-Deklaration ab Android 14 |

Ein Widget-Tipp ohne Netzpfad zum Agent muss sichtbar scheitern (Toast), nicht
still. Ein schlafender PC bekommt keinen Befehl — außer das Widget ist an eine
WOL-Aktion gebunden.

---

## 7. Updates über GitHub

`SelfUpdater.cs` ist zu 80 % das, was gebraucht wird: Manifest holen, SHA-256
prüfen, per Batch tauschen, Startschleife über einen Merkzettel verhindern.
Geändert wird die Quelle und die Vertrauenskette.

### Agent

- Quelle: `GET https://api.github.com/repos/<owner>/RemoteDesktop/releases/latest`
  → Asset `RemoteDesktopAgent.exe` und `manifest.json`
- **Signatur statt nur Prüfsumme.** Ein Hash aus derselben Quelle wie die
  Datei schützt gegen abgebrochene Downloads, nicht gegen ein übernommenes
  GitHub-Konto. Der Agent hat vollständige Kontrolle über den Rechner, das
  rechtfertigt eine Signatur: `manifest.json` wird mit ECDSA P-256 signiert,
  der öffentliche Schlüssel ist in den Agent kompiliert, geprüft wird mit
  `ECDsa.VerifyData` — in .NET 8 eingebaut, keine Abhängigkeit. Der private
  Schlüssel liegt nur auf der NAS und wird beim Release-Bauen benutzt, nie im
  Repo.
- **Knopfdruck**: `POST /api/update` löst die Prüfung sofort aus, statt auf den
  nächsten Start zu warten. Der Client zeigt vorher an, dass eine neue Version
  daliegt.
- `/api/info` meldet zusätzlich `version` und `protocol`. Bei ungleichem
  `protocol` sagt der Client klar, welche Seite zu alt ist, statt an einer
  unbekannten Nachricht zu scheitern. Ohne das wird die getrennte
  Aktualisierbarkeit zur Fehlerquelle.
- Unauthentifizierte GitHub-API: 60 Anfragen pro Stunde und IP. Für eine
  Prüfung beim Start und eine auf Knopfdruck völlig ausreichend.

### Android

- Dasselbe Release, Asset `remotedesktop.apk`. Der Client vergleicht
  `versionCode`, lädt herunter, übergibt an die `PackageInstaller`-Session.
- **Grenze, die sich nicht umgehen lässt:** außerhalb von Google Play zeigt
  Android **immer** einen Bestätigungsdialog, und die App braucht
  `REQUEST_INSTALL_PACKAGES`. „Ein Knopf und fertig" heißt hier: ein Knopf und
  ein Systemdialog. Silent Updates gibt es nur über Play (25 $ einmalig,
  Review, und ein bereits sideloadeter APK lässt sich davon nicht mehr
  aktualisieren, weil die Signatur nicht passt).
- Alternative ohne eigenen Code: **Obtainium** beobachtet GitHub-Releases und
  aktualisiert die APK. Wenn der Aufwand knapp wird, ist das der Weg — der
  eingebaute Weg sind aber nur ~50 Zeilen und eine Berechtigung.

### CI

`.github/workflows/release.yml` auf einen Tag: `dotnet publish` (win-x64,
self-contained, single-file), `vite build`, Gradle-`assembleRelease` mit einem
Keystore aus den Repository-Secrets, `manifest.json` signieren, alles als
Release-Assets anhängen. Ab dann ist „neue Version rausbringen" ein `git tag`.

---

## 8. Repo nach dem Umbau

```
agent/              .NET-Dienst — Kern bleibt, dazu actions/, pairing/, wol/
agent.Tests/        wächst mit
desktop/            WinForms + WebView2 + Tray (Windows-Client)
app/                geteilte React-UI — Quelle für beide Clients
clients/android/    Capacitor-Projekt + Kotlin (Widget, Tile, Installer, FGS)
waker/              destillierter Hub: nur WOL + Probe     (git mv hub/ waker/)
docs/
.github/workflows/

rdcoord/            zurückgestellt (Abschnitt 4a) — erst ab Phase 18
```

`app/` behält den Namen und wird zur geteilten Oberfläche. Beide Clients laden
dasselbe Bundle; die Unterschiede kommen aus zwei Schichten:

```
app/src/platform/
  index.ts        Schnittstelle: storage, keystore, capabilities, update, clipboard, qr
  web.ts          heute (localStorage, fetch)      — hält die PWA am Leben
  capacitor.ts    Android (Preferences, Plugins, Kamera)
  webview2.ts     Windows (Host-Bridge, Pointer Lock, echte Tastatur)

app/src/transport/
  index.ts        Schnittstelle: control(), inputChannel(), screenStream()
  direct.ts       HTTPS + WSS an host:8443 — der gebaute Weg
  rtc.ts          (später) WebRTC über rdcoord, alles über Data-Channels
```

Die Transportschicht wird **jetzt schon** eingezogen, obwohl es vorerst nur eine
Umsetzung gibt. Sie kostet in Phase 9 rund einen Tag und ist die einzige Stelle,
an der ein späterer Wechsel auf `rdcoord` (Abschnitt 4a) überhaupt bezahlbar
bleibt: `agentClient.ts` und `inputChannel.ts` wissen dann nicht mehr, worüber
sie reden. Nachträglich eingezogen wäre sie ein Umbau der halben App.

`waker/` erbt aus `hub/`, was noch taugt: `wol.ts`, `probe.ts`, `auth.ts` und
die Dockhand-Anbindung. Weg fallen PWA-Auslieferung, Geräte-Registry mit
Klartext-Tokens und `agentRelease.ts`.

---

## 9. Phasenplan

Reihenfolge nach Abhängigkeiten: Kopplung vor den Clients, weil beide sie
brauchen; Aktionen vor den Widgets, weil ein Widget sonst leer ist; die
Veröffentlichung zuletzt, weil erst dann feststeht, was der Assistent
einzurichten hat.

Nach jeder Phase steht etwas Nutzbares, und die PWA läuft die ganze Zeit
weiter — der Weg zurück ist bis zuletzt offen.

### Phase 9 — Plattform- und Transportschicht (kein sichtbarer Unterschied)
- `app/src/platform/` mit `web.ts`, `app/src/transport/` mit `direct.ts`
- `agentClient.ts` und `inputChannel.ts` sprechen nur noch die
  Transport-Schnittstelle
- Geräteliste aus dem lokalen Speicher statt vom Hub
- Abnahmekriterium: die bestehenden 78 Tests bleiben **unverändert** grün
- Grob 3–4 Tage

### Phase 10 — Kopplung und Geräteschlüssel
- Agent: Schlüsselpaar (ECDSA P-256) bei der Einrichtung, `clients.json` mit
  Public Keys und Scopes, `POST /api/pair` mit 6-stelligem Code (5 Minuten,
  einmalig), Widerruf
- `TokenAuth` wird zu `ClientAuth`: mehrere zugelassene Schlüssel, Vergleich
  weiter in fester Zeit
- Challenge-Response innerhalb der Verbindung — nicht am SDP-Fingerprint
- App: Kopplungsansicht mit Code (QR kommt mit der Kamera in Phase 13)
- Tests: Codeablauf, Einmalverwendung, Scope-Prüfung, Widerruf, Signaturprüfung
- Grob 4–5 Tage

### Phase 11 — Windows-Client
- `desktop/` mit WebView2-Fenster und **Tray-Icon** (schließt den offenen
  Punkt aus Phase 1)
- `platform/webview2.ts`: Pointer Lock, echte Tastatur, Zwischenablage
- Selbstverbindung sperren; Prüfung auf die WebView2-Runtime mit klarer Meldung
- Kopplungs- und Aktions-Verwaltung als lokale Ansicht in diesem Fenster —
  hier entsteht die Oberfläche, die den Kopplungscode anzeigt
- Grob 4–5 Tage

### Phase 12 — Android-APK
- Capacitor-Projekt, `platform/capacitor.ts`, Preferences statt localStorage
- Foreground-Service für die laufende Sitzung
- QR-Scanner für die Kopplung
- Debug-APK auf dem Handy gegen die PWA vergleichen: Gesten, Tastatur,
  H.264-Latenz
- Grob 3–4 Tage plus Gerätetests

### Phase 13 — Aktionen am Agent
- `actions.json` mit Zod-artiger Validierung beim Start (wie `hub/src/config.ts`
  es vormacht): unbekannter `type`, fehlende Datei, `args` als String → Abbruch
  mit Klartext, nicht erst beim Auslösen
- `GET /api/actions`, `POST /api/actions/{id}/invoke`, Typen `process`,
  `script`, `keys`, `url`, `sequence`
- Editor im Windows-Fenster (lokal), Aktionsansicht in beiden Clients
- Tests: Validierung, Argumentbildung ohne Shell, Sequenzen mit Verzögerung
- Grob 4 Tage

### Phase 14 — Updates über GitHub, Hub zu `waker` schrumpfen
- `SelfUpdater` auf GitHub Releases umstellen, ECDSA-Signaturprüfung ergänzen
- `POST /api/update`, `version` und `protocol` in `/api/info`,
  Kompatibilitätsmeldung im Client
- Android: Versionsprüfung, Download, `PackageInstaller`
- `.github/workflows/release.yml`, Signaturskript für die NAS
- **WOL zur Netz-Fähigkeit machen**: `wol.ts` nach C# portieren, `POST /api/wol`
  am Agent, `canWake` und `siteId` in der Fähigkeitsliste — Rechner wecken sich
  gegenseitig
- **`siteId` aus der Gateway-MAC** in Agent und Waker; Client wählt den Waker
  selbst (Abschnitt 3, „Mehrere Standorte, ein Knopf"). Tests: gleiche `siteId`
  wird gefunden, fremde nicht, kein Kandidat → Knopf ausgegraut
- `git mv hub/ waker/`: PWA-Auslieferung, Geräte-Registry und `agentRelease.ts`
  raus. Übrig bleiben WOL, `siteId` und Kopplung — keine Gerätekonfiguration
  mehr. Multi-Arch-Image (amd64 + arm64), Dockhand-Stack neu deployen
- `docs/SICHERHEIT.md` fortschreiben: die Token-Bündelung im Hub ist damit weg
  — heute liefert er die Agent-Tokens **aller** Geräte an jeden aus, der das
  Hub-Token kennt
- Grob 5 Tage

### Phase 15 — Android-Flächen
- Widget mit Aktionsraster, Quick-Settings-Tile („wecken" / „schlafen"),
  dynamische App-Shortcuts
- Weckknopf nur aktiv, wenn im Netz des Ziels ein weckfähiger Knoten erreichbar
  ist (Abschnitt 3) — sonst ausgegraut mit Begründung
- Grob 4–5 Tage

### Phase 16 — Veröffentlichung
Erst hier wird aus dem eigenen Werkzeug eine App für Fremde (Abschnitt 4b).
- **Einrichtungsassistent**: Tailscale mitliefern, `tailscale up` aufrufen,
  Anmeldung im Browser, Kopplung per QR. Drei Schritte, kein `devices.json`
- Fehlermeldungen für Menschen ohne Vorwissen — „Hub nicht erreichbar. Läuft
  Tailscale?" aus `hubClient.ts` ist für dich richtig und für Fremde nutzlos
- Coord-Adresse **aus der Konfiguration**, damit Modell B (Self-Hosting) später
  ohne Umbau möglich bleibt
- Lizenzdatei, README für Fremde, Historie auf Tokens, MACs und Tailnet-Namen
  prüfen (`.gitignore` deckt nur die Zukunft ab)
- Entscheidung Sprache: deutsch bleiben oder Oberfläche zweisprachig
- Grob 5–7 Tage

---

**Ab hier zurückgestellt (Entscheidung vom 30.07.2026).** Die folgenden Phasen
lösen Tailscale ab. Sie bleiben stehen, weil die Analyse gilt und die Frage nach
Phase 16 erneut ansteht — gebaut werden sie vorerst nicht.

### Phase 17 — Machbarkeitsprüfung (Wegwerf-Code, aber entscheidend)
Vor jeder Zeile Produktivcode für Phase 18:
- **SIPSorcery**: ICE mit TURN-Server und ein SCTP-Data-Channel mit binärer
  Nutzlast. Nachweis, nicht Annahme. Scheitert es, sind die Auswege ein
  Pion-Sidecar (Go) oder ein anderer WebRTC-Stack — das ändert alles Weitere
- **Anschluss**: öffentliche IPv4 oder CGNAT? IPv6 auf beiden Seiten? Daraus
  folgt NAS oder VPS (Abschnitt 4a)
- coturn im Dockhand-Stack: 3478/UDP, 5349/TLS, eingeschränkter Relay-Bereich
- Grob 2–3 Tage

### Phase 18 — `rdcoord`: Konto und Signalisierung
Ab hier wird das System ein anderes. Der Direktweg bleibt die ganze Zeit
funktionsfähig — abgeschaltet wird er erst in Phase 20.
- `git mv hub/ rdcoord/`: PWA-Auslieferung, Geräte-Registry mit Klartext-Tokens
  und `agentRelease.ts` raus
- Konto mit Passkey (WebAuthn) plus Wiederherstellungscode, Geräteliste mit
  Public Keys, Rate-Limiting und Sperrlogik von Anfang an
- Signalisierungs-Briefkasten: Agent und Client halten je eine WSS-Verbindung
  offen, SDP und ICE-Kandidaten laufen durch
- coturn als eigener Container, Zugangsdaten kurzlebig und pro Sitzung
- Öffentliche Erreichbarkeit über den bestehenden `nginx`/NPM-Stack samt
  Let's Encrypt; DDNS einrichten
- **Prüfen, dass ein übernommenes `rdcoord` keinen Rechner steuern kann** — ein
  eigener Testfall, nicht bloß eine Absicht
- Grob 8–10 Tage

### Phase 19 — Transport-Umbau: alles über Data-Channels
Der größte Einzelposten des Plans (Abschnitt 4a).
- Agent: ICE mit STUN und TURN, Data-Channels `control`, `input`, `screen`
- `input` **ordered und reliable** — sonst kippt die Garantie aus
  `InputChannel.flushPendingMove`
- Request/Response-Umschlag für `/api/info`, `/api/power`, `/api/media`,
  `/api/actions`; JPEG-Rückfall über `screen`
- `transport/rtc.ts` in der App, umschaltbar gegen `direct.ts`
- Messen: Eingabe-Latenz und Bildlatenz über P2P **und** über TURN, jeweils im
  WLAN, im Mobilfunk und mit abgeschaltetem Tailscale
- Grob 15–20 Tage

### Phase 20 — Tailscale wird optional
- WOL: `wol.ts` nach C# portieren, `POST /api/wol` am Agent, `canWake` in der
  Fähigkeitsliste; `rdcoord` bleibt der Rückfall für „alles aus"
- Status kommt aus der Signalisierung statt aus einer TCP-Probe — `rdcoord`
  weiß ohnehin, wer verbunden ist
- Ein Durchlauf mit **deinstalliertem** Tailscale auf Handy und PC. Das ist der
  Abnahmetest dieser Phase
- `tailscale cert` entfällt: die Clients prüfen Geräteschlüssel, keine
  Web-PKI-Zertifikate mehr
- `docs/ARCHITEKTUR.md` und `docs/SICHERHEIT.md` neu schreiben — die
  Grundannahme „nichts ist aus dem Internet erreichbar" gilt nicht mehr und
  darf nicht als veraltete Zusicherung stehen bleiben
- Grob 4–5 Tage

### Phase 21 — Optional, nach Bedarf
- `rdcoord` auf einen VPS umziehen (Konfiguration, kein Umbau)
- FritzBox-TR-064 als WOL-Weg
- Inno-Setup- oder MSIX-Installer für Windows
- Nativer WebRTC-Stack im Android-Client, falls die WebView-Latenz je störn sollte
- Mehrbenutzer-Konten, geteilte Geräte — erst wenn es wirklich mehr als ein
  Nutzer wird

### Aufwand

| Abschnitt | Tage |
|---|---|
| **Phasen 9–16 — wird gebaut** (Apps, Kopplung, Aktionen, Widgets, Updates, Veröffentlichung) | **31–42** |
| Phasen 17–20 — zurückgestellt (Tailscale ablösen) | 29–38 |

Die Phasen 9–16 sind gut zwei Monate nebenher und liefern **alles, was du an
Funktionen wolltest**: echte Apps auf Android und Windows, Aktionen mit
Skripten und Programmen, Widgets, Updates auf Knopfdruck, Kopplung per QR statt
Token-Abtippen — und eine App, die andere installieren können.

Die zurückgestellten Phasen kosten noch einmal genauso viel und liefern keine
einzige neue Funktion, sondern nur Unabhängigkeit von Tailscale. Deshalb steht
die Entscheidung darüber **nach Phase 16** an, nicht heute — dann mit zwei
Monaten Erfahrung mehr und mit echten Nutzern, die zeigen, ob die
Tailscale-Einrichtung tatsächlich die Hürde ist, für die man sie hält.

---

## 10. Was bleibt, wie es ist

Ehrlich zusammengezogen, damit keine falschen Erwartungen entstehen:

1. **Ein Mittelmann bleibt — immer.** Nicht als Entwurfsschwäche, sondern weil
   niemand die wechselnde öffentliche Adresse hinter einem NAT kennt. Weg fällt
   nur, wer der Mittelmann ist: statt Tailscale dann `rdcoord`. Der
   Nutz-Traffic läuft danach direkt, die Signalisierung nie.
2. **10–20 % der Verbindungen brauchen einen Relay.** Symmetrisches NAT im
   Mobilfunk, CGNAT, Firmen-Firewalls. Ohne coturn sind das ausgefallene
   Verbindungen, nicht langsame.
3. **Ein Gerät im LAN muss wach sein, um einen schlafenden PC zu wecken.** WOL
   direkt vom Handy aus dem Mobilfunknetz gibt es nicht — ein schlafender
   Rechner führt keine Software aus. Kandidaten: anderer Agent, `rdcoord`,
   FritzBox.
4. **Der Account entscheidet nicht allein über den Zugriff.** Ein neues Gerät
   braucht immer eine Bestätigung am Zielrechner. Sonst wäre der Kontodienst
   der Generalschlüssel zu deinen PCs.
5. **Erst ab Phase 18 wäre etwas aus dem Internet erreichbar.** Das ist die
   Kehrseite von „aus jedem Netz". Rate-Limiting, Sperrlogik und zeitnahe
   Updates werden damit zur Pflichtaufgabe.
6. **Android-Updates verlangen immer einen Tipp.** Silent Update gibt es nur
   über Google Play, und dorthin führt von einem sideloadeten APK kein Weg.
7. **Unsignierte Windows-Binaries lösen SmartScreen aus.** Signieren kostet
   Geld; für zwei eigene Rechner ist die Warnung hinnehmbar.
8. **Eigene Aktionen sind RCE, absichtlich.** Deshalb: deklariert am Agent,
   `args` als Array, kein Shell-Aufruf, Schreiben nur lokal.
9. **Sperrbildschirm und UAC bleiben unerreichbar** — unabhängig von der
   Client-Plattform. Gleichzeitig eine Sicherheitseigenschaft; siehe
   `docs/SICHERHEIT.md`.
10. **Android wird kein Agent.** Eingabe-Injektion gibt es nicht ohne Root.
11. **Die WebView bleibt eine WebView.** Der Umbau verbessert Reichweite,
    Bedienung und Verwaltung — nicht die Bild-Latenz.

## 11. Entscheidungen

### Bereits entschieden (30.07.2026)

- **Tailscale bleibt der Netzwerkpfad.** Die Phasen 17–20 sind zurückgestellt,
  die Analyse in Abschnitt 4a bleibt als Grundlage stehen.
- **Kein Rendezvous-Server für Fremde auf der NAS.** Bandbreite, DSGVO,
  Impressumspflicht und Missbrauchsrisiko stehen in keinem Verhältnis
  (Abschnitt 4b, Modell C).
- **Veröffentlicht wird nach Modell A + B:** Agent und Client als Download,
  Tailscale-Einrichtung über einen Assistenten, `rdcoord` später optional als
  Self-Hosting-Image.

### Offen, bevor Phase 9 beginnt

1. **Android: Capacitor oder Kotlin-Neubau?** Empfehlung Capacitor; ein Neubau
   verwirft `screenGestures.ts`, `softKeyboard.ts`, `keyboardLayout.ts`,
   `inputChannel.ts` und 78 Tests, und die Android-Eigenheiten verschwinden
   dadurch nicht.
2. **Sprache der Oberfläche.** Alles ist heute deutsch. Für eine öffentliche App
   entweder bewusst deutsch bleiben und die Zielgruppe eingrenzen, oder die
   Oberfläche zweisprachig anlegen. Nachträglich eingezogen ist i18n teuer —
   die Entscheidung gehört vor Phase 9, umgesetzt wird sie in Phase 16.
3. **Lizenz.** MIT oder Apache-2.0 für eine App, die volle Kontrolle über einen
   Rechner gibt? Apache-2.0 enthält eine ausdrückliche Haftungs- und
   Patentklausel und ist hier die vorsichtigere Wahl.

### Offen, nach Phase 16

**Lohnt sich das Ablösen von Tailscale?** Die Frage lautet dort nicht „geht
das?" — das ist geklärt —, sondern: *ist es dir wert, einen öffentlich
erreichbaren Dienst zu betreiben, der die Vordertür zu deinen Rechnern ist?*
Heute ist aus dem Internet nichts erreichbar; das ist die stärkste Eigenschaft
des Aufbaus, und sie ginge dabei verloren.

Entschieden wird das mit echten Nutzern in der Hand — die zeigen dann, ob die
Tailscale-Einrichtung tatsächlich die Hürde ist, für die man sie heute hält.
