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
| App-Tests | `cd app && npm test` — Stand 05.08.2026: **306 grün** |
| Agent-Tests | `cd agent.Tests && dotnet test` — Stand 05.08.2026: **310, davon 1 rot** (siehe Phase 21) |
| Einrichtungs-Tests | `cd setup.Tests && dotnet test` — Stand 05.08.2026: **37 grün** |
| Kotlin-Tests | `cd clients/android/android && ./gradlew testDebugUnitTest` — **9 grün** |
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

**Status:** offen

### Umfang

- `SetupSteps` hängen am Modus: im Heimnetz und im eigenen VPN gibt es keine
  Tailscale-Schritte, dafür „Adresse festlegen"
- Im Heimnetz schlägt die Oberfläche die gefundene LAN-Adresse vor
- `docs/NETZ.md`: die drei Modi, was sie kosten, und für „eigenes VPN" eine
  Anleitung mit WireGuard, OpenVPN und ZeroTier als Beispielen
- README verweist darauf, „Tailscale" ist nicht mehr Voraussetzung

### Abnahme

- [ ] Tests belegen: im Heimnetz-Modus enthält die Schrittliste kein
      „Tailscale" · eine Adresse ohne Schema und ohne Port wird angenommen,
      `https://…` und `host:8443` werden abgelehnt

---

## Phase 23 — Eine Oberfläche für alles

**Status:** offen

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

- [ ] `grep -rn "CreateNoWindow" desktop/` belegt: kein Prozessstart ohne
- [ ] Tests belegen die Zustandslogik je Teil samt der erlaubten Handgriffe
- [ ] `cd desktop && dotnet build` ohne Warnung

---

## Phase 24 — Clients nehmen das eigene Zertifikat an

**Status:** offen

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

- [ ] Tests belegen: ein Zertifikat mit falschem Fingerabdruck wird abgelehnt
- [ ] `./gradlew testDebugUnitTest` grün

---

## Phase 25 — Installer, Release, Doku

**Status:** offen

### Umfang

- Installer legt immer alle Dateien ab; die Komponentenwahl entfällt bis auf
  Tailscale. Der Dienst wird nur noch auf Wunsch registriert
- `.github/workflows/release.yml` zieht die Umbenennung nach
- `docs/ARCHITEKTUR.md`, `docs/SICHERHEIT.md` (zweiter Port, eigene CA),
  `CLAUDE.md`, README

### Abnahme

- [ ] Kein Verweis mehr auf `RemoteDesktopClient.exe` außer dort, wo die
      Verträglichkeit mit alten Installationen es verlangt
