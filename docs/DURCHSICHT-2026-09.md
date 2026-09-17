# Durchsicht vor der Veröffentlichung — Stand 18.09.2026

Ziel laut Auftrag: eine fertige, simple App, die Windows→Android und
Windows→Windows (und weiterhin Android→Windows) ohne Ärger steuert, öffentlich
veröffentlichbar. Der Dateimanager (Phasen 32–35) bleibt außen vor.

Durchgesehen wurde der gesamte Quelltext: `agent/`, `setup/`, `desktop/`,
`app/`, `clients/android/`, `waker/`, Installer, Workflow, Docs. Testläufe:
Agent 382 grün, Setup 133 grün, App 323 grün, Kotlin grün, Waker 69 grün.

Die Nummern (A1, B3 …) sind für die Umsetzung gedacht; der Plan steht in
Abschnitt 7.

---

## 1. Die Ursache für „Bild bricht ab, Eingaben gehen noch" (Windows → Android)

Das ist kein einzelner Fehler, sondern eine Kette aus vier Stellen. Jede für
sich ist klein; zusammen ergeben sie genau das beobachtete Bild.

### A1 — Fehlerbänder sind seit 31q praktisch unsichtbar, und jeder Fehler baut den Bild-Socket neu auf

`useNotice()` in `app/src/lib/notice.ts:110-125` gibt bei **jedem Rendern** ein
neues Objekt mit neuen Funktionen `report` und `clear` zurück. In
`app/src/App.tsx:98` werden sie als `setError` und `clearError` benutzt, und
zwar als Abhängigkeiten von Effekten:

- `App.tsx:317-322`: `useEffect(() => { clearError() }, [selected, clearError])`
  läuft dadurch nach **jedem** Rendern — also auch direkt nach dem Rendern, in
  dem eine Meldung erschienen ist. Die Meldung ist einen Bilddurchlauf später
  wieder weg. Nachgewiesen mit einer kleinen React-Probe (Meldung nach
  `report()` bereits im nächsten Zustand leer).
- `App.tsx:812`: `onError={setError}` geht in die `ScreenView`, und dort hängt
  der JPEG-Effekt an `onError` (`ScreenView.tsx:385`) und der `getInfo`-Effekt
  ebenfalls (`ScreenView.tsx:296`). Folge: **jedes Rendern der Shell reißt den
  Bild-Socket ab und baut ihn neu auf** — bei jedem Verbindungszustandswechsel
  des Eingabekanals, bei jeder Meldung, beim Verschwinden einer Meldung.
  Gleiches Muster in `ActionsView.tsx:47` (Aktionen werden bei jedem Rendern
  neu geholt).

Bei Windows→Windows kostet das nur ein neues Vollbild und fällt kaum auf. Bei
Windows→Android löst es A3 aus.

**Fix:** `report` und `clear` in `useNotice` mit `useCallback` stabil machen (die
`Notices`-Instanz ist schon per `useMemo` stabil). Zusätzlich in `ScreenView`,
`ActionsView`, `MediaView` die Rückrufe in einem `useRef` halten statt in den
Effekt-Abhängigkeiten. Dazu ein Test, der genau das prüft: eine Meldung
überlebt ein Rendern.

### A2 — Der Android-Host vergisst die Aufnahme-Erlaubnis, behält aber die Zustimmung

`HostServer.partOver` (`host/HostServer.kt:367-375`) ruft beim Ende des
letzten **Bild**-Sockets `releaseScreen()` → `ScreenCapture.forget()`, die
Zustimmung der Sitzung (`HostSession.forget`) aber erst beim Ende der letzten
Verbindung **des Geräts**. Bleibt der Eingabe-Socket stehen und nur der
Bild-Socket wird neu aufgebaut (A1, A4, Netzwackler), dann:

