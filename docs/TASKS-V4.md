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

## Phase 29 — Bild vom Handy ✅

- `WebSocketFrames.kt` · `WebSocketConnection.kt` — das Rahmenformat von
  RFC 6455 und die stehende Verbindung. Zwei Bildschirmseiten Festlegung, und
  die drei Stellen, an denen eine eigene Fassung erfahrungsgemäß scheitert
  (erweiterte Länge, Maske, Fortsetzungsrahmen), stehen je einmal unter Test
- `ScreenCapture.kt` — MediaProjection → virtueller Bildschirm → `ImageReader`
  → Bitmap → JPEG. Längste Kante 1280: die vollen 2400 Pixel eines Handys zu
  übertragen kostet mehr, als es zeigt
- `ScreenStream.kt` — `/ws/screen` mit demselben Achtbyte-Kopf, denselben
  Textnachrichten und derselben Qualitätsregelung wie beim Agent
- Vordergrunddienst meldet sich nach der Bestätigung mit dem Typ
  `mediaProjection` neu an — vorher, und Android zieht seit Fassung 14 die
  Erlaubnis wortlos zurück

**Die Entscheidung, die im Plan noch anders stand: JPEG statt WebRTC.**

Der Plan sah `io.github.webrtc-sdk:android` vor. Dagegen spricht dasselbe wie
gegen Ktor und BouncyCastle in Phase 28 — rund zehn Megabyte, bei jedem
Selbst-Update aufs Neue —, und dafür spricht weniger als gedacht: die App kann
den JPEG-Weg längst, es ist Stufe 1 aus `docs/ARCHITEKTUR.md`. Kein neuer
Kanal, keine neue Aushandlung, keine Abhängigkeit. Reicht es am echten Gerät
nicht, ist H.264 der nächste Schritt — dann aber mit einem Grund statt auf
Verdacht.

Ruft die App `/api/webrtc/offer`, antwortet das Handy mit 404, und sie fällt von
allein auf den JPEG-Stream zurück. Genau dafür ist der Rückfall gebaut.

**Abnahme (10.08.2026):** Kotlin-Tests **70 grün** (vorher 53).

## Phase 30 — Eingabe auf dem Handy ✅

- `InputCommands.kt` — dieselben Nachrichten wie an den Windows-Agent, gelesen
  ohne Android-Bezug und deshalb unter Test
- `RemoteInputService.kt` — die Bedienungshilfe. `dispatchGesture` für Tippen,
  langes Drücken, Ziehen und Wischen; `performGlobalAction` für Zurück, Home
  und Übersicht; Text über den Knoten mit dem Eingabefokus
- `/ws/input` im Server, getrennt vom Bild wie beim Agent

**Der Zeiger ist erfunden.** Android kennt keinen Mauszeiger, die App schickt
aber Bewegung und Klick getrennt — weil sie mit einem PC redet. Der Host führt
deshalb eine Position und tippt beim Klick dorthin. Damit funktionieren das
Zeiger-Overlay und der direkte Tipp aufs Bild unverändert. Ein Rechtsklick wird
zum langen Drücken; die mittlere Maustaste gibt es nicht und sagt das auch.

**Was nicht geht, sagt es.** Jede abgelehnte Eingabe schickt einen Satz über den
Eingabe-Socket, den die App in ihrer Statuszeile zeigt — je Verbindung einmal,
sonst stünde bei jedem Antippen dieselbe Zeile. Der wichtigste Fall ist die
ausgeschaltete Bedienungshilfe: ein Gerät, das Berührungen wortlos verschluckt,
sieht aus der Ferne aus wie ein hängendes.

**Eingeschaltet wird von Hand.** Android verlangt den Gang in die
Systemeinstellungen, und die App kann das weder abkürzen noch heimlich tun. Die
Freigabeseite führt hin und sagt, was dort zu suchen ist — und dass der Dienst
überall hintippen darf.

**Abnahme (10.08.2026):** Kotlin-Tests **83 grün** (vorher 70) · App-Tests
**358 grün** · `npm run apk` baut durch.

**Am echten Gerät noch nicht geprüft.** Das ist jetzt dran und braucht zwei
Geräte:

1. Am Handy: Einstellungen → „Dieses Gerät freigeben" → einschalten,
   Bildschirm freigeben (Systemdialog bestätigen), Bedienungshilfen öffnen und
   „RemoteDesktop-Fernsteuerung" einschalten
2. Am PC-Fenster oder am zweiten Handy: koppeln — QR scannen oder Adresse,
   Port und Code eintippen
3. Erwartet: Bild des Handys, Tippen aufs Bild löst dort einen Tipp aus, langes
   Drücken hält, die Bildschirmtastatur schreibt in ein geöffnetes Textfeld.
   Power, Medien, Aktionen und Shortcuts sind **nicht** in der Leiste

## Phase 31a — der Befund: die Kopplung kam nie bis zum Zertifikat ✅

**Am echten Gerät ließ sich ein Handy von keinem PC aus koppeln.** Unter jeder
angezeigten Adresse stand „<IP> antwortet nicht", während der Host lief und
antwortete.

**Die Ursache lag nicht bei V4, sondern seit V3 im Weg dorthin.** Die App holt
die eigene Zertifizierungsstelle der Gegenstelle unter
`http://<adresse>:8442/ca.crt` — unverschlüsselt, und das muss so sein: die
verschlüsselte Verbindung ist ja gerade die, die ohne dieses Zertifikat nicht
zustande kommt. Nur läuft die App selbst unter `https`: unter Android auf
`https://localhost` (Capacitor), im Fenster auf einem virtuellen Host. Chromium
verwirft eine `http`-Anfrage von einer `https`-Seite als **aktiven Mixed
Content**, bevor irgendetwas über das Netz geht. Die Ausnahme, die dabei
herauskommt, ist von „Rechner nicht erreichbar" nicht zu unterscheiden — und
genau so wurde sie angezeigt.

Das erklärt rückwirkend auch, warum „Zertifikat bestätigen" am Handy nie etwas
tat: es gab nie eine Datei zu bestätigen.

**Der Abruf gehört nach nativ.** Dort gibt es diese Sperre nicht.

- Android: `CertificateTrustPlugin.fetch` holt sie mit `HttpURLConnection`
- Windows: eine Brücke über `chrome.webview.postMessage` mit Kennung und
  Antwort; geholt wird mit dem vorhandenen `TrustImport.FetchAsync`
- Die Seite fällt auf ihren eigenen Abruf zurück, wo es keine Brücke gibt — im
  gewöhnlichen Browser funktioniert er

