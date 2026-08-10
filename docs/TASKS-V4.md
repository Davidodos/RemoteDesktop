# V4 — in beide Richtungen, und mit Dateien

Arbeitsanweisung nach V3. Zwei Wünsche vom 09.08.2026, beide groß:

1. **Ein Handy soll sich ebenso steuern lassen wie ein PC** — von einem anderen
   Handy und vom Windows-Fenster aus. Bild sehen, klicken, tippen. Kein WOL,
   keine Power-Aktionen, keine Shortcuts, keine Aktionen: das sind Dinge, die
   ein Handy entweder nicht kann oder nicht soll.
2. **Ein Dateimanager.** Die Dateien des verbundenen Geräts durchsehen, eine
   Datei ansehen, eigene Dateien hinaufladen, fremde herunterladen.

Reihenfolge nach Entscheidung vom 09.08.2026: **erst das Handy als Ziel**, dann
der Dateimanager — der bekommt dadurch beide Seiten in einem Durchgang, statt
zweimal angefangen zu werden.

## Der Leitsatz: das Handy wird ein Agent

Es gibt keinen „Handy-Modus" in der App. Das Handy spricht dasselbe Protokoll
wie der Windows-Agent — `/api/info`, `/api/pair`, `/api/session`, `/ws/input`,
`/api/webrtc/offer` — auf demselben Port 8443, mit demselben TLS-Aufbau und
derselben Kopplung. Die App fragt nicht, *was* am anderen Ende steht.

Das ist dieselbe Entscheidung wie bei den vier Netzmodi: **keine
Fallunterscheidung im Code.** Der Preis ist ein größerer Android-Teil; der
Gewinn ist, dass Bildschirmansicht, Zeiger-Overlay, Bildschirmtastatur,
Texteingabe, Kopplung, Gerätliste, Widerruf und der ganze Transport unverändert
weiterlaufen. Ein zweiter Weg dorthin wäre ein zweiter Weg, den man kaputt
machen kann.

**Was ein Gerät kann, sagt es selbst.** Neu in `/api/info`:

```json
"capabilities": ["screen", "input", "keys", "media", "power", "actions", "wake", "files"]
```

Die App baut ihre Seitenleiste daraus. Ein Handy meldet `screen`, `input`,
`files` — und Power, Medien, Aktionen, Shortcuts und der Weckknopf sind dort
schlicht nicht da. Kein `if (istHandy)`, nirgends.

| Kennung | Bedeutung |
|---|---|
| `screen` | liefert ein Bild |
| `input` | nimmt Zeiger- und Texteingaben an |
| `keys` | versteht echte Tastendrücke (Strg+…, F-Tasten). Daran hängen die Shortcuts |
| `media` · `power` · `actions` · `wake` | wie bisher |
| `files` | hat den Dateidienst |

Ein Agent ohne das Feld ist älter als V4. Dann gilt die Windows-Liste von
damals — alles außer `files`. Sonst verlöre jeder noch nicht aktualisierte PC
beim App-Update seine halbe Oberfläche.

**`AgentVersion.Protocol` bleibt bei 1.** Die Regel dort ist ausdrücklich: nur
erhöhen, wenn die alte Seite die neue nicht mehr versteht. Hier versteht sie
sie — ein alter Agent lässt das Feld weg, eine alte App überliest es. Ein
höherer Wert hieße bloß, dass jeder noch nicht aktualisierte PC eine Warnung
zeigt, die nichts bedeutet.

Ob es „mehrere Monitore" als Fähigkeit braucht, hat sich beim Bauen erledigt:
die Monitor-Auswahl erscheint ohnehin erst ab zwei Einträgen in der Liste. Eine
Fähigkeit, die niemand abfragt, ist eine Fähigkeit zu viel.

## Was das Handy **nicht** können wird, und warum

Das gehört an den Anfang, nicht in die Fußnoten:

