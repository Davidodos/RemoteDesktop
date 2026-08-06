# V3 — eine Oberfläche, ein Netz nach Wahl

Arbeitsanweisung nach Release v1.0.0. Drei Befunde aus dem ersten echten
Durchlauf, dazu die Entscheidungen vom 05.08.2026.

## Die Befunde

1. **Die Teile haben keine gemeinsame Oberfläche.** Wer nur den Agent
   installiert, hat gar kein Fenster; wer nur den Client installiert, sieht den
   Agent nirgends. Was fehlt, wird ausgeblendet statt angeboten
   (`ClientTrayContext.InstalledSelection`) — man kann nichts nachinstallieren,
   ohne den Installer erneut zu suchen.
2. **Der Agent startet nicht ohne Tailscale.** `AgentSettings.Load` wirft bei
   fehlendem `Agent:CertificatePath`, `CertificateLoader.Load` bei fehlender
   Datei. Wer den Rechner nur im eigenen Heimnetz steuern will, braucht kein
   VPN — bekommt aber einen Dienst, der sich sofort wieder beendet.
3. **„Zertifikat holen" blitzt und tut nichts.** `WindowsAutostart.Run` startet
   `tailscale.exe` ohne `CreateNoWindow` (daher das Terminal) und ohne
   Ausgabeumleitung (daher kein Fehlertext). Und `tailscale cert <name>` ohne
   `--cert-file`/`--key-file` schreibt ins Arbeitsverzeichnis statt nach
   `C:\ProgramData\RemoteDesktopAgent\` — wohin ohnehin nur ein Administrator
   schreiben darf. Der Schritt konnte nie „erledigt" werden.

## Entscheidungen (05.08.2026, mit David)

- **Eine UI-.exe.** `RemoteDesktop.exe` ist der einzige Startpunkt für den
  Menschen: gemeinsame Oberfläche, Tray, Einrichtung, Start und Stopp beider
  Dienste. `RemoteDesktopAgent.exe` bleibt daneben liegen als reine
  Dienst-Binärdatei — sie wird nie von Hand gestartet. **Nicht** in eine Datei
  verschmolzen: sonst trüge ein Dienst unter SYSTEM WinForms und WebView2 mit.
- **Alles mitliefern, die Oberfläche richtet ein.** Der Installer legt immer
  alle Dateien ab. „Nicht installiert" heißt: Dienst nicht registriert bzw.
  Teil nicht aktiv. Nachinstallieren ist ein UAC-Sprung, kein Download.
- **Drei Netzmodi statt Tailscale-Zwang:**
  - **Heimnetz (LAN)** — kein VPN. Agent und Handy im selben Netz. Der
    Normalfall für „PC steht zuhause, Handy hängt im WLAN".
  - **Tailscale** — wie bisher, weiterhin die Empfehlung für unterwegs.
  - **Eigenes VPN** — wer schon WireGuard, OpenVPN, ZeroTier o. ä. benutzt,
    trägt die Adresse ein, unter der dieses Gerät dort erreichbar ist.
    RemoteDesktop startet und prüft das VPN **nicht**; dafür gibt es eine
    Anleitung (`docs/NETZ.md`).
- **TLS bleibt in jedem Modus.** Ohne Tailscale stellt sich der Agent selbst
  ein Zertifikat aus. Damit das ein Client auch annimmt, gibt es eine eigene
  kleine CA, deren Fingerabdruck über die Kopplung geht.

## Ablauf je Phase

Wie in `docs/TASKS-V2.md`: Zustand lesen → nur diese Phase bauen → Abnahme
nachweisen (Tests laufen lassen, Ausgabe zeigen) → eintragen → committen.

## Umgebung

| | |
|---|---|
| App-Tests | `cd app && npm test` — Stand 05.08.2026: **314 grün** (vorher 306) |
| Agent-Tests | `cd agent.Tests && dotnet test` — Stand 05.08.2026: **336 grün** (vorher 310, davon 1 rot) |
| Einrichtungs-Tests | `cd setup.Tests && dotnet test` — Stand 05.08.2026: **73 grün** (vorher 37) |
| Kotlin-Tests | `cd clients/android/android && ./gradlew testDebugUnitTest` — **16 grün** (vorher 9) |
| Windows-Client | `cd desktop && dotnet build` — baut auch auf Linux, läuft dort nicht |

---

## Phase 21 — Der Agent startet immer

**Status:** erledigt (05.08.2026)

Befund 2. Kein Zertifikat mehr, ohne das nichts geht.

### Umfang

- `setup/NetworkProfile.cs`: die drei Modi, die Adresse, der Koordinator; in
  derselben `setup.json`, die es schon gibt (alte Dateien mit nur
  `coordinator` gelten weiter als Tailscale-Profil)
- `agent/Services/SelfSignedCertificate.cs`: eigene CA (10 Jahre) plus
  Serverzertifikat (2 Jahre, selbsttätig erneuert). Alternativnamen aus dem
  Profil, dem Rechnernamen, allen lokalen IPv4-Adressen und `localhost`
- `CertificateLoader.LoadOrCreate`: ein vorhandenes Tailscale-Zertifikat
  gewinnt; sonst das eigene. **Kein Startabbruch mehr**
- `GET /ca.crt` unverschlüsselt auf einem zweiten Port (Vorgabe 8442),
  ausschließlich diese eine Route. Sie liefert nur ein öffentliches
  Zertifikat — geprüft wird es am Fingerabdruck aus der Kopplung
- `caFingerprint` in `/api/info` und in der Antwort auf `/api/pair`
- `ManifestVerifierTests`: der Test auf den leeren Release-Schlüssel prüft
  seit dem Eintragen des echten Schlüssels den Repo-Zustand statt das
  Verhalten. Er wird auf einen ausdrücklich leeren Schlüssel umgestellt

### Abnahme

- [x] `agent.Tests` **336 statt 310** und ohne den einen roten von vorher;
      `setup.Tests` **53 statt 37**. Kein bestehender Test geändert außer
      `ManifestVerifierTests.Ohne_einkompilierten_Schluessel_geht_gar_nichts`:
      er prüfte `ReleaseKeys.PublicKey` und damit den Repo-Zustand — seit dem
      ersten Release steht dort ein echter Schlüssel, und der Test schlug
      ausgerechnet dann fehl, als das Projekt richtig eingerichtet wurde. Er
      prüft jetzt einen ausdrücklich leeren Schlüssel
- [x] `SelfSignedCertificateTests` (13), `CertificateVaultTests` (4),
      `CertificateChoiceTests` (6) belegen alle genannten Punkte. Dazu die
      Kette selbst: ein Client, der die CA kennt, baut sie erfolgreich auf
      (`X509Chain` mit `CustomRootTrust`) — ohne diesen Test fiele ein Fehler
      darin erst am Handy auf

### Notizen

- **Der zweite Port (Vorgabe 8442) trägt genau eine Datei.** Ohne ihn gäbe es
  ein Henne-Ei-Problem: ein Client kann das CA-Zertifikat nicht über eine
  Verbindung holen, der er noch nicht traut. Die Weiche steht als erstes
  Middleware-Stück und nicht als Route — dort könnte sie jemand übersehen und
  versehentlich einen zweiten Weg zu `/api/*` öffnen. Geöffnet wird er nur,
  wenn es überhaupt eine eigene CA gibt
- **Der Agent liest jetzt `RemoteDesktopSetup`.** Zwei Leser derselben
  `setup.json` mit je eigener Auslegung wären eine Falle
- **`Agent:CertificatePath` und `Agent:KeyPath` dürfen fehlen.** Das war der
  eigentliche Befund: `AgentSettings.Load` warf, und von außen sah ein Rechner
  ohne Tailscale-Zertifikat aus wie einer, der gar nicht läuft

---

## Phase 22 — Drei Netzmodi und die Anleitung

**Status:** erledigt (05.08.2026)

### Umfang

- `SetupSteps` hängen am Modus: im Heimnetz und im eigenen VPN gibt es keine
  Tailscale-Schritte, dafür „Adresse festlegen"
- Im Heimnetz schlägt die Oberfläche die gefundene LAN-Adresse vor
- `docs/NETZ.md`: die drei Modi, was sie kosten, und für „eigenes VPN" eine
  Anleitung mit WireGuard, OpenVPN und ZeroTier als Beispielen
- README verweist darauf, „Tailscale" ist nicht mehr Voraussetzung

### Abnahme

- [x] `setup.Tests` **60 statt 53**, kein bestehender Test geändert. Die alte
      Überladung `SetupSteps.For(selection, probe)` gibt es weiter und liefert
      dieselbe Liste wie vorher — ein Test hält genau das fest, damit ein Update
      bestehende Installationen nicht stumm umschreibt
- [x] `SetupStepsProfileTests` belegt: im Heimnetz-Modus enthält **kein**
      Schritt das Wort „Tailscale", weder im Titel noch in der Erklärung · ohne
      Tailscale entfällt „Zertifikat holen" (der Agent stellt es selbst aus) ·
      eine eingetragene Adresse hakt den ersten Schritt ab · beim eigenen VPN
      wird auf die Anleitung verwiesen
- [x] Adressprüfung in `NetworkProfileTests` (Phase 21): `192.168.178.20`,
      `pc.fritz.box` und `[fd7a::1]` werden angenommen, `https://…`,
      `…:8443` und `…/pfad` abgelehnt

### Notizen

- **`docs/NETZ.md` sagt auch, was nicht geht:** ein kommerzieller VPN-Dienst
  (NordVPN, Mullvad …) leitet nur den Internetverkehr um — die eigenen Geräte
  sehen sich dort nicht. Das ist der Fehlschlag, den sonst niemand einordnen
  kann
- **RemoteDesktop startet fremde VPN nicht und prüft sie nicht.** Das wäre eine
  Fläche für konfigurierbare Kommandozeilen, und genau die gibt es im Agent
  bewusst nirgends (Regel aus Phase 13)

---

## Phase 23 — Eine Oberfläche für alles

**Status:** erledigt (05.08.2026)

Befund 1 und 3.

### Umfang

- `setup/Inventory.cs`: je Teil ein Zustand (nicht installiert · installiert,
  gestoppt · läuft) und die Handgriffe, die dazu passen — prüfbar ohne Windows
- `desktop/`: aus `RemoteDesktopClient.exe` wird `RemoteDesktop.exe`. Ein
  Fenster mit allen Teilen, auch den nicht eingerichteten; Einrichtung,
  Autostart, Updates und Netz in denselben Fenster-Reitern
- Tray: Agent starten/beenden, Fenster öffnen, Einrichtung, Beenden
- `desktop/Elevation.cs`: Handgriffe mit Adminrechten laufen über einen
  Selbstaufruf `--admin-task <name>`; das Ergebnis kommt als Datei zurück
- `desktop/ProcessRunner.cs`: startet ohne Fenster, fängt Ausgabe und
  Fehlertext ab. **Behebt den Terminalblitz.** `tailscale cert` bekommt
  `--cert-file`/`--key-file` und läuft erhöht

### Abnahme

- [x] `setup.Tests` **73 statt 60**. `InventoryTests` (13) belegt die
      Zustandslogik je Teil samt der erlaubten Handgriffe — vor allem: ein
      nicht eingerichteter Agent steht mit dem Knopf da, der ihn einrichtet ·
      ohne Programmdatei gibt es keinen Knopf (er liefe ins Leere) · die
      Fernsteuerung kennt kein Starten und Beenden (sie ist kein Dienst) · es
      sind immer drei Kacheln, egal was fehlt
- [x] `dotnet build desktop/` — **Build succeeded**, keine Warnung
      (`TreatWarningsAsErrors` ist an)
- [x] Jeder Prozessstart läuft über `ProcessRunner` mit `CreateNoWindow` und
      umgeleiteter Ausgabe. Die drei verbliebenen `UseShellExecute = true` sind
      begründet: zwei öffnen eine Adresse im Browser, der dritte startet den
      Installer mit UAC-Rückfrage

### Notizen

- **Die Schrittliste im Fenster ist zu einem Satz geschrumpft.** Sie stand
  vorher neben der Teileliste und sagte dasselbe zweimal. Jetzt trägt jede
  Kachel ihren Zustand, und darüber steht die eine Zeile „Als Nächstes: …".
  `SetupSteps` bleibt die Quelle dafür
- **`ClientUpdate` startete den Installer ohne Rechtesprung.** Aufgefallen beim
  Durchsehen der Prozessstarts: der Installer verlangt im Manifest
  Administratorrechte, und mit `UseShellExecute = false` gibt es keine
  Rückfrage, sondern den Fehler „Elevation required". Das Update über den Knopf
  aus dem Nachtrag vom 04.08.2026 konnte so nie funktionieren. Behoben
- **Der Dienstzustand wird beim Agent selbst erfragt** (`/health` über
  Loopback), nicht bei der Dienstverwaltung: ein Dienst kann laufen und
  trotzdem nichts beantworten. Für „läuft" zählt nur, was ein Handy davon
  hätte. Nebenbei erspart es das Auswerten der übersetzten Ausgabe von
  `sc query`
- **Die `.exe` heißt jetzt `RemoteDesktop.exe`.** Der Namensraum im Quelltext
  bleibt `RemoteDesktopClient`, damit der Umbau nicht jede Datei anfasst

---

## Phase 24 — Clients nehmen das eigene Zertifikat an

**Status:** erledigt (05.08.2026), Hardware-Prüfung offen

Ohne diese Phase startet der Agent zwar ohne Tailscale, aber kein Client
verbindet sich zu ihm.

### Umfang

- Windows: die CA auf Wunsch in den Zertifikatspeicher des Benutzers, mit
  angezeigtem Fingerabdruck und Rückfrage
- Android: `network_security_config.xml` nimmt vom Benutzer installierte CAs
  an; die App holt `ca.crt`, vergleicht den Fingerabdruck mit dem aus der
  Kopplung und übergibt sie dem System zum Installieren
- App: `caFingerprint` wird beim Koppeln mitgeschrieben

### Abnahme

- [x] App-Tests **314 statt 306** (`certificateTrust.test.ts`, 8),
      Kotlin-Tests **16 statt 9** (`CertificateTrustTest`, 7).
      `npx tsc -b --force` sauber, `./gradlew testDebugUnitTest` grün
- [x] Beide Seiten belegen: ein Zertifikat mit falschem Fingerabdruck wird
      abgelehnt · ohne Fingerabdruck wird gar nicht erst geholt (die App fragt
      den Rechner nicht einmal) · was kein X.509-Zertifikat ist, wird abgelehnt,
      auch wenn sein Fingerabdruck stimmt · ein Serverzertifikat ohne
      `CA:true` wird abgelehnt, weil es im Speicher wirkungslos wäre

### Notizen

- **Geprüft wird zweimal, in der App und nativ.** Kein Versehen: die
  Weboberfläche ist austauschbar, und was über das Vertrauen des ganzen Geräts
  entscheidet, soll nicht allein an ihr hängen
- **`cleartextTrafficPermitted="true"` im Android-Netzprofil.** Es gilt für
  genau eine Datei — das öffentliche Zertifikat auf Port 8442. Über eine
  verschlüsselte Verbindung wäre es nicht zu holen; das ist ja gerade die, die
  ohne dieses Zertifikat nicht zustande kommt. Bild, Eingabe und Sitzungstoken
  laufen unverändert ausschließlich über `https`/`wss`, und die App baut
  nirgends eine `http`-Adresse zu einem Agent zusammen
- **Warum eine CA im Systemspeicher und nicht `onReceivedSslError`.** Letzteres
  greift bei Chromium nicht für WebSocket-Verbindungen — Bild und Eingabe
  wären stumm ausgefallen, während die REST-Aufrufe funktionieren. Der Weg über
  den Speicher des Systems gilt für alles
- **Der Windows-Client hat denselben Weg**, nur ohne Systemdialog: Adresse
  eintragen, Zertifikat holen, Fingerabdruck vergleichen, bestätigen. Es landet
  im Speicher **dieses Benutzers**, nicht des Rechners — eine Stelle, der man
  vertraut, gilt für alles, was danach kommt, und diese Entscheidung darf einer
  für sich treffen und nicht für alle
- **Offen für die Hardware-Prüfung:** ob Android den Dialog aus
  `KeyChain.createInstallIntent` wie erwartet zeigt und die Stelle danach für
  `wss` gilt. Das lässt sich hier nicht ausführen

---

## Phase 25 — Installer, Release, Doku

**Status:** erledigt (05.08.2026)

### Umfang

- Installer legt immer alle Dateien ab; die Komponentenwahl entfällt bis auf
  Tailscale. Der Dienst wird nur noch auf Wunsch registriert
- `.github/workflows/release.yml` zieht die Umbenennung nach
- `docs/ARCHITEKTUR.md`, `docs/SICHERHEIT.md` (zweiter Port, eigene CA),
  `CLAUDE.md`, README

### Abnahme

- [x] `grep -rn "RemoteDesktopClient.exe"` findet außerhalb dieser Datei nichts
      mehr. Der Registry-Wert heißt weiterhin `RemoteDesktopClient` — der Name
      bleibt absichtlich, sonst entstünde bei einem Update ein zweiter
      Autostart-Eintrag neben dem alten
- [x] Der Installer legt immer alle Dateien ab und räumt den alten
      `client\`-Unterordner weg. Gefragt wird nur noch, was laufen soll

### Notizen

- **Der alte Autostart-Eintrag wird auch entfernt**, wenn das Häkchen weg ist.
  Er zeigte sonst nach dem Update auf eine Datei, die es nicht mehr gibt, und
  Windows meldete bei jeder Anmeldung einen Fehler
- **`Tasks:` gibt es in der `[Tasks]`-Sektion nicht.** Beim ersten Tag-Lauf
  (v1.1.0, 05.08.2026) brach `iscc` mit „Unrecognized parameter name Tasks" ab.
  Eine Aufgabe, die eine andere voraussetzt, wird über einen **verschachtelten
  Namen** geschrieben (`agentservice\autostart`); Inno rückt sie dann ein und
  lässt sie nur ankreuzen, solange die übergeordnete steht. Verweise darauf —
  in `[Run]` und in `WizardIsTaskSelected` — tragen den vollen Namen mit
  Backslash
- **Offen für die Hardware-Prüfung:** den Installer mit `iscc` übersetzen und
  einmal durchspielen — Erstinstallation, Update über eine V2-Installation
  hinweg, Deinstallation

---

## Phase 26 — Ein Fenster, ein Zeichen, ein Fassungsvergleich

**Status:** erledigt (06.08.2026), Hardware-Prüfung offen

Drei Befunde aus dem ersten Durchlauf von v1.1.0.

### Befund 1 — es waren immer noch drei Fenster

Phase 23 hat die *Einrichtung* zusammengelegt, aber `MainWindow` (Fernsteuerung)
und `PairingWindow` (Kopplung) blieben eigene Fenster daneben. Wer ein Gerät
koppeln wollte, während er einen Rechner steuerte, schob Fenster.

- `desktop/ShellWindow.cs`: **ein** Fenster mit Seitenleiste und fünf Seiten
  (Übersicht · Fernsteuerung · Geräte · Netz · Einstellungen)
- `desktop/Pages/`: aus jedem alten Fenster wird eine Seite. `ControlPanel`,
  `MainWindow`, `PairingWindow`, `NetworkView` und `OptionsView` sind weg
- Die Reiter sind weg. Reiter sagen „diese drei Dinge gehören zusammen"; die
  Fernsteuerung ist aber keine Einstellung
- **F11** schaltet die Fernsteuerung randlos auf den ganzen Bildschirm. Der
  Tastendruck kommt über eine Nachricht aus der Seite (`WebMessageReceived`) —
  die WebView ist ein eigenes Fenster, Tastendrücke darin erreichen WinForms
  nie
- Die beiden Meldungsfenster (Widerruf, Zertifikat bestätigen) sind
  Rückfragen *in* der Karte geworden. Ein Fenster über dem Fenster ist die
  Bauweise, von der dieser Umbau wegwill

### Befund 2 — es sah aus wie ein Konfigurationsdialog

- `desktop/Ui/`: Palette, Karten, Knöpfe, Auswahlflächen, Rollbalken und
  Statuszeile selbst gezeichnet
- Die Farben sind **nicht erfunden**: es sind dieselben aus
  `app/src/styles.css`. Deshalb sitzt das eingebettete Fernsteuerbild nicht als
  Fremdkörper im Fenster
- Der Rollbalken ist selbst gezeichnet, weil Windows seinen nicht einfärben
  lässt — er wäre der eine helle Streifen, an dem man sieht, dass hier nur
  Farben überschrieben wurden
- Der **Fensterrahmen bleibt der von Windows** und wird nur dunkel gefärbt
  (`DwmSetWindowAttribute`). Ein selbst gezeichneter Rahmen müsste Andocken,
  Aufteilungsvorschläge und den Wechsel der Bildschirmskalierung nachbauen

### Befund 3 — die Updatesuche bot ewig dieselbe Fassung an

Gemessen an der echten Release-Datei von v1.1.0: in `RemoteDesktop.exe` steht als
ProductVersion nicht `1.1.0`, sondern
`1.1.0+435992d47c60e8d9890ee4e4a00aa1025499ffad`. Seit .NET 8 hängt der Build die
Commit-Kennung an die InformationalVersion. Verglichen wurde das gegen den nackten
Git-Tag — beide sind nie gleich, also bot die App bei **jeder** Suche ein Update
auf genau die Fassung an, die schon lief.

- `setup/ReleaseCheck.Normalize`: schneidet `v` vorne und alles ab dem
  Pluszeichen ab. Nach SemVer ist alles hinter dem Plus ausdrücklich *kein*
  Unterschied in der Fassung
- `ClientUpdate.InstalledVersion` geht durch dieselbe Stelle — damit im Fenster
  „Fassung 1.1.0" steht und nicht die Commit-Kennung

### Das Zeichen

Das Launcher-Symbol der APK war noch das Standardbild von Capacitor (blaues „X"
auf weißem Karo) — mit dem Monitor-Symbol der PWA hatte es nie etwas zu tun.

- `assets/icon.svg` ist ab jetzt die einzige Quelle, `assets/icon-small.svg`
  dasselbe Motiv für 16 bis 32 Pixel (bei 16 entfallen auf den Zeiger noch drei
  Bildpunkte)
- `node scripts/icons.mjs` erzeugt daraus: `desktop/RemoteDesktop.ico`
  (9 Größen, bis 64 als DIB, darüber als PNG), die Android-Mipmaps samt
  Vordergrundebene für adaptive Symbole, und `app/public/icon-{192,512}.png`
- Die Hintergrundfarbe des adaptiven Symbols ist jetzt die Kachelfarbe und
  nicht mehr Weiß. Die beiden ungenutzten Vorlagen-Vektoren von Android Studio
  sind weg

### Abnahme

- [x] `setup.Tests` **75 statt 73** — zwei Tests zum Fassungsvergleich, davon
      einer mit genau der Zeichenkette aus der ausgelieferten v1.1.0
- [x] `dotnet build desktop/` — **Build succeeded**, keine Warnung
- [x] `agent.Tests` 336, `app` 314 — unberührt
- [x] Kein `MessageBox.Show` mehr in `desktop/`
- [x] Die .ico wurde nach dem Erzeugen zerlegt und je Größe gegen das SVG
      geprüft — Reihenfolge der Bildzeilen, Farbkanäle und Alphakanal stimmen

### Notizen

- **`Stack.Clear()` entsorgt seine Kinder.** Das ist richtig für Karten, die
  bei jedem Anzeigen neu entstehen, und falsch für Felder, in die jemand gerade
  getippt hat. Deshalb bauen die Geräte- und die Netzseite ihre Karten **einmal**
  und füllen nur den Teil neu, der sich ändert
- **Die Fernsteuerung wird erst beim ersten Anzeigen aufgebaut.** Der Aufbau der
  WebView dauert eine knappe Sekunde; wer nur etwas einstellen will, soll nicht
  darauf warten. Danach bleibt sie stehen — ein Seitenwechsel darf keine
  laufende Sitzung abbrechen

---

## Was am echten Gerät noch geprüft werden muss

Hier nicht ausführbar, deshalb gesammelt:

- **Der Agent ohne Tailscale.** Dienst einrichten, starten, und im Log
  nachsehen: „Selbst ausgestelltes Zertifikat für …" mit Fingerabdruck. Danach
  `https://<adresse>:8443/health` vom Handy aus
- **Das Zertifikat am Handy bestätigen.** Ob Android den Dialog aus
  `KeyChain.createInstallIntent` zeigt und die Stelle danach auch für die
  WebSocket-Verbindungen gilt (Bild und Eingabe, nicht nur `/api/*`)
- **„Zertifikat holen" unter Tailscale** — jetzt ohne aufblitzendes Terminal,
  mit UAC-Rückfrage, und danach müssen `cert.crt` und `cert.key` in
  `C:\ProgramData\RemoteDesktopAgent\` liegen
- **Agent einrichten, starten, beenden, entfernen** aus dem Fenster und aus dem
  Tray-Menü
- **Der Installer** mit `iscc`, samt Update über eine V2-Installation hinweg
- **Das neue Fenster** (Phase 26): ob die Titelzeile auf diesem Windows dunkel
  wird, ob die Seiten bei 125 % und 150 % Skalierung noch stimmen, ob der
  selbst gezeichnete Rollbalken sich anfassen lässt, und ob F11 die
  Fernsteuerung wirklich randlos schaltet — auch aus der WebView heraus
- **Das Symbol** in Taskleiste, Infobereich und Explorer, und die APK auf dem
  Homescreen. Hier ist beides nur als Bilddatei prüfbar, nicht in Windows
- **Die Updatesuche** auf der v1.1.0-Installation: sie muss jetzt „das ist die
  neueste" sagen statt eine Neuinstallation anzubieten