**Das Fenster konnte überhaupt nichts bestätigen.** `webview2.ts` meldete
`trust: noTrust`; `TrustImport` gab es zwar, aber die App kam nicht daran.
Jetzt gibt es `desktop/TrustedAuthorities.cs`: eine Liste bestätigter Stellen
in `{app}\data\trusted.json`, durchgesetzt über
`ServerCertificateErrorDetected`. **Nicht** der Windows-Zertifikatspeicher —
was hier bestätigt wird, gilt für dieses Fenster und für nichts sonst. Ein
Handy, das im Heimnetz seinen Bildschirm freigibt, soll nicht nebenbei zur
Stelle werden, der jeder Browser auf diesem Rechner glaubt.

**Ohne QR-Code fehlte der Vergleichswert.** Am PC sitzt keine Kamera, also
werden Adresse und Code abgetippt — und der Fingerabdruck kam nicht mit. Die
Kopplung holt die Stelle jetzt und **zeigt** sie: das Handy zeigt denselben
Wert unter „Dieses Gerät freigeben", und beide werden nebeneinandergelegt.
Derselbe Anker wie beim Scannen, nur mit dem Auge statt der Kamera.

**Abnahme (10.08.2026):** App-Tests **361 grün** (vorher 358) ·
`cd desktop && dotnet build` · `npm run apk` baut durch. Am echten Gerät noch
nicht geprüft.

## Phase 31b — Kopplung in beide Richtungen ✅

Wunsch vom 10.08.2026: wer ein Gerät koppelt, soll damit zugleich die
Gegenrichtung eingerichtet haben.

**Das ist mehr als Bequemlichkeit.** Der Fingerabdruck der Gegenstelle wandert
damit über eine Verbindung, die gerade beglaubigt wurde — statt am zweiten
Gerät von einem Menschen abgelesen zu werden. Der Weg über das Auge aus Phase
31a bleibt, ist aber nur noch der Notweg.

**Wie es läuft.** Wer koppelt, legt sein eigenes Angebot bei: Adresse, Port, ein
frischer Kopplungscode **seines** Agents und sein Fingerabdruck. Die Gegenseite
hebt es auf — erst **nach** bestandener Kopplung, sonst wäre es ein Weg, jedem
Gerät ein Angebot unterzuschieben, indem man Codes rät.

Einlösen kann es der Agent nicht: koppeln heißt, einen privaten
Geräteschlüssel zu benutzen und ein Gerät in eine Liste einzutragen, und beides
liegt in der Oberfläche. Sie sieht alle fünf Sekunden nach — wer am anderen
Gerät koppelt, hat dieses hier meist gerade in der Hand.

- `agent/Auth/PendingPairing.cs` · `host/PendingPairings.kt` — dasselbe auf
  beiden Seiten: eines, kurz gültig, einmal abzuholen
- `GET /api/pair/pending` ist **nur am Rechner selbst** erreichbar. Es liegt
  unterhalb von `/api/pair`, und das ist absichtlich ohne Ausweis erreichbar —
  ohne eigenen Eintrag in `LocalOnly` käme jeder im Netz an einen gültigen
  Kopplungscode der Gegenseite. Ein Test hält das fest
- `platform/localNode.ts` — das eigene Gerät als Gegenstelle. Am Handy über das
  Plugin, im Fenster über die Brücke: der eigene Agent weist sich selbst
  ausgestellt aus, und die Seite müsste ihm erst vertrauen, um ihn nach dem
  Vertrauen fragen zu können
- `lib/backPairing.ts` — holt das Angebot, vertraut, koppelt, trägt ein. Ohne
  Rückfrage: dieselbe Entscheidung ein zweites Mal zu verlangen wäre keine
  Sicherheit, sondern eine Zumutung

**Damit gilt für alle drei Wege dasselbe.** Handy scannt den QR-Code am PC ·
zwei Handys scannen sich · zwei PCs tippen Adresse und Code — danach ist jedes
Paar in beide Richtungen gekoppelt.

**Abnahme (10.08.2026):** App-Tests **368 grün** (vorher 361) · Agent-Tests
**364 grün** (vorher 353) · Kotlin-Tests **89 grün** (vorher 83) · desktop baut
· `npm run apk` baut. Am echten Gerät noch nicht geprüft.

## Phase 31c — die Gegenkopplung kam im Fenster nie an ✅

**Am echten Gerät:** das Handy stand in der Geräteliste des PCs, aber die
Fernsteuerung meldete „Noch kein Gerät gekoppelt". Die Kopplung hatte also in
eine Richtung geklappt und in die andere nicht.

**Die Ursache ist der Lebenszyklus, nicht das Protokoll.** Die WebView im
Fenster entsteht erst beim ersten Öffnen der Fernsteuerung
(`RemotePage.ShowRemoteAsync`). Solange sie nicht läuft, holt niemand das
Angebot zur Gegenkopplung ab — und der Kopplungscode darin ist nach fünf
Minuten wertlos. Wer am Handy koppelte und den Tab später öffnete, fand eine
leere Liste, obwohl alles richtig gelaufen war.

Die Oberfläche wird deshalb beim Öffnen des Fensters im Hintergrund
hochgefahren. Nebenbei ist der erste Wechsel auf die Seite jetzt sofort da.

**Und der Fehlschlag war stumm.** Er wurde abgefangen, weil das Angebot eine
Zugabe ist — und genau das war falsch: eine Gegenkopplung, die still scheitert,
sieht aus wie eine, die nie angeboten wurde. Sie meldet sich jetzt, aber nur
einmal je Fehlerbild.

## Phase 31d — Einrichtung ohne gekoppeltes Gerät ✅

Solange nichts gekoppelt war, gab es genau eine Seite: „Gerät koppeln". Die
Einstellungen — und damit die Freigabe dieses Geräts — waren erst erreichbar,
wenn schon etwas gekoppelt war. Eine Einrichtung, die eine fertige Einrichtung
voraussetzt.

Jetzt führt von dort ein Weg in die Einstellungen und weiter auf „Dieses Gerät
freigeben". Der Startbildschirm sagt außerdem, dass beides unabhängig
voneinander ist: man kann steuern, ohne steuerbar zu sein, und umgekehrt.

## Phase 31e — die Kopplung geht immer in beide Richtungen ✅

**Befund vom 11.08.2026 am echten Gerät:** das Handy wurde am PC weiterhin nicht
als steuerbar geführt. Die Ursache war nicht ein Fehler, sondern der Entwurf aus
31b — er war an drei Stellen falsch, und alle drei hingen zusammen.