- **Die Bildschirmaufnahme braucht einmal eine Hand am Gerät.** Android fragt
  bei `MediaProjection` jedes Mal mit einem Systemdialog nach; das lässt sich
  ohne Root nicht umgehen. Einmal bestätigt, läuft die Aufnahme, solange der
  Vordergrunddienst lebt — also auch mit gesperrtem Bildschirm und über Tage.
  Aber **nach einem Neustart des Handys muss jemand wieder tippen.** Ein Handy,
  das unbeaufsichtigt in der Schublade liegt und dort neu startet, ist bis
  dahin nicht einsehbar.
- **Eingaben laufen über die Bedienungshilfen.** Eine gewöhnliche App darf
  keine Berührungen in fremde Apps schicken; die einzige Tür ohne Root ist ein
  `AccessibilityService`, den der Nutzer in den Einstellungen selbst
  freischaltet. Er kann tippen, wischen, lange drücken, zurück/Home/Übersicht
  — und Text in das gerade fokussierte Eingabefeld schreiben.
- **Es gibt keine echten Tastendrücke.** Kein Strg+C, keine F-Tasten, keine
  Pfeiltasten in einem Spiel. Deshalb meldet das Handy `keys` nicht, und die
  Bildschirmtastatur zeigt dort nur die Seiten, die auch ankommen.
- **Kein Aufwecken.** Ein schlafendes Handy hört auf kein Magic Packet.

## Ablauf je Phase

Wie in `docs/TASKS-V3.md`: Zustand lesen → nur diese Phase bauen → Abnahme
nachweisen → hier eintragen → committen.

## Umgebung

| | |
|---|---|
| App-Tests | `cd app && npm test` |
| Agent-Tests | `cd agent.Tests && dotnet test` |
| Einrichtungs-Tests | `cd setup.Tests && dotnet test` |
| Kotlin-Tests | `cd clients/android/android && ./gradlew testDebugUnitTest` |
| Windows-Client | `cd desktop && dotnet build` — baut auch auf Linux, läuft dort nicht |
| APK | `cd clients/android && npm run apk` — baut hier, läuft hier nicht |

---

# Teil A — das Handy als Ziel

## Phase 27 — `capabilities`: die Oberfläche folgt dem Gerät ✅

**Warum zuerst.** Solange die App annimmt, dass am anderen Ende Windows steht,
kann kein Handy dazukommen, ohne dass die halbe Oberfläche ins Leere zeigt.

- `agent/Services/AgentCapabilities.cs`: die Liste, die ein Windows-Agent
  meldet — alles außer `files`, denn den Dienst gibt es noch nicht. Eine
  Fähigkeit anzukündigen, die man nicht hat, ist schlimmer als zu schweigen
- `/api/info` liefert sie mit
- `app/src/lib/capabilities.ts`: `capabilitiesOf(info)` mit der Rückfallliste
  für Agents ohne das Feld, `can(info, 'power')`. Was diese App nicht kennt,
  wird verworfen statt durchgereicht
- `Sidebar` zeigt nur, was das Gerät kann (`pageAvailable`). Die App merkt sich
  den *Wunsch* und zeigt die *mögliche* Seite: wer von einem Handy zurück auf
  den PC wechselt, landet wieder auf „Ein/Aus". Entschieden beim Zeichnen, nicht
  in einem Effekt — sonst blitzte eine Seite auf, die es dort nicht gibt
- Die Auskunft hat genau einen Besitzer: einen Effekt an `selected`. `select()`
  fragt zwar auch, aber nur, um über das Verbinden zu entscheiden

**Abnahme (09.08.2026):**
App-Tests **354 grün** (vorher 314) · Agent-Tests **353 grün** (vorher 336).
Ein Test hält fest, dass Fähigkeit und Recht dort gleich heißen, wo sie
dasselbe meinen — die App vergleicht beide Listen, und ein Tippfehler auf einer
Seite fiele sonst erst vor einer leeren Oberfläche auf.

Nachgetragen aus dem Bauen: `keys` steuert in dieser Phase nur die
Shortcuts-Seite. Welche Tasten die Bildschirmtastatur am Handy noch anbieten
darf, entscheidet sich in Phase 30 — vorher weiß niemand, was dort ankommt.

## Phase 28 — Der Handy-Agent: Server, TLS, Kopplung ✅