1. ist die Projektion weg (`permission = null`),
2. ist die Sitzung noch „approved" → `confirmOnce` fragt **nicht** neu,
3. zeigt die App drüben keine Karte → niemand ruft `enableScreen()`,
4. wartet `awaitSource()` sechs Sekunden auf nichts und schickt `NO_SCREEN`
   („gibt seinen Bildschirm noch nicht frei"), der Socket wird geschlossen,
5. der Client verbindet neu → dasselbe, mit wachsender Wartezeit.

Genau das ist „Bild weg, Eingaben gehen noch".

**Fix (drei Teile):**

- Die Aufnahme-Erlaubnis an dieselbe Lebensdauer hängen wie die Zustimmung:
  `releaseScreen()` erst, wenn `live.countFor(client) == 0` — nicht wenn nur
  der Bild-Socket zu ist. Androids stille Rücknahme fängt der `onStop`-Rückruf
  ab, der ohnehin registriert ist.
- Fehlt die Erlaubnis trotz genehmigter Sitzung, statt `NO_SCREEN` ein
  Ereignis `screenPermissionNeeded` an die Oberfläche (`HostPlugin` →
  `notifyListeners`), das dort `enableScreen()` auslöst; der Socket wartet
  wie bisher bis zu sechs Sekunden.
- `ConnectionRequestView` und dieses neue Ereignis auf **eine** Stelle
  zusammenlegen, die den Systemdialog verantwortet.

### A3 — Der Android-Host schweigt in der Pause, der Client hält das für tot

`ScreenStream.run` (`host/ScreenStream.kt:124-127`) schickt bei `paused` gar
nichts — auch keine Kennzahlen. Der Windows-Agent tut das ausdrücklich
(`agent/Api/ScreenSocket.cs:137-149`). Der Client kündigt nach sechs Sekunden
Stille (`screenChannel.ts:21`) und baut neu auf → A2. Ausgelöst wird `pause`
bei jedem `visibilitychange` (`ScreenView.tsx:405-418`), am Rechner also beim
Minimieren des Fensters.

**Fix:** in der Pause weiter `sendStatsIfDue()` rufen, wie beim Agent.

### A4 — Der Thread-Vorrat des Android-Servers ist zu knapp bemessen

`HttpServer` (`host/HttpServer.kt:39,165`) hat 16 feste Arbeiter. Jede
Keep-Alive-Verbindung hält einen 30 Sekunden lang, jeder WebSocket dauerhaft,
der Bild-Socket zusätzlich einen Sender-Thread. Die Geräteliste fragt alle
vier Sekunden `/health` (`DeviceListView.tsx:36`), die Seitenleiste alle zehn,
dazu `/api/info`, Anmeldung, Sockets — ein Browser hält bis zu sechs
Verbindungen offen. Wird `activeCount` erreicht, wird die neue Verbindung
kommentarlos geschlossen — also auch ein Bild-Socket-Neuaufbau.

**Fix:** Keep-Alive-Leerlauf auf 5 s statt 30 s, Arbeiter auf 32, und
WebSockets nicht aus demselben Vorrat wie kurze Anfragen bedienen (eigener
Thread je Socket, der Vorrat nur für HTTP).

### A5 — Widget, Kachel und App-Kürzel finden den Geräteschlüssel nicht mehr

`SurfaceStore.clientKey` (`surfaces/SurfaceStore.kt:44-54`) liest den
privaten Schlüssel aus den Capacitor-Preferences unter
`remotedesktop.clientKey`. Seit 31h holt die App den Schlüssel aber vom
Host-Plugin (`app/src/lib/clientKey.ts:125-131`, `host/clientkey.txt`) und
schreibt die Preferences **nicht mehr**. Auf einer frischen Installation sagen
Widget, Kachel und Kürzel deshalb immer „Noch kein Rechner verbunden".

**Fix:** `SurfaceStore.clientKey` liest `LocalClientKey` aus dem Host-Ordner
(dieselbe Datei wie das Plugin); die Preferences bleiben nur als Rückfall für
Installationen von vor 31h.

### A6 — Die Erreichbarkeitsprüfung stapelt sich bei Geräten, die aus sind

`probeAll` läuft alle vier Sekunden, jeder Versuch hat drei Sekunden
Zeitlimit, und bei einem Fehlschlag folgen Zertifikatsabruf und ein zweiter
Versuch (`reachability.ts:22-33`, `DeviceListView.tsx:36`). Bei einem
ausgeschalteten Rechner dauert eine Runde bis zu neun Sekunden — länger als
der Takt. `setInterval` wartet nicht; die Runden überholen sich.

**Fix:** `setTimeout`-Kette statt `setInterval` (nächste Runde erst nach Ende
der vorigen), Zertifikatsabruf höchstens einmal je Minute je Gerät, und die
Seitenleiste nutzt dieselben Stände statt eigener Abfragen.

---

## 2. Sicherheit — was vor einer Veröffentlichung geändert gehört

Der Kern (Kopplung per Schlüsselpaar, Challenge-Response, Whitelist der
Pfade, signierte Updates, Widerruf trennt laufende Verbindungen) ist sauber
gebaut und auf drei Plattformen konsistent. Die Befunde betreffen die Ränder.

### B1 — Private Schlüssel liegen im Klartext in einem Ordner, den jeder Benutzer lesen und beschreiben darf (hoch)

`installer/RemoteDesktop.iss:147`: `{app}\data` hat `users-modify`. Darin:
`agentkey.txt`, `cert.key`, `agentca.pfx`, `agent.pfx`, `clientkey.json`,
`clients.json`, `setup.json`, `hotkey.txt`, `agent.log` (mit Kopplungscodes).
`docs/SICHERHEIT.md` behauptet noch Admin-ACLs.

Folgen: jeder lokale Prozess — auch ohne Adminrechte, auch ein zweiter
Benutzer — kann sich in `clients.json` mit allen Rechten eintragen, den
Agent-Schlüssel kopieren und damit gegenüber jedem gekoppelten Gerät dieser
Rechner *sein*. Der Agent läuft mit `HighestAvailable`; Aktionen aus
`actions.json` und der Installer laufen also erhöht — ein Weg von
„Standardbenutzer" zu „erhöht".

**Fix:** Ordner zurück auf `admins-full system-full users-read` (oder gar kein
Leserecht für Users). Die drei Dinge, die das Fenster ohne Rechte schreiben
muss (eigener Ausweis, Gegenrichtung bei gestopptem Agent, Gerätename und
Kürzel), gehen über den bereits vorhandenen erhöhten Aufruf (`Elevation`) —
je einmal je Kopplung eine Rückfrage von Windows, oder alternativ: der Agent
legt den Ausweis des Fensters an (tut er schon, `LocalClient.Ensure`), und das
Fenster liest nur. Zusätzlich die privaten Schlüssel mit DPAPI
(`ProtectedData`, Bereich `LocalMachine`) verschlüsseln, damit ein kopierter
Ordner nichts nützt.

### B2 — Die „nur lokal" erreichbaren Endpunkte haben gar keinen Ausweis (hoch am Handy, mittel am PC)

`/api/pair/code`, `/api/pair/grant`, `/api/clients` sind nur an Loopback
gebunden (`agent/Api/ClientAuthMiddleware.cs:38-45`, `host/HostScopes.kt:58`).
Auf Android heißt Loopback: **jede installierte App** kann sich einen Code
holen, sich koppeln und danach mit einem selbst gewählten Namen um Zustimmung
bitten — Bildschirm und Bedienungshilfe des ganzen Handys. Die App selbst
benutzt diese HTTP-Endpunkte am Handy gar nicht (`HostPlugin.pairingCode`
ruft die Runtime direkt).

**Fix:** Am Handy die `LOCAL_ONLY`-Endpunkte aus `HostServer.route` entfernen
(Kopplungscode und Clientliste laufen ausschließlich über das Plugin). Am PC
ein lokales Geheimnis: der Agent legt beim Start `local.secret` (32 Byte
Zufall) in den dann admin-only Datenordner; das Fenster schickt es als Bearer
bei den lokalen Aufrufen mit; ohne passendes Geheimnis 403 — auch von
127.0.0.1.

### B3 — Der Installer wird aus `%TEMP%` erhöht ausgeführt (hoch, sobald B1 behoben ist)

`InstallerUpdate.Launch` (`agent/Services/InstallerUpdate.cs:162,206-230`)
schreibt Installer und Startskript in den Temp-Ordner des Benutzers, wartet
fünf Sekunden und startet erhöht. In diesen fünf Sekunden kann ein
unprivilegierter Prozess beide Dateien austauschen; die Prüfsumme ist längst
geprüft. Dasselbe Muster in `AgentUpdater.Install` (`AgentUpdater.cs:233`).

**Fix:** nach `{app}\data\update\` (admin-only) laden, Prüfsumme unmittelbar
vor dem Start ein zweites Mal rechnen, Skript ebenfalls dorthin — oder den
Installer direkt per `Process.Start` mit `CREATE_BREAKAWAY_FROM_JOB` starten
und auf das Skript verzichten.

### B4 — „Einem anderen Rechner vertrauen" installiert eine fremde CA in den Benutzer-Stammspeicher (hoch)

`desktop/Pages/NetworkPage.cs:117-134,302-320` mit `TrustImport.Trust`
(`desktop/TrustImport.cs:83-90`): die CA des anderen Geräts landet unter
`CurrentUser\Root`. Danach glaubt **jeder Browser dieses Benutzers** dieser
CA — und die kann jeden Servernamen unterschreiben (B5). Das Fenster braucht
das nicht: `RemotePage` hat mit `TrustedAuthorities` eine eigene, auf die
Fernsteuerung begrenzte Liste. Der Text der Karte verweist zudem auf einen
Fingerabdruck „unter Übersicht", den es seit 31i nicht mehr gibt.

**Fix:** Karte und `TrustImport.Trust` entfernen. `TrustImport.FetchAsync`
bleibt (die Brücke braucht ihn).

### B5 — Die Geräte-CAs dürfen jeden Namen unterschreiben (mittel)

`SelfSignedCertificate.CreateAuthority` (`agent/Services/SelfSignedCertificate.cs:60-89`)
und `HostCertificate` (`host/HostCertificate.kt`) erzeugen CAs mit
`pathLen 0`, aber ohne Name Constraints. Am Handy landet die CA des Rechners
im Benutzer-Zertifikatspeicher, dem Chrome vertraut: wer einem fremden Gerät
vertraut, vertraut dessen Besitzer für `google.de`.

**Fix:** X.509 Name Constraints (`2.5.29.30`, kritisch) auf private
Adressbereiche (10/8, 172.16/12, 192.168/16, 100.64/10) und die Suffixe
`.local`, `.fritz.box`, `.ts.net`, plus den eingetragenen Namen. Bestehende
Kopplungen bleiben gültig, weil nur neu erzeugte CAs betroffen sind; für alte
einmal neu koppeln.

### B6 — Manuelle Kopplung ohne QR nimmt die CA ungeprüft an (mittel)

`PairingView.trust()` (`app/src/views/PairingView.tsx:307-335`): ohne
Fingerabdruck aus dem QR-Code wird `downloadAuthority()` über HTTP geholt und
**ohne Vergleich** installiert. Wer in den fünf Minuten im WLAN dazwischen
sitzt, hat danach eine CA auf dem Handy. Der Kommentar nennt das bewusst.
Für ein Ein-Personen-Heimnetz vertretbar, für eine Veröffentlichung nicht
mehr.

**Fix:** Bei der Anzeige des Codes ohne QR-Weg die ersten **acht** Zeichen
des CA-Fingerabdrucks daneben zeigen und im Client als drittes Feld
abfragen (Adresse · Code · Prüfzeichen). Acht Hexzeichen tippt man, 64 nicht.

### B7 — Das alte Sammel-Token ist noch aktiv (mittel)

`ClientAuth` (`agent/Auth/ClientAuth.cs:47-75,108-119`) nimmt `Agent:Token`
weiter an; `Device.token` und `staticCredentials` in der App ebenso. Ein
Geheimnis, das für alles gilt und keine Rechte kennt. Die Kopplung ist seit
Phase 10 überall durch.

**Fix:** entfernen — Agent, `transport/credentials.ts`, `Device.token`,
`deviceSources.toDevice` (Geräte ohne `clientId` verwerfen).

### B8 — Kleinere Punkte

- `/api/pair` lässt den Client eigene `scopes` wählen (`PairingEndpoints.cs:152-156`).
  Es kommt immer `All` heraus; Parameter streichen, weniger Fläche.
- `ChallengeStore` leert bei 64 offenen Challenges **alles**
  (`ChallengeStore.cs:40-45`, `HostSecrets.kt:132-134`): 64 Anfragen ohne
  Ausweis sperren alle anderen für Sekunden aus. Älteste verwerfen statt alle.
- `release.keystore` liegt im Workspace-Root (ignoriert, aber auf einem
  CIFS-Mount mit 777). Aus dem Repo-Ordner heraus.
- `waker`: Abhängigkeit `zod` wird nirgends benutzt.
- `agent.log` enthält Kopplungscodes — in Ordnung, sobald B1 steht.
- Die WebView2-Brücke gibt der Seite den privaten Ausweis (`local-key`). In
  Ordnung, solange die WebView nie fremde Inhalte lädt; das gehört als Regel in
  `SICHERHEIT.md`.

---

## 3. Die Netzmodi — was im Code steckt (Heimnetz, Tailscale, anderer VPN)

Getestet wurde bisher nur Tailscale. Aus dem Code:

### C1 — Tailscale: das Zertifikat läuft nach 90 Tagen ab, und niemand erneuert es (hoch)

`tailscale cert` stellt Let's-Encrypt-Zertifikate mit 90 Tagen aus. Geholt
wird es genau einmal in der Einrichtung (`Elevation.FetchCertificate`); der
Agent liest es beim Start (`CertificateLoader.LoadOrCreate`) und prüft
nicht, ob es abgelaufen ist — er zeigt es vor. Danach scheitert jede
Verbindung, und die App meldet vage „meist ist das Zertifikat abgelaufen"
(`inputChannel.ts:269-273`). Das ist die eine Stelle, die bei jedem Nutzer
nach drei Monaten zuschlägt.

**Fix:** Der Agent prüft täglich `NotAfter`; unter 30 Tagen ruft er selbst
`tailscale cert --cert-file … --key-file …` (er läuft erhöht) und lädt das
neue Zertifikat per `ServerCertificateSelector` ohne Neustart. Ist das
Zertifikat abgelaufen und die Erneuerung scheitert, auf das selbst
ausgestellte zurückfallen und es im Fenster sagen.

### C2 — Heimnetz: IP-Wechsel ohne Agent-Neustart (mittel)

Das selbst ausgestellte Zertifikat enthält alle IPv4-Adressen **beim Start**
(`LocalAddresses.List`, `CertificateLoader.Names`). Ein Laptop, der das WLAN
wechselt, hat danach eine IP, die nicht im Zertifikat steht — die Verbindung
scheitert bis zum Neustart. Der Android-Host stellt beim Einschalten neu aus
(`HostRuntime.start`), hat also dasselbe Problem nur während einer laufenden
Sitzung.

**Fix:** `NetworkChange.NetworkAddressChanged` abonnieren, Zertifikat neu
ausstellen und per `ServerCertificateSelector` tauschen (derselbe Mechanismus
wie C1). Die CA bleibt, die Clients merken nichts.

### C3 — Heimnetz: IPv6 fehlt im Zertifikat (mittel)

`LocalAddresses` nimmt nur IPv4. Trägt jemand `pc.fritz.box` ein und die
FritzBox liefert AAAA, verbindet Android womöglich über IPv6 — der Name
steht zwar im Zertifikat (DNS-Eintrag), die Verbindung geht aber über eine
Adresse, an der Kestrel lauscht (`ListenAnyIP` ist Dual-Stack) — das geht.
Trägt jemand dagegen eine IPv6-Adresse ein, steht sie nicht als IP-Eintrag
im Zertifikat. Entweder IPv6-Adressen mit aufnehmen oder in `NetworkProfile.
RejectAddress` IPv6 ablehnen und in `NETZ.md` sagen: IPv4 oder Name.

### C4 — Anderer VPN-Anbieter (WireGuard u. ä.)

Funktioniert nach Code genauso wie Heimnetz mit eingetragener Adresse:
Zertifikat auf die eingetragene Adresse, Vertrauen über den QR-Code, keine
Sonderfälle. Am Handy bevorzugt `HostAddresses` einen VPN-Transport für die
eigene Adresse — richtig. Offen bleibt nur, dass WebRTC ohne STUN läuft
(`WebRtcSession`, `webrtcChannel.ts:59`): über ein VPN mit NAT zwischen den
Enden kommt kein Host-Kandidat durch, dann bleibt es beim JPEG — die App
schaltet von allein um. Das ist in Ordnung und sollte so in `NETZ.md` stehen.

### C5 — Waker nur mit Tailscale

Der Waker lauscht ohne `tailscale cert` im Klartext (`waker/src/index.ts:68`),
und die App darf von `https://` aus keine `http`-Adresse ansprechen. Im
Heimnetz-Modus ist der Waker damit unerreichbar. Entweder dieselbe
Selbst-CA-Logik wie beim Agent (Port 8442 mit `/ca.crt`) oder in `NETZ.md`
klar sagen: Waker setzt Tailscale voraus, im Heimnetz weckt ein zweiter
wacher Rechner.

### C6 — Kleines

- `DeviceProfile.Sanitize` und `usableProfile` lassen `host` bis 255 Zeichen
  frei durch — eine Adresse mit Leerzeichen oder `/` sollte abgelehnt werden
  (dieselbe Regel wie `NetworkProfile.RejectAddress`).
- Der Agent bindet `ListenAnyIP` — im Heimnetz gewollt. Für den Modus
  „Tailscale" wäre ein Binden nur an die Tailscale-Adresse (100.64/10) die
  engere Wahl; als Option, nicht als Pflicht.

---

## 4. Was sich vereinfachen lässt

### D1 — Zwei Update-Wege, einer reicht

`AgentUpdater` + `SelfUpdater` tauschen nur die Agent-`.exe` beim Start
(`.old`, `.new`, `.update`-Merker, eigenes Batch-Skript, Sonderfälle im
Installer `InstallDelete`). `InstallerUpdate` erneuert alles. Seit 31m/31r
läuft alles über den Installer; der Agent-Tausch ist der zweite Weg mit
eigenem Fehlerbild. Empfehlung: `AgentUpdater`, `SelfUpdater`,
`POST /api/update` und die drei `InstallDelete`-Zeilen streichen; die
Startprüfung ruft `InstallerUpdate` (mit Rückfrage im Fenster, nicht still).

### D2 — Toter oder überholter Code

- `setup/SetupSteps.cs` und `ISetupProbe`-Teile werden vom Fenster nicht mehr
  benutzt (nur Tests).
- `NetworkKind.Headscale`, `Coordinator.UpArguments` mit `--login-server`,
  `CoordinatorConfig`: Headscale ist seit 31s kein Modus mehr; das Lesen alter
  Dateien kann bleiben, der Rest nicht.
- `app/src/platform/web.ts` beschreibt eine PWA, die es nicht mehr gibt;
  sie bleibt als Testrückfall sinnvoll, sollte aber so heißen (`testPlatform`).
- `app/src/serviceWorker.ts` (Abmelden alter Worker) kann nach einer
  Übergangsfassung weg.
- `Device.token`, `staticCredentials` — siehe B7.
- `Inventory.Client` („Fernsteuerung"-Karte) wird nicht mehr angezeigt
  (`OverviewPage.Shown`).

### D3 — Dateien über 800 Zeilen

`ScreenView.tsx` (1059), `HostServer.kt` (987), `App.tsx` (910),
`SetupPage.cs` (906). Sinnvolle Schnitte: aus `ScreenView` die Übernahme
(`useTakeover`), die Gesten-Handler und die Symbolreihe herausziehen; aus
`HostServer` die Routen (`HostRoutes`) von Auth/Sockets trennen; aus `App.tsx`
die Tastaturweiterleitung (`useHardwareKeyboard`) und das Kürzel.

### D4 — Kommentare

Der Stil „Der Befund dahinter (Datum)" steht in fast jeder Datei, oft zehn
bis dreißig Zeilen. Das ist Historie im Quelltext. Für ein veröffentlichtes
Projekt: den Befund in einen Satz kürzen, die Geschichte in `TASKS-*.md`
belassen (dort steht sie ohnehin). Das halbiert viele Dateien und macht sie
für Fremde lesbar.

### D5 — Docs

- `README.md` beschreibt einen Installer mit drei Häkchen und „Geräte
  koppeln…" — beides gibt es nicht mehr (Einrichtung im Fenster, Seite
  „Geräte"). Headscale steht noch als Modus. Ankerlinks `#tailscale` und
  `#bedienungshilfe` (aus `SetupPage.cs:46` und `FirstRunView.tsx:19`)
  führen ins Leere.
- `docs/ARCHITEKTUR.md`: „vier Modi", „Windows-Dienst", „Pre-Shared-Token"
  — überholt.
- `docs/SICHERHEIT.md`: Stand 1. August; `agentkey.txt` „neben der .exe",
  keine Erwähnung von `users-modify`, der Bedienungshilfe, der CA im
  Benutzerspeicher. Muss vor Veröffentlichung neu geschrieben werden — die
  Bedienungshilfe ist das größte Recht, das die App verlangt (steht schon
  unter „Offene Risiken" in `TASKS-V4.md`).
- `PLAN-V2.md`, `TASKS.md`, `TASKS-V2.md`, `TASKS-V3.md` (2 900 Zeilen) nach
  `docs/archiv/` — für Fremde ist nur der aktuelle Stand interessant.

---

## 5. Oberfläche — Übersichtlichkeit und Texte

Die Oberfläche ist schon deutlich aufgeräumt (31i–31r). Was noch übrig ist:

### E1 — Ein Begriff für eine Sache

| Sache | heute | Vorschlag |
|---|---|---|
| Vollzugriff auf einen Rechner | Vollzugriff · Übernahme · „Toggle für Remote Windows-Steuerung" (`SettingsPage.cs:172`) · „Shortcut ändern" | **Vollzugriff**, Kürzel dafür: **Kürzel** |
| Eigene Tastenkombinationen | Shortcuts (Seite, Symbol) · „Kürzel" (`ActionsView.tsx:14`) | **Tastenkombinationen** — „Shortcut" und „Kürzel" sind sonst das Vollzugriff-Kürzel |
| Ein/Aus-Seite | „Power" (Sidebar) · „Ein/Aus" (`protocol.ts:23`) · „Energie" (Test) | **Ein/Aus** |
| Dieses Gerät freigeben | „Fernsteuerung dieses Geräts" · „Freigabe und Rechte" · „Dieses Gerät freigeben" | **Freigabe** |

### E2 — Texte, die falsch geworden sind

- `protocol.ts:23`: „Auf der Seite ‚Ein/Aus' lässt er sich aktualisieren" —
  das Update steht seit 31m in der Geräteliste.
- `agentClient.ts:158`: „Am Rechner ‚Geräte koppeln…' öffnen" — heißt „Geräte".
- `inputChannel.ts:269-273`: „Meist ist das Sicherheitszertifikat abgelaufen —
  im Fenster unter ‚Einrichtung' neu holen" — geraten; nach C1 überflüssig.
  Kürzen auf: „Verbindung zu X kommt nicht zustande."
- `NetworkPage.cs:124-128`: verweist auf den Fingerabdruck „im Fenster" — weg
  mit der Karte (B4).
- `SetupPage.cs:46`, `FirstRunView.tsx:19`: tote Links.

### E3 — Streichen oder kürzen

- `ConnectionRequestView.tsx:127-139`: zwei Hinweisabsätze unter der Frage.
  Behalten: nichts davon; die Frage und zwei Knöpfe reichen. Den
  Systemdialog-Hinweis („Android fragt gleich noch einmal") als einen kurzen
  Satz nur dann, wenn er kommt.
- `ShareView.tsx:84`: „Eingeschaltet. Läuft, solange die App nicht
  weggewischt wird. Jede Verbindung wird einzeln bestätigt." → „Eingeschaltet."
  Die beiden Regeln gehören einmal in die Doku, nicht neben jeden Schalter.
- `PairingOffer.tsx:123`: „Erst freigeben — der Code kommt vom laufenden
  Server. Einstellungen → Dieses Gerät freigeben." → „Zuerst unter Freigabe
  einschalten." mit direktem Knopf dorthin.
- `SetupPage.cs:355-360` (Heimnetz-Detailschritt): drei Zeilen über
  Zertifikate → ein Satz: „Das Handy bestätigt den Rechner einmal beim
  Koppeln."
- `SetupPage.cs:670-675`: „Es wird ein Eintrag in der Aufgabenplanung …"
  — technische Auskunft, streichen.
- `Inventory.cs:128-129,144-146`: Karten-Texte des Agents doppelt so lang wie
  nötig; „Macht diesen Rechner fernsteuerbar." reicht.
- Übersicht **und** Einstellungen haben je „Updates" und „Über"
  (`OverviewPage.cs:119-120`, `SettingsPage.cs:85-86`). Einmal reicht: in
  den Einstellungen. Die Übersicht zeigt Name, Agent, Netz.
- `DeviceListView.tsx:562`: der Tooltip am Aktualisieren-Knopf beschreibt die
  Technik; „Rechner ist danach kurz weg." reicht.
- `CertificateTrustStep.tsx:65-68`: „stellt sein Zertifikat selbst aus …
  danach nie wieder" → „X einmal bestätigen."
- `TakeoverSetup.tsx:76-79`: den zweiten Satz streichen.

### E4 — Abläufe

- Fehlerbänder (A1) müssen erst wieder sichtbar sein, bevor sich die Texte
  beurteilen lassen.
- Beim Klick auf ein Handy in der Liste: erst „Warte auf Bestätigung", dann
  Android-Dialog drüben, dann Bild. Nach A2 kommt der Dialog verlässlich.
- Am Rechner in der Sitzung mit einem Handy gibt es keine Symbolreihe; die
  Zoomgeste per gezogenem Rechtsklick ist nirgends erklärt. Ein einmaliger
  Hinweis beim ersten Verbinden zu einem Handy (wie das Kürzel beim ersten
  Rechner) — ein Satz.

---

## 6. Was in Zukunft Probleme machen könnte

- **Neuere Android-Fassungen:** `FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION`
  verlangt seit API 34, dass der Dienst *vor* `getMediaProjection` mit diesem
  Typ läuft (gelöst). Jede weitere Verschärfung — etwa ein Ende der Projektion
  beim Sperren — kommt über denselben `onStop`-Rückruf an; die Oberfläche sagt
  dann aber nichts. Ein Satz „Aufnahme am Handy beendet" wäre besser als
  Schwarz.
- **Bedienungshilfe im Play Store:** Google verlangt für
  Accessibility-Services eine Begründung und ein Formular; außerhalb des Play
  Store nicht nötig. Für GitHub-Releases in Ordnung, in `SICHERHEIT.md`
  erwähnen.
- **GitHub-API ohne Token:** 60 Anfragen je Stunde und IP. Agent, Fenster und
  App fragen je Start; die App zusätzlich in den Einstellungen. In einem
  Haushalt mit mehreren Geräten hinter einer IP reicht das, bei einem
  Firmennetz nicht. Antwort mit `ETag`/`If-None-Match` zwischenspeichern.
- **WebView2 Runtime** kann fehlen (Fenster sagt es). Der Installer könnte
  den Evergreen-Bootstrapper mitliefern.
- **ffmpeg** ist nicht dabei; H.264 hängt an einer Datei, die der Nutzer
  selbst besorgt. Für Veröffentlichung: entweder `ffmpeg.exe` (LGPL-Build)
  mitliefern oder den Knopf „H.264" ausblenden, solange keins da ist —
  heute steht er da und fällt still auf JPEG zurück.
- **`SendARP`/Standort-Kennung** gibt es unter Windows nur für IPv4; bei
  reinen IPv6-Netzen fehlt der Weckknopf.
- **Vitest auf dem CIFS-Mount:** Worker-Timeouts (Umgebung 232 s). Nicht der
  Code; für CI egal, lokal `--pool=threads` probieren.

---

## 7. Plan

Reihenfolge nach Wirkung auf das Ziel „veröffentlichbar". Jede Phase endet
mit grünen Tests und einem Commit; am Gerät geprüft wird R1 und R3.

### R1 — Stabil zwischen Windows und Android (zuerst)

1. A1: `useNotice` stabil, Effekt-Abhängigkeiten in `ScreenView`,
   `ActionsView`, `MediaView` über Refs. Test: Meldung überlebt ein Rendern;
   `ScreenChannel` wird bei einem Rendern der Shell nicht neu gebaut.
2. A2: Erlaubnis-Lebensdauer an die Geräteverbindung; Ereignis
   `screenPermissionNeeded`; `ConnectionRequestView` übernimmt den Dialog.
   Kotlin-Test: Bild-Socket neu, Eingabe-Socket bleibt → keine `forget()`.
3. A3: Kennzahlen in der Pause. Kotlin-Test vorhanden erweitern.
4. A4: HttpServer-Vorrat.
5. A5: `SurfaceStore.clientKey` aus `host/clientkey.txt`.
6. A6: Erreichbarkeitsprüfung entzerren.
7. Abnahme am Gerät: Windows→Android 30 Minuten mit Minimieren, WLAN-Wechsel
   am Handy, Fenster in den Hintergrund; Widget/Kachel auf frischer
   Installation.

### R2 — Sicherheit vor der Veröffentlichung

1. B1 Datenordner admin-only + DPAPI; Schreibwege des Fensters über
   `Elevation` (eine Rückfrage je Kopplung ist vertretbar) — oder der Agent
   schreibt und das Fenster fragt ihn (läuft er nicht, Rückfrage).
2. B2 lokales Geheimnis am PC, Loopback-Endpunkte am Handy weg.
3. B3 Installer aus admin-only Ordner, Prüfsumme vor dem Start.
4. B4 Trust-Karte und `TrustImport.Trust` entfernen.
5. B7 Sammel-Token entfernen, B8 Kleinkram.
6. B5 Name Constraints, B6 Prüfzeichen bei manueller Kopplung.
7. `SICHERHEIT.md` neu: Bedrohungsmodell, Bedienungshilfe, CA am Handy,
   was ein verlorenes Gerät bedeutet.

### R3 — Netzmodi

1. C1 Tailscale-Zertifikat automatisch erneuern, Zertifikat ohne Neustart
   tauschen.
2. C2 IP-Wechsel im Heimnetz ohne Neustart.
3. C3 IPv6 entscheiden (aufnehmen oder ablehnen) und dokumentieren.
4. C5 Waker: Selbst-CA oder Doku.
5. Abnahme: je Modus einmal koppeln und steuern (Heimnetz mit IP, Heimnetz
   mit `pc.fritz.box`, WireGuard, Tailscale), einmal Adresse wechseln.

### R4 — Oberfläche und Texte

E1–E4 in einem Durchgang; danach ein Durchgang am Gerät nur mit dem Blick
„was steht da, das ich nicht brauche".

### R5 — Aufräumen und Doku

D1 (ein Update-Weg), D2 (toter Code), D3 (große Dateien), D4 (Kommentare
kürzen), D5 (README, ARCHITEKTUR, SICHERHEIT neu; alte Pläne ins Archiv).
Dazu: ffmpeg-Entscheidung (Abschnitt 6), GitHub-API-ETag.

### R6 — Abnahme-Matrix vor dem Tag

| Richtung | Heimnetz | Tailscale | anderer VPN |
|---|---|---|---|
| Handy → PC | Bild, Maus, Tastatur, Medien, Ein/Aus, Aktionen, Wecken | dito | dito |
| PC → Handy | Bild (JPEG), Tippen, Wischen, Text, Zoom, Zurück/Home, 30 min Dauer, Minimieren, Pause | dito | dito |
| PC → PC | Bild (H.264 und JPEG), Vollzugriff ein/aus, Kürzel, Monitorwechsel | dito | dito |
| Kopplung | QR, von Hand mit Prüfzeichen, Entfernen beidseitig, Verbindungstest | dito | dito |
| Update | Fenster, von Gerät aus, Handy | — | — |

Nach R1–R3 ist die App aus meiner Sicht veröffentlichbar; R4 und R5 machen
sie verständlich und wartbar.