### Was falsch war

1. **Die Gegenkopplung hing daran, dass der Host im Augenblick des Koppelns
   lief.** Wer beim Koppeln die Freigabe nicht eingeschaltet hatte, bekam die
   Gegenrichtung nie — auch später nicht, ohne neu zu koppeln.
2. **Die Gegenrichtung brauchte einen Netzaufruf mit Kopplungscode.** Daher die
   fünf Minuten, daher die Abhängigkeit vom offenen Fenster, daher der ganze
   `pending`-Mechanismus.
3. **Der Host war auf Dauerbetrieb ausgelegt.** Er sollte es nicht sein.

### Der Steckbrief ersetzt den weitergereichten Code

Beide Seiten tauschen beim Koppeln alles aus, was sie voneinander brauchen — in
**einem** Aufruf:

- Die Anfrage an `/api/pair` trägt den **Steckbrief** des Anrufers:
  `agent/Auth/DeviceProfile.cs` · `host/DeviceProfile.kt` ·
  `platform/localNode.ts`, dreimal dieselben Regeln
- Die Antwort trägt den **Ausweis der Gegenseite** — den öffentlichen
  Client-Schlüssel ihrer Oberfläche. Das ist die ganze Gegenrichtung, in einem
  Feld
- Danach trägt **jede Seite ohne Netzverkehr** ein: der Schlüssel der anderen in
  die eigene `clients.json` (`PairingService.Grant`, über die Brücke
  `local-grant`), ihr Steckbrief in die eigene Geräteliste

Der entscheidende Unterschied ist die **Frist**. Ein Kopplungscode verfällt; ein
Steckbrief ist eine Beschreibung und kein Geheimnis. Er darf auf Platte liegen
(`peers.json`), einen Neustart überleben und wirken, sobald der Server startet.
Damit entfallen `PendingPairings`, `/api/pair/pending`, `LocalNode.OfferAsync`
und der Fünf-Sekunden-Takt in `App.tsx` **ersatzlos**.

### Zwei Fehler daran, gefunden am echten Gerät (15.08.2026)

Am PC blieb das Handy im Fernsteuerungs-Tab aus. Die Kopplung selbst lief —
im Geräte-Tab stand es, und der PC war vom Handy aus steuerbar. Nur der
Steckbrief kam nie in der Liste an. Zwei Ursachen, beide in dieser Phase
entstanden:

1. **Es wurde nur beim Start nachgesehen.** Ich hatte den Fünf-Sekunden-Takt
   ersatzlos gestrichen, weil ein Steckbrief nicht verfällt — und dabei
   übersehen, *wann* jemand hinsieht. Der Normalfall ist, dass das Fenster
   **offen ist, während drüben gekoppelt wird**: man scannt am Handy den
   QR-Code, den dieses Fenster gerade anzeigt. Danach passierte hier nichts
   mehr. Der zweite Abholpunkt half nicht: solange nichts gekoppelt ist, zeigt
   die App die Startkarte und rendert die Geräteliste gar nicht.
   → Ein **ruhiger Takt** (10 s) ist zurück. Nicht der alte: der jagte einem
   Code hinterher, der nach fünf Minuten verfiel. Dieser sieht nur nach, weil
   jemand hinsehen muss, damit etwas erscheint — und der Aufruf geht an den
   eigenen Rechner.
2. **Lesen leerte den Eingang.** Ging danach irgendetwas schief — kein
   Vertrauen, kein Speicher, ein Fenster, das gerade schließt —, war der
   Steckbrief **endgültig** weg, ohne zweiten Versuch. Genau der Fehler, den
   ein Posteingang nicht machen darf.
   → **Erst eintragen, dann vergessen:** `GET /api/pair/peers` liest ohne
   Nebenwirkung, `POST /api/pair/peers/forget` räumt weg, was in der Liste
   steht. Sonst käme ein entferntes Gerät von allein zurück.

**Drei Dinge, die beim Bauen dazukamen**

- **Die eigene Kennung wird ausgerechnet, nicht erfragt.** Bei der Gegenrichtung
  findet kein Kopplungsaufruf statt, aus dem `clientId` zurückkäme — also bildet
  `lib/clientKey.ts` sie aus dem eigenen Schlüssel, genau wie es beide
  Gegenstellen tun. Ohne sie wirft `parseDevices` den Eintrag beim nächsten
  Lesen weg
- **Das Fenster hinterlegt seinen Ausweis beim eigenen Agent** (`LocalClient`,
  `POST /api/pair/local`, beim Start). Der Agent hat ihn nicht selbst — er
  gehört der Oberfläche. Ohne ihn bliebe jede Kopplung einseitig.
  → *Ersetzt in 31h:* der Ausweis liegt jetzt in einer Datei, die beide lesen,
  und wird nirgends mehr hinterlegt
- **Alles unter `/api/pair/…` steht einzeln in `LocalOnly`.** `/api/pair` ist
  absichtlich ohne Ausweis erreichbar, und die Prüfung darunter vergleicht auf
  Segmentgrenzen — ohne die Einträge käme jeder im Netz an die Steckbriefe oder
  ließe sich selbst eintragen. Ein Test hält jeden einzelnen fest

### Der Host lebt mit der App

Kein Schalter „steuerbar machen" mehr, sondern die Einstellung **„Dieses Gerät
darf ferngesteuert werden"** (Vorgabe: aus). Sie liegt nativ
(`host/HostPreference.kt`) und nicht im localStorage: sie entscheidet über den
Lebenslauf des Servers, und der beginnt und endet mit der Activity — an einer
Stelle also, an der noch keine Weboberfläche läuft.

`MainActivity` startet den Host beim Öffnen und beendet ihn in `onDestroy`. Der
Vordergrunddienst bleibt — eine laufende Sitzung soll überstehen, dass der
Bildschirm ausgeht —, aber **ohne `START_STICKY`**: ein Server, der sich hinter
dem Rücken seines Besitzers wieder anschaltet, ist genau das, was ein Handy
nicht tun soll.

### Jede Verbindung wird bestätigt

`/api/session` wartet auf ein Ja am Gerät (`host/ConnectionRequests.kt`, etwa
30 Sekunden). **Eine Kopplung sagt, *wer* fragen darf — dass jetzt gerade jemand
zusehen darf, sagt nur ein Mensch.**

- **Ablehnung ist die Vorgabe** — bei „Nein", bei Zeitablauf und auch dann, wenn
  gar keine Oberfläche da ist, die fragen könnte