Neues Kotlin-Paket `clients/android/.../host/`. Kein Capacitor-Kram im Kern:
reines Kotlin, damit `./gradlew testDebugUnitTest` es prüfen kann — und das tut
es, bis hinunter zum TLS-Handschlag.

**Was gebaut wurde**

- `Der.kt` · `HostCertificate.kt` — eigene CA plus Serverzertifikat, beim ersten
  Start erzeugt. Der Fingerabdruck geht über den QR-Code, ein zweiter,
  unverschlüsselter Port liefert `/ca.crt`. Genau wie beim Windows-Agent, damit
  derselbe Client beides versteht
- `HttpServer.kt` — HTTP/1.1 über `SSLServerSocket`, ein Thread je Verbindung
- `HostServer.kt` — `/health`, `/api/info`, `/api/pair`, `/api/session`,
  `/api/clients` und die Zugangsprüfung als Sperre für den ganzen Baum
- `HostIdentity` · `PairingCodes` · `ChallengeStore` · `SessionStore` ·
  `ClientStore` · `PairingService` — Zeile für Zeile dasselbe wie
  `agent/Auth/`. Kein Zufall: die Gegenseite ist derselbe Client
- `HostService.kt` — Vordergrunddienst, damit der Server das Wegwischen der App
  überlebt. `HostPlugin.kt` als Brücke, `views/ShareView.tsx` als Oberfläche

**Drei Entscheidungen, die im Plan noch offen waren**

- **Weder Ktor noch BouncyCastle.** Android kann keine Zertifikate ausstellen,
  und die übliche Antwort darauf — BouncyCastle — kostet rund acht Megabyte.
  Ktor für sechs Endpunkte noch einmal vier bis fünf. Beides fällt bei jedem
  Selbst-Update wieder an. Stattdessen ein DER-Schreiber und ein HTTP-Server
  von Hand, zusammen keine tausend Zeilen und vollständig unter Test. Der
  entscheidende Test ist `HostServerTest`: er geht über eine **echte**
  TLS-Verbindung von außen durch Kopplung und Anmeldung. Wäre am selbst
  kodierten Zertifikat ein Byte falsch, käme er nicht bis zur ersten Antwort
- **WebSockets sind noch nicht dabei.** Sie kommen mit dem Eingabe-Socket in
  Phase 30. Ein Rahmenformat auf Vorrat zu bauen, das noch niemand benutzt,
  wäre die Art Vorratshaltung, die dieses Projekt sonst vermeidet
- **Der QR-Code braucht einen Erzeuger.** `qrcode` als Abhängigkeit der App —
  rund 50 KB. Das Handy zeigt Adresse und Code auch als Text, weil am PC keine
  Kamera sitzt

**Zwei Befunde aus dem Bauen**

- **Der QR-Code trug den gewünschten Port statt des tatsächlichen.** Fiel im
  Test auf, weil der auf Port 0 läuft. Am Gerät wäre es genau dann aufgefallen,
  wenn 8443 belegt war — also in dem Fall, in dem ohnehin niemand mehr weiß,
  woran es liegt
- **Rechte, die das Handy nicht kennt, werden weggelassen statt abgelehnt.**
  Die App fragt überall dieselbe Liste an, auch `power` und `wake`. Ein Handy
  kann davon nichts, und mit der strengen Prüfung des Agents ließe es sich
  **nie** koppeln. Der Windows-Agent bleibt streng — dort ist ein unbekanntes
  Recht ein Tippfehler, hier ist es der Normalfall

**Abnahme (10.08.2026):**
Kotlin-Tests **53 grün** (vorher 16) · App-Tests **358 grün** (vorher 354) ·
`npm run apk` baut durch.

Am echten Gerät noch nicht geprüft — das ist der nächste Schritt und braucht
zwei Geräte: freigeben, Code anzeigen, vom PC-Fenster aus koppeln. Bild und
Eingabe gibt es dabei noch nicht; die Kopplung muss stehen, bevor es etwas zu
übertragen gibt.

## Phase 29 — Bild vom Handy

- Abhängigkeit `io.github.webrtc-sdk:android` mit `ScreenCapturerAndroid`:
  MediaProjection → Hardware-H.264 → PeerConnection. Der Weg über eine fertige
  Bibliothek statt eines eigenen `MediaCodec`-Stroms, weil die App-Seite dann
  **unverändert** bleibt — sie schickt dasselbe Angebot wie an den PC
- `POST /api/webrtc/offer` beantwortet das Angebot, `DELETE /api/webrtc/{id}`
  räumt auf. `/monitor` antwortet mit 400: ein Handy hat einen Bildschirm
- Vordergrunddienst vom Typ `mediaProjection` (ab Android 14 Pflicht), eigene
  Benachrichtigung, getrennt vom bestehenden `dataSync`-Dienst der Client-Seite
- Die Freigabeseite sagt klar, was der Systemdialog bedeutet und dass er nach
  einem Neustart des Handys wiederkommt
- Kein JPEG-Rückfall auf dem Handy: ohne Hardware-Encoder gibt es kein Android

**Abnahme:** vom PC-Fenster aus das Handybild sehen, Drehung inbegriffen.

## Phase 30 — Eingabe auf dem Handy

- `RemoteInputService : AccessibilityService`
- `dispatchGesture` für Tippen, langes Drücken, Ziehen und Wischen;
  `performGlobalAction` für Zurück, Home, Übersicht, Benachrichtigungen
- **Der Zeiger ist erfunden.** Android kennt keinen Mauszeiger. Der Host merkt
  sich die zuletzt gemeldete Position aus `move`, und `click` tippt dort. Damit
  passen das Zeiger-Overlay und der direkte Tipp aufs Bild aus der App
  unverändert
- `moverel` vom Touchpad verschiebt dieselbe gemerkte Position
- `scroll` wird zu einer Wischgeste
- **Text**: in den fokussierten Knoten über `ACTION_SET_TEXT`, Rücktaste durch
  Kürzen desselben Textes, `Enter` über die Editor-Aktion. Was nicht geht, geht
  sichtbar nicht: der Host antwortet auf dem Input-Socket mit einer Meldung,
  und die App zeigt sie in der Statuszeile, statt sie zu verschlucken
- Die Bildschirmtastatur zeigt ohne `keys` nur, was ankommt: keine `Fn`-Seite,
  keine Kombi-Sammlung. Statt dessen Zurück, Home und Übersicht
- Die Freigabeseite führt mit einem Knopf direkt in die Bedienungshilfen und
  sagt, was dort einzuschalten ist

**Abnahme:** vom zweiten Handy aus auf dem ersten eine App öffnen und in einem
Textfeld schreiben.

## Phase 31 — Beide Richtungen im Fenster und in der App

- Die Geräteliste zeigt Handys mit eigenem Symbol; alles Weitere folgt schon
  aus Phase 27
- Kopplung eines Handys **am PC**: Adresse und Code werden getippt. Der Weg ist
  derselbe wie beim Handy, nur ohne Kamera — die Seite dafür gibt es bereits
- `desktop/TrustImport.cs` muss die CA eines Handys ebenso annehmen wie die
  eines Rechners; prüfen, dass nichts auf „Rechner" festgenagelt ist
- Ein Gerät darf sich nicht selbst steuern: `lib/selfConnection.ts` bekommt den
  Fall „dieses Handy ist der eigene Host" dazu
- `docs/ARCHITEKTUR.md` und `docs/NETZ.md` nachziehen: das Bild mit den zwei
  Rechnern stimmt dann nicht mehr

**Abnahme:** PC steuert Handy, Handy steuert Handy, Handy steuert PC — alles
aus derselben Oberfläche.

---

# Teil B — Dateimanager

## Phase 32 — Dateidienst im Windows-Agent

Neues Recht `files`. **Achtung:** ein neues Recht bekommt kein bereits
gekoppeltes Gerät rückwirkend — sonst hieße „Recht" nichts. Wer die Dateien
sehen will, koppelt das Gerät einmal neu; die App sagt das im Klartext, statt
einen 403 als Fehler anzuzeigen.

| Endpoint | Zweck |
|---|---|
| `GET /api/files?path=` | Inhalt eines Ordners. Ohne `path`: die Laufwerke |
| `GET /api/files/content?path=` | Die Datei selbst, mit `Range`-Unterstützung |
| `POST /api/files/upload?path=` | Eine Datei ablegen, Strom statt Puffer |