- **Gefragt wird erst nach der Prüfung.** Wer nicht gekoppelt ist, soll am Handy
  keine Karte auslösen können
- Der Token wird ausgestellt, bevor gefragt wird, und bei einem Nein sofort
  geschlossen (`SessionStore.close`) — er darf keinen Augenblick länger gelten
- Die Karte (`views/ConnectionRequestView.tsx`) liegt **über allem** und lässt
  sich nicht wegtippen. Sie in eine Seite zu legen, die man erst aufsuchen muss,
  hieße, sie in den meisten Fällen ablaufen zu lassen; ein Wegwischen sähe aus
  wie eine Zustimmung, die niemand gegeben hat

Nebenbei rückt damit der Systemdialog der Bildschirmaufnahme an die richtige
Stelle: er kommt beim ersten Verbinden und nicht Tage vorher.

**Abnahme (14.08.2026):**
Agent-Tests **378 grün** (vorher 364) · App-Tests **369** (vorher 368) ·
Kotlin-Tests **95** (vorher 89) · `cd desktop && dotnet build` ·
`npm run apk` baut durch.

**Am echten Gerät noch nicht geprüft.** Das ist jetzt dran:

1. Am Handy koppeln — die Freigabe dabei **aus** lassen
2. Danach in den Einstellungen „Dieses Gerät darf ferngesteuert werden"
   einschalten
3. Am PC muss das Handy jetzt in der Geräteliste stehen und sich verbinden
   lassen; am Handy erscheint dabei die Karte „Verbindung zulassen?"
4. App am Handy wegwischen → der PC verliert die Verbindung, die
   Benachrichtigung verschwindet

## Phase 31f — nur noch die brauchbare Adresse ✅

Vorher zählte `HttpServer.localAddresses()` alle Adressen aller Schnittstellen
auf, die „oben" sind. Auf einem Handy sind das drei bis fünf — WLAN, Mobilfunk,
dazu Tunnel und Attrappen, die Android für sich selbst führt. In den
Einstellungen standen sie nebeneinander, und **keine** funktionierte zum
Abtippen: die richtige ging in den anderen unter.

`host/HostAddresses.kt` fragt jetzt das System, welches Netz das aktive ist
(`ConnectivityManager.getLinkProperties`), und dessen Adresse zählt. Der Gang
über die Schnittstellen bleibt als Rückfall, nun ohne `dummy*`, `rmnet_ims*`,
`p2p*` und ohne die selbstvergebenen `169.254.*`. Die Freigabeseite zeigt eine
Adresse groß und weitere nur, wenn es sie wirklich gibt.

Ob das reicht, zeigt erst das Gerät: wenn auch die richtige Adresse nicht
antwortet, liegt es nicht an der Auswahl, sondern daran, dass der Server nicht
lauscht oder das WLAN Geräte voneinander trennt (AP-Isolation).

## Phase 31h — die Kopplung, und warum sie so lange nicht ging (16.08.2026)

Die Kopplung funktioniert seit dem 16.08. **in beide Richtungen**, am echten
Gerät geprüft. Der Weg dorthin ist der Teil, der hier festgehalten gehört: an
derselben Meldung wurde über mehrere Sitzungen gearbeitet, und die meisten
Befunde waren echte Fehler, ohne das Symptom zu erklären.

### Was tatsächlich die Ursache war

**Alte Daten und alter Code überlebten jede Neuinstallation.** Zwei Speicher
liegen außerhalb dessen, was eine Deinstallation anfasst:

- Im Fenster kommt die Oberfläche über einen virtuellen Host und damit über
  `https`. Für WebView2 ist das eine Website wie jede andere — sie wird
  zwischengespeichert, und der Ordner liegt unter
  `%LOCALAPPDATA%\RemoteDesktop\WebView2`, nicht neben der `.exe`. Wer neu
  installierte, bekam die alte App zu sehen. Im selben Ordner liegt der
  localStorage: Geräteliste und Client-Schlüssel — daher die Kopplungen, die
  nach dem Löschen des Ordners wiederkamen.
- Am Handy stand `allowBackup="true"`. Android stellt damit `filesDir` und die
  Preferences aus dem Cloud-Backup wieder her: `clients.json`, `peers.json`,
  den privaten Host-Schlüssel und die eigene CA. Für eine App mit
  Geräteidentitäten ist das nicht nur lästig, sondern falsch.

**Die Lehre:** ein Fehlerbild, das sich nicht ändert, obwohl man es geändert
hat, ist zuerst ein Verdacht auf alten Code — nicht auf die Logik. Wer hier
sucht, prüft als Erstes, ob eine geänderte Meldung wirklich ankommt.

### Die Fehler, die nebenbei gefunden wurden

Alle echt, alle behoben, keiner davon allein die Ursache:

| Befund | Wirkung |
|---|---|
| `ACCESS_NETWORK_STATE` fehlte im Manifest | `ConnectivityManager` gab nichts heraus; die Adressliste fiel still auf die rohe Interface-Aufzählung zurück, und vorn stand eine `rmnet`-Adresse |
| Zertifikat und Server entstanden einmal je Prozess | nach einem Netzwechsel zeigte die App eine Adresse, die das eigene Zertifikat nicht abdeckte → TLS-Fehler, der wie „nicht erreichbar" aussieht |
| `grantPeer` hing an `profile()` | ohne Netzadresse wurde der Schlüssel der Gegenseite nicht eingetragen |
| Ausweis ging einmal beim Start an den Agent | wer den ersten Versuch verlor, verlor für die ganze Sitzung |
| `discover` verschluckte den Grund | „antwortet nicht" bei einer Gegenstelle, die nachweislich antwortet |
| Sperre gegen Selbstverbindung verglich **Namen** | ein Handy, das „David" heißt, galt als der Rechner „David" |

### Wo der Ausweis herkommt (erledigt)

Der Client-Schlüssel des Fensters lag im localStorage der WebView, und der Agent
kannte ihn nur, weil die React-App ihn über `/api/pair/local` hinterlegte
(`announceSelf`). Das hatte zwei Folgen, die beide unerwünscht waren:

- **Der Ausweis hing am Lebenslauf einer Weboberfläche.** `RemotePage` baut die
  WebView erst beim ersten Anzeigen auf. Wer das Fenster öffnete, auf „Geräte"
  ging und dort einen QR-Code erzeugte, hatte nie eine laufende React-App — der
  Ausweis blieb unhinterlegt, und die Gegenseite bekam beim Koppeln ein leeres
  `clientKey`. Symptom: „die Gegenseite hat ihren Ausweis nicht mitgeschickt".