- Einträge tragen Name, Größe, Änderungsdatum, Ordner-ja/nein, versteckt-ja/nein
- `Range` ist nicht Zierde: ohne ihn lässt sich in einem Video nicht springen
- Der Inhaltstyp kommt aus der Endung — geraten wird nichts
- Kein Löschen, kein Umbenennen, kein Verschieben. Nicht gewünscht, und jede
  dieser Aktionen ist eine, die man nicht zurücknehmen kann
- Gelesen und geschrieben wird mit den Rechten des angemeldeten Benutzers.
  Was der nicht darf, geht auch hier nicht — die Meldung sagt das

**Abnahme:** Agent-Tests für Auflisten, Bereichsabfragen und das Ablegen;
Pfade mit Umlauten und Leerzeichen inbegriffen.

## Phase 33 — Die Dateiseite in der App

- `views/FilesView.tsx`: Brotkrumenpfad, Liste mit Symbol/Größe/Datum,
  Sortierung, Filterfeld für den aktuellen Ordner
- **Öffnen heißt ansehen** (Entscheidung vom 09.08.2026): Bild, Video, Audio,
  PDF, Text und Quelltext werden geholt und angezeigt, ohne etwas zu speichern.
  Alles andere bietet nur das Herunterladen an
- Große Dateien werden nicht vorab geladen — das Video-Element holt sich seine
  Bereiche selbst über die Adresse mit Sitzungstoken
- **Herunterladen**: im Fenster und im Browser der gewöhnliche Weg, am Handy
  über einen kleinen Capacitor-Zusatz in den Ordner „Download"
- **Hochladen**: Mehrfachauswahl, eine Datei nach der anderen, mit Fortschritt
  und einer Zeile je Fehlschlag. Kein stilles Überschreiben

**Abnahme:** App-Tests für Pfadlogik, Typ-Erkennung und Fehlerfälle; am echten
Gerät ein Bild ansehen, ein Video spulen, eine Datei in beide Richtungen
schieben.

## Phase 34 — Dateien auf dem Handy

- Dieselben drei Endpunkte im Kotlin-Host
- Zugriff auf den gemeinsamen Speicher braucht „Zugriff auf alle Dateien"
  (`MANAGE_EXTERNAL_STORAGE`). Die Freigabeseite fragt danach, wenn der
  Dateidienst eingeschaltet wird — und läuft ohne ihn mit dem, was ohne die
  Erlaubnis zu sehen ist
- Hochgeladenes landet in „Download", nicht irgendwo

**Abnahme:** vom PC aus ein Foto vom Handy holen und eine Datei hinlegen.

## Phase 35 — Abnahme, Doku, Release

- `docs/ARCHITEKTUR.md`, `docs/NETZ.md`, `docs/SICHERHEIT.md`, `README.md`
- Alle vier Testläufe grün, APK gebaut, Fassungsnummern gezogen
- Durchgang am echten Gerät über alle drei Richtungen

---

## Offene Risiken

- **APK-Größe.** Die WebRTC-Bibliothek bringt rund 10 MB mit. Vertretbar, aber
  es fällt beim Selbst-Update auf
- **Akku.** Ein Handy, das seinen Bildschirm überträgt, ist ein Handy unter
  Volllast. Der Dienst soll deshalb von allein enden, wenn eine Weile niemand
  zusieht
- **Adresse im Heimnetz.** Ein Handy bekommt seine IP per DHCP und wechselt sie.
  Für den Heimnetz-Modus heißt das: feste Adresse im Router eintragen — oder
  Tailscale nehmen, wo der Name bleibt. Gehört in `docs/NETZ.md`
- **Die Bedienungshilfe ist ein sehr großes Recht.** Sie darf mitlesen, was auf
  dem Bildschirm steht. Das ist hier gewollt und trotzdem der Punkt, an dem
  dieses Projekt am meisten Vertrauen verlangt. Es gehört in
  `docs/SICHERHEIT.md`, nicht in eine Fußnote