- **Koppeln setzte einen laufenden Agent voraus.** Gewünscht ist: eingerichtet
  genügt. Wer diesen Rechner nur zum Steuern anderer benutzt, soll den Agent
  nicht starten müssen, um ein Gerät zu koppeln.

**Beides löst derselbe Umbau, und der steht:**

1. Das Schlüsselpaar des Rechners liegt in `{app}\data\clientkey.json`
   (`setup/ClientKeyFile.cs`) — angelegt von wem auch immer zuerst kommt. Agent
   **und** Fenster lesen dieselbe Datei; `register`, `announceSelf` und
   `POST /api/pair/local` sind ersatzlos weg. Am Handy dasselbe: der Ausweis
   liegt in `clientkey.txt` bei den übrigen Schlüsseln des Hosts
   (`host/LocalClientKey.kt`), die App holt ihn über `localClientKey()` ab,
   statt einen eigenen zu erzeugen.
2. `desktop/LocalNode.cs` fragt bei laufendem Agent weiter über HTTP (er hält
   `clients.json` im Speicher, eine Datei unter ihm zu ändern ginge verloren)
   und geht bei gestopptem Agent auf die Dateien im Datenordner
   (`desktop/AgentData.cs`, `setup/ClientsFile.cs`). Beim nächsten Start liest
   er sie ohnehin neu.

**Der Preis dafür steht im Installer:** `{app}\data` bekommt `users-modify`.
Der Agent läuft erhöht und kommt ohnehin hinein, das Fenster nicht — und ohne
Schreibrecht bliebe für „koppeln bei gestopptem Agent" nur eine Rückfrage von
Windows bei jeder einzelnen Kopplung. Was es kostet: ein zweiter,
nicht-administrativer Benutzer dieses Rechners könnte sich selbst in die
`clients.json` eintragen. Lesen durfte er den Ordner ohnehin (er erbt die Rechte
von „Programme"), und der Agent läuft in der Sitzung genau des Benutzers, der
ihn eingerichtet hat.

**Zwei Befunde nebenbei**, beide aus derselben Ecke:

- `platform/index.ts` und `platform/web.ts` importierten sich gegenseitig. In
  einem solchen Kreis entsteht das zuerst Gefragte zuletzt: die
  Vorgabe-Plattform des Browsers hatte ein leeres `node` und ein leeres
  `trust`. Aufgefallen ist es erst, als jemand `platform.node` im Browser las.
  Jetzt holen sich die drei Umsetzungen ihre Werte aus den definierenden
  Modulen, und `noTrust` hat eine eigene Datei.
- Die `clients.json` hat jetzt **eine** Fassung (`setup/ClientsFile.cs`), die
  der Agent und das Fenster benutzen. Zwei Fassungen desselben Formats wären
  ein Fehler, der erst am echten Gerät auffiele.

### Namen kommen von dem, der koppelt

Beim Koppeln werden zwei Namen vergeben — wie dieses Gerät drüben heißen soll
und wie die Gegenseite hier heißt. Der erste ging bis zum 16.08. nur in die
`clients.json` der Gegenseite; in ihre **Geräteliste** kam der Selbstname aus
dem Steckbrief. Wer am Handy „Handy" eintippte, fand am PC „David" wieder — den
Android-Gerätenamen. Behoben: der Steckbrief trägt den eingetippten Namen.

Der Name gilt ab der Kopplung und ändert sich nicht mehr
mit. Wer ein Gerät später umbenennt, benennt es nur bei sich um. Das ist so gewollt.

## Phase 31g — ein Geräte-Tab statt dreier ✅ (16.08.2026, am Gerät noch zu prüfen)

Wunsch vom 15.08.2026. Die Windows-Oberfläche hat heute fünf Einträge in der
Leiste, und drei davon reden über dasselbe: „Fernsteuerung" zeigt die
Geräteliste der App, „Geräte" die `clients.json` des Agents, „Netz" eine
Einstellung. Wer ein Handy koppelt, sieht es an einer Stelle und steuert es an
einer anderen — und muss selbst wissen, dass das zwei Listen desselben Paares
sind.

### Die Leiste

| vorher | nachher |
|---|---|
| Übersicht · Fernsteuerung · Geräte · Netz · Einstellungen | Übersicht · **Geräte** · Einstellungen |

- **Fernsteuerung + Geräte werden eins.** Der neue Tab ist die WebView
  (`RemotePage`); die native `DevicesPage` entfällt. Sie kann nichts, was die
  App nicht auch kann — nur eben in einer zweiten Liste.
- **Netz wandert unter Einstellungen.** Ein Netzmodus wird einmal eingestellt
  und danach nie wieder angefasst; er gehört nicht neben etwas, das man täglich
  benutzt.

**Was dafür über die Brücke muss.** Die beiden Aufgaben der `DevicesPage`
hängen an Endpunkten, die absichtlich nur am Rechner selbst erreichbar sind:

- `local-code` — Kopplungscode und QR-Inhalt (`POST /api/pair/code`)
- `local-clients` — wer diesen Rechner steuern darf, samt Widerruf
  (`GET`/`DELETE /api/clients`)

Damit ist `platform.host` im Fenster nicht mehr `noHost`, sondern eine Umsetzung
über die Brücke. Das ist die richtige Auflösung: „dieses Gerät freigeben" heißt
am PC „der Agent läuft", und das ist eine Auskunft und kein Schalter.

### Der Kopplungscode

Wie bisher — mit vier Wegen, auf denen er wieder verschwindet: **Ablauf** (der
Countdown steht dabei), **Benutzung**, **Tabwechsel** und **ein Knopf daneben**.
Ein Code, der noch dasteht, wenn er nicht mehr gilt, wird abgetippt und
scheitert ohne erkennbaren Grund; einer, der nach dem Einlösen stehen bleibt,
sieht aus, als könne man ihn noch einmal benutzen.

### Jedes Gerät zeigt drei Angaben

| Angabe | Woher |
|---|---|
| Name | wie bisher, mit eigenem Namen überschreibbar |
| zuletzt verbunden | **neu**, lokal gemerkt beim Verbinden |
| Plattform | **neu im Protokoll**: `platform: "windows" \| "android"` in `/api/info` und im Steckbrief |

`platform` gehört in den Steckbrief und nicht nur in `/api/info`: die Liste soll
das Symbol auch dann zeigen, wenn das Gerät gerade aus ist. Ein Agent ohne das
Feld ist älter als 31g — dann steht dort nichts, statt „Windows" zu raten.

### Und drei Knöpfe

1. **Entfernen** — hebt die Kopplung **bei beiden** auf, mit Rückfrage, die das
   ausspricht. Danach soll nichts mehr davon übrig sein, als hätten sich die
   Geräte nie gekannt:
   - hier: Gerät aus der Liste, Partner aus der eigenen `clients.json`, seine
     Stelle aus `trusted.json`
   - dort: dieses Gerät aus seiner `clients.json` **und** aus seiner Geräteliste
   - **Was dafür fehlt:** ein Weg, sich beim Partner selbst auszutragen —
     `DELETE /api/pair/self` mit dem eigenen Sitzungstoken. `/api/clients/{id}`
     geht nicht: der ist nur am Rechner selbst erreichbar, und das soll er
     bleiben. Und die Kennung des Partners in der **eigenen** `clients.json`
     muss beim Koppeln mitgeschrieben werden (Fingerabdruck über
     `peer.clientKey`), sonst weiß später niemand, welcher Eintrag zu welchem
     Gerät gehört
   - Ist die Gegenseite nicht erreichbar, wird **hier trotzdem** entfernt und
     gesagt, dass drüben ein Rest bleibt. Ein Entfernen, das an einem
     ausgeschalteten Handy scheitert, wäre keins
2. **Verbindung testen** — in beide Richtungen, und die Antwort sagt beides:
   - hin: `/api/info` und eine Anmeldung; daraus die Rechte, die dieses Gerät
     dort hat
   - her: steht der Partner in der eigenen `clients.json`, und mit welchen
     Rechten
   - Das ist die Auskunft, die heute fehlt: „antwortet nicht" verschweigt, ob es
     am Netz, am Vertrauen oder an einer fehlenden Freigabe liegt
3. **Verbinden** — ausgegraut, solange es nichts zu verbinden gibt: Gerät nicht
   erreichbar, oder erreichbar ohne `screen`/`input` (am Handy die
   Bedienungshilfe, am PC ein Agent, der nicht läuft). Ein Knopf, der nur eine
   Fehlermeldung erzeugt, ist schlimmer als keiner

**Abnahme:** alle vier Testläufe, APK und desktop bauen — und am echten Gerät:
koppeln, testen, verbinden, entfernen, und danach steht auf beiden Seiten
nichts mehr voneinander.

### Wie es gebaut wurde

- **Die Leiste** hat drei Einträge (`desktop/Ui/NavigationRail.cs`).
  `DevicesPage.cs` ist gelöscht; „Netz" führt über eine Karte in
  `SettingsPage`. Im Infobereich führen „Fernsteuerung" und „Geräte koppeln"
  auf denselben Eintrag, weil es dieselbe Seite ist.
- **`platform.host` im Fenster** (`webview2.ts`, `windowHost`) läuft über die
  Brücke: `local-status`, `local-code`, `local-clients`, `local-revoke`. Neu in
  der Schnittstelle ist `toggleable`: am Handy lebt der Server mit der App und
  die Freigabe ist ein Schalter, am Rechner heißt „freigegeben" schlicht „der
  Agent läuft" — eine Auskunft. Bild und Bedienungshilfe verwaltet ohnehin nur
  Android; diese Abschnitte sind im Fenster nicht da statt leer.
- **Der Kopplungscode** verschwindet auf vier Wegen: Ablauf (mit Countdown),
  Benutzung (die Liste der zugelassenen Geräte ist um einen Eintrag gewachsen),
  Verlassen der Seite und ein Knopf daneben.
- **`platform`** steht in `/api/info` **und** im Steckbrief, `lastConnectedAt`
  ausschließlich lokal (`rememberContact`, höchstens einmal je Minute
  geschrieben). Beides kommt aus derselben Antwort: wer `/api/info`
  beantwortet, war erreichbar und sagt dabei, was er ist.
- **Entfernen bei beiden** über `DELETE /api/unpair` — mit dem eigenen
  Sitzungstoken, und nur für sich selbst. Der Pfad liegt ausdrücklich *nicht*
  unter `/api/pair/…`: alles darunter ist ohne Ausweis erreichbar, weil der
  Kopplungsaufruf die Berechtigung erst erzeugt, und ein Widerruf ohne Ausweis
  wäre ein Weg, die Kopplung eines fremden Geräts über das Netz zu beenden. Ein
  Test hält das fest. Auf der eigenen Seite räumt `lib/removeDevice.ts` die
  drei übrigen Stellen ab: Geräteliste, eigene `clients.json` (über
  `peerClientId`, beim Koppeln aus dem Ausweis der Gegenseite ausgerechnet) und
  die bestätigten Zertifizierungsstellen.
- **Der Verbindungstest** (`lib/connectionTest.ts`) meldet sich für die
  Hinrichtung ausdrücklich selbst an, am Transport vorbei: der merkt sich ein
  Token und wirft weg, was bei der Anmeldung sonst gesagt wurde — unter anderem
  die Rechte, und genau die sind hier die Frage.

**Was dabei anders kam als geplant:** die Kennung des Partners steht im *Gerät*
(`peerClientId`) und nicht in der eigenen `clients.json`. Dort wäre sie ein
zweites Feld für dieselbe Angabe — der Eintrag hat seine Kennung bereits, sie
ist der Fingerabdruck über den Ausweis, und beide Seiten rechnen ihn gleich aus.

### Nachtrag 16.08.2026 — die Seite hat zwei Ebenen statt einer

Nach dem ersten Blick auf die gebaute Seite umgestellt:

- **Die Liste zeigt Geräte, sonst nichts.** Umbenennen sitzt als Stift am Namen
  und nicht mehr im Aufklapper — es ist die eine Änderung, die man im
  Vorbeigehen macht. Darunter steht genau ein Knopf: **Neues Gerät koppeln**.
- **Der Knopf führt auf eine eigene Seite mit beiden Richtungen**
  (`views/PairingView.tsx`): oben *dieses Gerät anbieten* — Code, Countdown,
  QR-Code, die eigene Adresse zum Abtippen und der Fingerabdruck der eigenen
  Stelle —, darunter *ein anderes eintragen* (seine Adresse, sein Code), und
  von dort weiter zur Namensvergabe für beide Seiten. Zwei Knöpfe hätten eine
  Entscheidung verlangt, die vorher niemand treffen kann: beim Koppeln tun
  immer beide Seiten etwas.
- **Das Angebot ist ein eigener Baustein** (`views/PairingOffer.tsx`), und mit
  ihm ist auch „wer dieses Gerät steuern darf" dorthin gewandert. Es ist
  dieselbe Frage wie die darüber, nur in der Vergangenheitsform — und damit
  gibt es für den Kopplungscode weiterhin genau eine Stelle. Die vierte Art,
  auf der er verschwindet, ist jetzt buchstäblich das Verlassen der Seite: der
  Baustein wird abgebaut, und mit ihm der Code.
- **Im Fenster fehlt „Einstellungen"** in der Kopfzeile der Liste. Alles
  dahinter — Autostart, Updates, Einrichtung, Netz — gehört dort der Leiste
  daneben, und ein zweiter Weg zu denselben Einstellungen wäre einer zu viel.
- `ShareView` behält, was nur Android hat (Freigabe-Schalter, Bildschirm,
  Bedienungshilfe) und verweist für den Rest auf die Kopplungsseite.

## Phase 31 — Beide Richtungen im Fenster und in der App ✅ (16.08.2026)

- Die Geräteliste zeigt Handys mit eigenem Symbol; alles Weitere folgt schon
  aus Phase 27 → in 31g gebaut (`ComputerIcon`/`PhoneIcon`, gespeist aus
  `platform`)
- Kopplung eines Handys **am PC**: Adresse und Code werden getippt. Der Weg ist
  derselbe wie beim Handy, nur ohne Kamera — die Seite dafür gibt es bereits
  → sie ist unverändert die richtige: im Fenster steht `capabilities.camera`
  auf `false`, und `PairingView` zeigt dann das Formular
- `desktop/TrustImport.cs` muss die CA eines Handys ebenso annehmen wie die
  eines Rechners → nachgesehen: dort ist nichts auf Windows festgenagelt, es
  ist derselbe Port, dieselbe Datei, dieselbe Prüfung. Die Meldungen sagen
  jetzt „Gerät" statt „Rechner"
- Ein Gerät darf sich nicht selbst steuern → erledigt in 31h: die Sperre
  vergleicht Fingerabdrücke statt Namen, und der eigene kommt aus dem
  Steckbrief der eigenen Gegenstelle — am Handy also aus dem eigenen Host. Der
  Namensvergleich hatte zugeschlagen, weil ein Handy „David" heißt und der
  Rechner auch
- `docs/ARCHITEKTUR.md` und `docs/NETZ.md` nachgezogen: das Bild zeigt zwei
  gleichberechtigte Geräte statt einer Fernbedienung und zweier Rechner, und
  „Netz" steht dort, wo es jetzt steht

**Abnahme:** PC steuert Handy, Handy steuert Handy, Handy steuert PC — alles
aus derselben Oberfläche. **Am echten Gerät noch zu prüfen.**


---

## Phase 31i — die Oberfläche aufgeräumt ✅ (16.08.2026, am Gerät noch zu prüfen)

Nicht eine Funktion mehr, sondern weniger davon. Der Auslöser war ein Satz:
„auf dem PC und dem Handy ist alles ziemlich unübersichtlich geworden."

### Der eigene Gerätename — einmal, nicht jedes Mal

**Der Befund:** der Name wurde bei *jeder* Kopplung neu eingetippt, und zwar
zweimal — „wie heißt dieses Gerät drüben?" und „wie soll die Gegenseite hier
heißen?". Wer drei Geräte koppelte, vergab denselben Namen dreimal und tippte
ihn beim dritten Mal anders. Und wer gar nicht selbst koppelte, sondern nur
seinen Code vorzeigte, hieß drüben `DESKTOP-4F2K9L1` — dann sprang der
Systemname ein.

Jetzt steht er an genau einer Stelle, und zwar **nativ**, nicht im Speicher der
Weboberfläche: er geht in `/api/info`, und das beantwortet ein Gerät auch dann,
wenn keine Seite offen ist.

| | wo er liegt | wo er vergeben wird |
|---|---|---|
| PC | `{app}\data\devicename.txt` (`setup/DeviceNameFile.cs`) | erster Schritt der Einrichtung, danach Einstellungen |
| Handy | `HostPreference` (`deviceName`) | erster Start, danach Einstellungen |

Gelesen wird er **bei jedem Aufruf frisch** — `DeviceNameFile.Read` im Agent,
`HostRuntime.deviceName` als berechnete Eigenschaft am Handy. Wer sich
umbenennt, muss dafür weder den Agent neu starten noch die App. Deshalb nimmt
`HostServer` den Namen als Funktion (`() -> String`) und nicht als Wert: ein
Server, der den von seinem Start behielte, meldete nach einer Umbenennung den
alten.

Der zweite Name — der lokale Spitzname für die Gegenseite — ist beim Koppeln
**weg**. Die Gegenseite erscheint unter dem Namen, den sie sich selbst gegeben
hat; umbenennen geht weiterhin nachträglich in der Geräteliste.

### Der Fingerabdruck ist weg

Er stand an vier Stellen zum Vergleichen da — beim Koppeln, in der Übersicht des
Fensters, im Kopplungsangebot, auf der Freigabeseite — und **verglichen hat ihn
nie jemand**. Ein Sicherheitsschritt, den niemand ausführt, ist kein
Sicherheitsschritt, sondern eine Zeile, die den Blick auf die nimmt, die zählen.

Was bleibt: **kommt der Fingerabdruck über den QR-Code, wird er geprüft** — von
der App, gegen das geholte Zertifikat (`verify` in `PairingView`). Ohne QR-Code
gibt es keinen Vergleichswert; dann trägt der Kopplungscode allein: sechs
Ziffern, fünf Minuten, genau eine Kopplung. **Das ist eine bewusste Abwägung
und keine Nachlässigkeit.** Wer in genau diesem Fenster im Netz dazwischensitzt,
kommt durch; wer es nicht tut, kommt nie wieder heran.

### „Wer dieses Gerät steuern darf" ist weg

Die Liste stand im Kopplungsangebot und sagte dasselbe wie die Geräteliste — nur
an zweiter Stelle, mit einem zweiten Knopf zum Entfernen. Wer koppelt,
entscheidet damit ohnehin genau darüber, wer darin steht. Entfernt wird jetzt
an der einen Stelle, an der auch alles andere steht, und es wirkt weiterhin auf
**beiden** Seiten (`DELETE /api/unpair`).

### Was wo Sinn ergibt

| | PC (Fenster) | Handy |
|---|---|---|
| QR-Code **zeigen** | ja | ja |
| QR-Code **scannen** | nein (keine Kamera) | ja |
| Adresse + Code eintippen | ja | ja |
| Burger-Menü | nein (native Leiste daneben) | ja, **immer** |
| Freigabe schalten | nein — das ist der Agent | ja |

Das Burger-Menü gab es vorher erst, **sobald etwas verbunden war**. Damit war
der eine durchgehende Weg durch die App genau dann verschwunden, wenn man ihn
braucht: bevor überhaupt etwas gekoppelt ist. Es steht jetzt immer da und zeigt
die gekoppelten Geräte und die Einstellungen; die Ansichten des verbundenen
Geräts kommen dazu, sobald eines verbunden ist.

Im Fenster fehlt der Eintrag „Einstellungen" in der Seitenleiste: dort steht er
in der nativen Leiste daneben. Zwei Wege zu zwei verschiedenen
Einstellungsseiten wären einer zu viel.

### Der erste Start am Handy

Zwei Fragen, dann ist die App benutzbar (`views/FirstRunView.tsx`):

1. **Wie heißt dieses Gerät?**
2. **Darf es ferngesteuert werden?** Ein „nein" ist der Normalfall und kostet
   nichts. Bei „ja" geht es sofort weiter zu den beiden Rechten, die Android
   verlangt — Bedienungshilfe und Bildschirmaufnahme. Sie später nachzureichen
   hieße, dass am anderen Ende jemand wartend vor einem schwarzen Bild sitzt.

Am Rechner gibt es diese Seite nicht: dort führt der Einrichtungsassistent, und
der fragt jetzt als Erstes nach dem Namen.

### Deinstallieren räumt wirklich auf

**Der Befund:** wer deinstallierte, den Programmordner löschte und neu
installierte, fand seine alten Geräte wieder — mitsamt Zugangsdaten zu
Kopplungen, die auf der Gegenseite längst weg waren. Schuld war
`%localappdata%\RemoteDesktop`: darin liegt der Zwischenspeicher von WebView2
und damit der `localStorage` der Oberfläche, also die Geräteliste des Fensters.
Der Uninstaller kannte den Ordner nicht, weil ihn niemand angelegt hatte außer
WebView2 selbst.

* **PC:** `[UninstallDelete]` räumt ihn im Profil des Deinstallierenden weg;
  `CurUninstallStepChanged` geht zusätzlich über alle Profile unter
  `{sd}\Users`, weil eine erhöhte Deinstallation unter einem anderen Konto
  laufen kann.
* **Handy:** Android löscht `/data/data/app.remotedesktop.client` selbst. Neu
  ist `res/xml/data_extraction_rules.xml`: `allowBackup="false"` schaltet seit
  Android 12 nur noch das Cloud-Backup ab, die Übertragung auf ein neues Gerät
  ist ein zweiter Weg mit eigenen Regeln. Beide sind jetzt ausdrücklich leer —
  sonst hätte eine Neuinstallation den alten Stand aus der Sicherung
  zurückgeholt, und der private Host-Schlüssel wäre auf ein fremdes Handy
  gewandert.

**Was Android nicht hergibt:** ein Zertifikat, das der Nutzer in den
Systemeinstellungen als vertrauenswürdige Stelle installiert hat, überlebt die
Deinstallation. Es gibt dafür keine Schnittstelle. Wer restlos aufräumen will,
entfernt es unter *Einstellungen → Sicherheit → Verschlüsselung und
Anmeldedaten → Vertrauenswürdige Anmeldedaten → Nutzer*.

### Und die langen Texte

Weg. Sie standen überall, erklärten in drei Absätzen, was ein Schalter selbst
sagt, und haben jede Seite doppelt so lang gemacht wie nötig. Was an
Erklärung bleibt, steht in einem Satz oder gar nicht.

### Nachtrag — „Abgelehnt (Nicht angemeldet.)" nach der ersten sauberen Neuinstallation

Am Gerät geprüft, und es ging nicht: nach der Kopplung wurde **jede** Anfrage
abgewiesen, in beide Richtungen. Das Agent-Protokoll war eindeutig:

```
INFO  Kopplung in beide Richtungen mit Handy David (100.114.10.121:8443).
WARN  Abgelehnt (Nicht angemeldet.): GET /api/info von ::ffff:100.114.10.121
WARN  Abgelehnt (Nicht angemeldet.): GET /ws/screen von ::ffff:100.114.10.121
```

**„Nicht angemeldet"** und nicht „unbekannter Ausweis" — die App legte gar
keine Berechtigung vor. Die Kopplung selbst war einwandfrei.

Die Ursache lag in `transport/direct.ts`:

```ts
const key = readClientKey()                       // synchron, nur aus dem App-Speicher

if (device.clientId !== undefined && key !== undefined) {
  return pairedCredentials(device.clientId, key, sessionExchange(device))
}

return staticCredentials(device.token ?? '')      // -> Authorization: "Bearer "
```

Seit **31h** liegt der Ausweis nicht mehr im Speicher der Weboberfläche,
sondern bei der Gegenstelle des Geräts — `clientkey.txt` am Handy,
`{app}\data\clientkey.json` am Rechner —, und die antwortet nur asynchron.
`readClientKey()` fand also nichts, und der Rückfall auf ein **leeres**
Bearer-Token sah an jeder Aufrufstelle aus wie ein gültiger Ausweis.

**Warum es monatelang nicht auffiel:** in jedem bisher benutzten Gerät lag noch
ein Schlüssel aus der Zeit vor 31h im App-Speicher. Erst das Aufräumen bei der
Deinstallation (siehe oben) hat einen wirklich leeren Zustand hergestellt — und
damit den Fehler.

Behoben: `credentialsFor` entscheidet nur noch an `clientId`, also daran, dass
das Gerät gekoppelt *ist*, und `pairedCredentials` bekommt den privaten
Schlüssel als **Frage** (`() => Promise<string>`) statt als Wert. Dahinter
steht `clientPrivateKey()` → `ensureClientKey()`, das die native Quelle kennt.
`ensureClientKey` ist dafür von `lib/pairing.ts` nach `lib/clientKey.ts`
gewandert: `pairing.ts` holt sich `postJson` aus dem Transport, ein Zugriff in
die Gegenrichtung wäre ein Kreis.

Festgehalten in `transport/credentialsFor.test.ts` — insbesondere „ein
gekoppeltes Gerät fällt nie auf ein leeres Token zurück".

**Abnahme:** am echten Gerät noch zu prüfen — Erststart am Handy, Erststart am
PC, Koppeln in beide Richtungen ohne Namensfelder, Umbenennen im laufenden
Betrieb, und eine Deinstallation gefolgt von einer Neuinstallation.

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
