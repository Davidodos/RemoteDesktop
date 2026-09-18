# V2 — Ausführbarer Phasenplan

Arbeitsanweisung für eine **kalte Session**. Wer hier hereinkommt, hat den
Gesprächsverlauf nicht — alles Nötige steht in diesem Dokument und in den
verlinkten Dateien.

Begründungen und Alternativen: **`docs/PLAN-V2.md`**. Dieses Dokument sagt nur,
*was* zu tun ist und *woran* man erkennt, dass es fertig ist.

## Offene Aufräumarbeiten

Kleinkram, der **den Bau der nächsten Phase blockiert**. Steht hier etwas, wird
es zuerst erledigt. Erledigtes hier löschen, nicht abhaken.

_(nichts offen)_

Alles, was liegengeblieben ist, aber niemanden aufhält, steht am Dokumentende
unter **„Aufräumarbeiten zum Schluss"**. Dort wird es *nach* Phase 16
abgearbeitet, nicht vorher.

## Ablauf je Phase

0. **Aufräumliste oben abarbeiten**, falls dort etwas steht. Sie enthält nur,
   was blockiert; die Sammlung am Dokumentende bleibt bis nach Phase 16 liegen.
1. **Zustand lesen**: die erste Phase unten, die nicht `erledigt` ist.
2. **Arbeiten**: nur den Umfang dieser Phase. Kein Vorgriff auf spätere Phasen.
   Fällt unterwegs etwas auf, das nicht dazugehört → unter „Notizen" der Phase
   vermerken, nicht einbauen.
3. **Abnahme prüfen**: jeder Punkt unter „Abnahme" muss nachweislich erfüllt
   sein. Tests laufen lassen, Ausgabe zeigen. Kein „müsste passen".
4. **Eintragen**: Status auf `erledigt` setzen, Datum, kurze Notiz was
   abweicht.
5. **Committen**: `<typ>: <beschreibung>` nach `docs/`-Konvention, ein Commit
   je Phase, kein Push.
6. **Stoppen**, wenn die Phase ein **Tor** ist (siehe unten). Sonst weiter mit
   der nächsten.

## Tore — hier wird nicht ohne Freigabe weitergearbeitet

| Phase | Warum |
|---|---|
| 10 — Kopplung | Kryptografie und Autorisierung. Ein Fehler hier verschenkt die Kontrolle über den PC |
| 13 — Aktionen | Führt Programme und Skripte aus. Falsch gebaut ist es Remote-Code-Execution für jeden mit Zugang |
| 16 — Veröffentlichung | Außenwirkung: Lizenz, fremde Nutzer, Repo öffentlich |

Bei diesen Phasen nach Abschluss **anhalten und berichten**, statt die nächste
zu beginnen.

## Umgebung

| | |
|---|---|
| App-Tests | `cd app && npm test` — Stand 04.08.2026: **306 grün** |
| Agent-Tests | `cd agent.Tests && dotnet test` — Stand 01.08.2026: **310 grün** |
| Waker-Tests | `cd waker && npm test` — Stand 01.08.2026: **69 grün** |
| Kotlin-Tests | `cd clients/android/android && ./gradlew testDebugUnitTest` — Stand 03.08.2026: **9 grün** |
| Einrichtungs-Tests | `cd setup.Tests && dotnet test` — Stand 04.08.2026: **37 grün** |
| Windows-Client | `cd desktop && dotnet build` — baut auch auf Linux, läuft dort nicht |
| Android-Client | `cd clients/android && npm run apk` — baut eine echte APK; Toolchain-Einrichtung in `clients/android/README.md` |
| Typprüfung | `cd app && npx tsc -b` |
| Nicht vorhanden | Windows, echte Hardware. Android-SDK und JDK 21 wurden am 31.07.2026 nach `~/android-sdk` bzw. `~/.jdk` nachinstalliert |

Das .NET-SDK wurde am 30.07.2026 nach `~/.dotnet` nachinstalliert; `~/.bashrc`
setzt `PATH` und `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. Ohne das Zweite
stürzt jeder `dotnet`-Aufruf ab, weil im Container `libicu` fehlt und kein root
verfügbar ist. Das Projekt setzt ohnehin `<InvariantGlobalization>true`.

**Was hier nicht prüfbar ist, gilt nicht als erledigt, sondern als
`offen: Hardware`.** Das betrifft alles, was Windows, ein echtes Handy oder
einen Monitor braucht. Solche Punkte werden in der Phase notiert und
gesammelt — nicht stillschweigend abgehakt.

## Regeln, die für alle Phasen gelten

- **Bestehende Tests bleiben grün.** Wer einen Test ändert, begründet es in der
  Notiz der Phase. Tests werden nicht an den Code angepasst, sondern umgekehrt.
- **Kommentare erklären das Warum**, im Stil des vorhandenen Codes (deutsch,
  ganze Sätze, kein Kommentar der wiederholt was der Code sagt).
- **Keine Tokens, MACs, Tailnet-Namen** im Repo (`CLAUDE.md`).
- **Dateien unter 800 Zeilen**, Funktionen unter 50.
- Neue Logik kommt mit Tests. Ziel bleibt 80 % Abdeckung.

---

## Phase 9 — Plattform- und Transportschicht

**Status:** erledigt (30.07.2026)
**Tor:** nein
**Aufwand:** 3–4 Tage

Vorbereitung ohne sichtbare Änderung: die App bekommt zwei Abstraktionen, damit
später Android, Windows und ein anderer Transport eingehängt werden können,
ohne die getunte Eingabe- und Gestenlogik anzufassen.

### Umfang

- `app/src/platform/index.ts` — Schnittstelle für `storage`, `keystore`,
  `capabilities`, `update`, `clipboard`, `qr`
- `app/src/platform/web.ts` — heutige Umsetzung (localStorage, fetch, keine
  Kamera). Bildet den Ist-Zustand ab, nichts Neues
- `app/src/transport/index.ts` — Schnittstelle `control()`, `inputChannel()`,
  `screenStream()`
- `app/src/transport/direct.ts` — heutiger Weg: HTTPS + WSS an `host:8443`
- `agentClient.ts`, `inputChannel.ts`, `screenChannel.ts`, `webrtcChannel.ts`
  sprechen nur noch die Transport-Schnittstelle
- Geräteliste und Zugangsdaten aus dem lokalen Speicher statt vom Hub;
  `HubClient` wird ein Anbieter unter mehreren, nicht mehr der einzige Weg

### Nicht in dieser Phase

Kein `rtc.ts`, kein Capacitor, kein WebView2, keine Kopplung. Nur die Schnitte.

### Abnahme

- [x] `cd app && npm test` grün, **ohne dass ein bestehender Test geändert wurde**
      — 132 statt 108, kein `.test.ts` aus dem Bestand im Diff
- [x] `cd app && npx tsc -b` ohne Fehler — **falsch abgehakt**, siehe die
      Notizen zu Phase 10; ein Aufruf in `web.test.ts` war typfehlerhaft und
      wurde dort behoben
- [x] `agentClient.ts` und `inputChannel.ts` enthalten kein `fetch(` und kein
      `new WebSocket(` mehr — Grep über alle vier Client-Dateien ohne Treffer
- [x] Neue Tests für die Web-Umsetzung der Plattformschicht
      (`platform/web.test.ts`, 11 Tests; `transport/direct.test.ts`, 13 Tests)
- [x] Die PWA funktioniert unverändert (`npm run build` läuft durch)

### Notizen

- Die Netz-Zugriffe liegen jetzt in genau zwei Dateien: `transport/direct.ts`
  und `hubClient.ts`. Letzteres ist gewollt — der Hub ist einer von mehreren
  Geräte-Anbietern (`lib/deviceSources.ts`) und stirbt erst in Phase 14.
- **Das Coalescing in `inputChannel.ts` wurde nicht angefasst.** `pendingMove`
  und `flushPendingMove` stehen unverändert; getauscht ist nur, worüber
  gesendet wird. Damit gilt die Garantie weiter, dass ein Klick nie vor der
  Bewegung ankommt, die ihn positioniert.
- Der Transport wird als Konstruktor-Parameter mit Vorgabewert übergeben
  (`transport: Transport = directTransport(device)`). Dadurch blieben die
  bestehenden Aufrufstellen unverändert und die neuen Tests kommen ohne
  Netzwerk-Mocks aus.
- `platform/web.ts` sagt bei `camera`, `pointerLock`, `backgroundSession` und
  `selfUpdate` bewusst `false`. Das ist der Ist-Zustand der PWA, kein Versäumnis
  — Phase 11 und 12 setzen diese Fähigkeiten auf `true`.
- **Zwischenfall:** Der erste Anlauf wurde unterbrochen und hatte dabei in
  `docs/TASKS.md` fünf offene Hardware-Prüfpunkte aus den Phasen 1–8 gelöscht
  statt sie stehen zu lassen. Zurückgesetzt. Erledigt ist davon nichts.
- `app/tsconfig.tsbuildinfo` war ein Build-Artefakt im Ausgangs-Commit. Steht
  jetzt in `.gitignore` und ist ausgetragen.

---

## Phase 10 — Kopplung und Geräteschlüssel · **TOR**

**Status:** erledigt (31.07.2026)
**Aufwand:** 4–5 Tage

Ersetzt das eine geteilte Token durch pro Client erzeugte Schlüssel mit
Widerruf und Rechten. Grundlage für alles Weitere.

### Umfang

- Agent erzeugt beim ersten Start ein Schlüsselpaar (ECDSA P-256,
  `System.Security.Cryptography`, keine neue Abhängigkeit)
- `clients.json` neben `appsettings.json`: `id`, `label`, `publicKey`,
  `scopes`, `createdAt`, `lastSeenAt` — **niemals ein Klartext-Token**
- `POST /api/pair`: 6-stelliger Code, 5 Minuten gültig, **einmal** verwendbar
- `TokenAuth` → `ClientAuth`: mehrere zugelassene Schlüssel, Vergleich weiter in
  fester Zeit (`CryptographicOperations.FixedTimeEquals`)
- Challenge-Response beim Verbindungsaufbau
- Scopes: `screen`, `input`, `media`, `power`, `actions`, `wake`
- Widerruf einzelner Clients
- App: Kopplungsansicht (Hostname + Code eintippen; QR kommt in Phase 12)

### Abnahme

- [x] `cd agent.Tests && dotnet test` grün — **199 statt 137**, kein
      bestehender Test geändert
- [x] Tests belegen: Code läuft nach 5 Minuten ab
      (`PairingCodesTests.Nach_fuenf_Minuten_gilt_der_Code_nicht_mehr`) · Code
      funktioniert kein zweites Mal (`Ein_Code_funktioniert_kein_zweites_Mal`) ·
      falscher Scope wird abgelehnt
      (`ClientAuthTests.Ein_Sitzungstoken_oeffnet_nur_die_erlaubten_Pfade`) ·
      widerrufener Client wird abgelehnt
      (`PairingServiceTests.Ein_widerrufener_Client_kommt_nicht_mehr_herein`) ·
      Signaturprüfung schlägt bei manipulierter Challenge fehl
      (`Eine_manipulierte_Challenge_faellt_durch`)
- [x] `cd app && npm test` grün — **170 statt 132**
- [x] `grep -ri "token" agent/ --include=*.cs` zeigt keinen Pfad mehr, der ein
      Klartext-Token persistiert. Nachweis: von allen Schreibstellen
      (`grep -rn "File.Write\|WriteAllText\|Serialize"`) berührt keine ein
      Token. `clients.json` enthält nur öffentliche Schlüssel — dazu ein
      eigener Test (`ClientStoreTests.In_der_Datei_steht_kein_Klartext_Token`).
      Sitzungstokens liegen ausschließlich im Arbeitsspeicher
- [x] Der alte Token-Weg funktioniert weiter
      (`ClientAuthTests.Das_alte_Token_gilt_weiter_und_darf_alles`)

### Notizen

- **Anmeldung statt Dauer-Token.** Der Client holt per `POST
  /api/session/challenge` eine Zufallszahl, unterschreibt sie und bekommt gegen
  `POST /api/session` ein Sitzungstoken (12 h, nur im Arbeitsspeicher). Das
  Token geht danach wie bisher als `Authorization: Bearer` bzw. `?token=` mit —
  dadurch blieben `<img>`-Adressen und WebSockets unverändert, die ja keine
  eigenen Header setzen können.
- **Format der Unterschrift:** ECDSA P-256 über SHA-256, r und s hintereinander
  (IEEE P1363), *nicht* DER. Das ist, was die WebCrypto-API des Browsers
  liefert; .NET prüft ausdrücklich in diesem Format. Wer eine der beiden Seiten
  umstellt, bekommt eine Prüfung, die immer fehlschlägt.
- **Kopplungscode und Widerruf sind nur lokal erreichbar.** `POST
  /api/pair/code`, `GET /api/clients` und `DELETE /api/clients/{id}` antworten
  über das Netz mit 403. Über das Netz erreichbar wäre genau der Weg, den die
  Kopplung verhindern soll. Bis Phase 11 das Fenster bringt, gibt es den Code
  per `Invoke-RestMethod` an `localhost` — und im Log des Agents.
- **Scope-Zuordnung ist eine Whitelist.** Ein Pfad, der in `AgentScopes` nicht
  steht, wird abgelehnt statt durchgelassen. `actions` und `wake` sind als
  Rechte schon da, ihre Endpunkte kommen erst in Phase 13 bzw. 14.
- **`Agent:Token` ist jetzt freiwillig.** Fehlt es, startet der Agent trotzdem
  und lässt nur gekoppelte Clients herein. Vorher war es Startbedingung.
- **Die Geräteliste braucht den Hub nicht mehr.** `DeviceListView` und
  `Sidebar` nehmen `hub` als `undefined` hin; ohne Hub gibt es keinen
  Online-Zustand, und ein gekoppeltes Gerät steht als „gekoppelt" statt
  fälschlich als „offline" da — sonst wäre der Knopf gesperrt, obwohl der
  Rechner läuft.
- **Ein bestehender Test wurde geändert:** `app/src/platform/web.test.ts` rief
  `update.install()` ohne Argument auf, obwohl die Schnittstelle seit Phase 9
  eines verlangt. `npx tsc -b` war dadurch schon **vor** dieser Phase rot (am
  Commit `bba39e5` nachgestellt); die Abnahme von Phase 9 ist an dieser Stelle
  falsch abgehakt worden, vermutlich wegen eines veralteten
  `tsconfig.tsbuildinfo`. Der Aufruf bekommt jetzt ein `UpdateInfo` mit; die
  Behauptung des Tests ist unverändert.
- **Nicht gebaut, gehört zu späteren Phasen:** QR-Kopplung (Phase 12), ein
  Fenster zum Anzeigen des Codes (Phase 11), Dateirechte auf `agentkey.txt` per
  Windows-ACL.

---

## Phase 11 — Windows-Client

**Status:** erledigt (31.07.2026)
**Tor:** nein
**Aufwand:** 4–5 Tage

### Umfang

- Neues Projekt `desktop/` — WinForms + `Microsoft.Web.WebView2`
- Tray-Icon (schließt den offenen Punkt aus Phase 1), Fenster nur auf Wunsch
- Lädt das gebaute `app/dist`
- `app/src/platform/webview2.ts`: Pointer Lock, echte Tastatur, Zwischenablage
- Selbstverbindung sperren (`/api/info`-Hostname gegen `Environment.MachineName`)
- Fehlt die WebView2-Runtime: verständliche Meldung mit Download-Link
- Lokale Verwaltung im Fenster: Kopplungscode anzeigen, Clients widerrufen

### Abnahme

- [x] `dotnet build` für `desktop/` läuft durch — **0 Warnungen, 0 Fehler**
- [x] `cd app && npm test` grün — **205 statt 170**, kein bestehender Test
      geändert (`git diff HEAD --stat -- "*.test.*"` leer)
- [x] Selbstverbindungssperre hat einen Test — `lib/selfConnection.test.ts`,
      11 Tests: gleicher Name greift, Groß-/Kleinschreibung und
      Domänen-Suffix egal, ähnlicher Name greift nicht, ohne eigenen
      Rechnernamen wird nichts gesperrt
- [~] Teilweise bestätigt am 31.07.2026: der Client startet, das
      Kopplungsfenster zeigt Code und QR, die Selbstverbindungssperre greift
- [ ] `offen: Hardware` — tatsächlicher Start unter Windows, Tray, Pointer Lock

### Notizen

- **`desktop/` referenziert `agent/` nicht**, anders als in `PLAN-V2.md`,
  Abschnitt 2 skizziert. Der Agent ist `Sdk.Web` mit `RuntimeIdentifier`
  `win-x64` und `SelfContained`; ein Verweis darauf zöge SIPSorcery, Vortice
  (Direct3D) und System.Drawing in eine reine Fensterhülle und brächte die
  RID-Einstellungen durcheinander. Gebraucht wird von dort nichts — die einzige
  Berührung ist HTTP über die Loopback-Adresse.
- **Die Oberfläche ist kein Teil des .NET-Builds.** `app/dist` wird per
  `Content Include` mitkopiert; wer `npm run build` vergisst, bekommt beim
  Öffnen des Fensters einen Satz statt einer leeren Fläche.
- **Ausgeliefert wird unter einer erfundenen https-Adresse**
  (`app.remotedesktop.invalid` per `SetVirtualHostNameToFolderMapping`), nicht
  über `file://`. Unter `file://` hat Chromium eine eigene, sehr enge Herkunft —
  localStorage und die Anfragen an den Agent würden dort scheitern.
- **Die echte Tastatur hängt an `KeyboardEvent.code`, nicht an `key`.** Sonst
  wäre aus Strg+Alt+Q auf einer deutschen Tastatur ein `@` geworden. Anschläge
  in Eingabefeldern der App bleiben dort (`belongsToRemote`), sonst ließe sich
  kein Kopplungscode mehr eintippen.
- **Kein C#-Testprojekt für `desktop/`.** Das Projekt ist eine WinForms-Hülle;
  ein Testprojekt, das es referenziert, ließe sich hier nicht *ausführen*, weil
  zum Laden der Assembly die WinForms-Laufzeit nötig ist. Die Logik, die
  Prüfung verdient, liegt deshalb in der App und ist dort getestet.
  `WebAppLocator` und `WebView2Runtime` bleiben ungeprüft — beide sind
  Pfad- bzw. API-Abfragen, die ohne Windows nichts aussagen.
- **`MSB3277` ist auf „Meldung" gesetzt.** Das WebView2-Paket bringt neben der
  WinForms- auch eine WPF-Hülle mit, die eine andere `WindowsBase`-Fassung
  verlangt. Geladen wird sie nie; ohne die Herabstufung rauscht bei jedem Build
  ein zwölfzeiliger Warnblock durch.
- **Aufgefallen, nicht gebaut (spätere Phasen):** Die PWA registriert im
  Fenster ihren Service Worker. Nach einem Update der Oberfläche kann er kurz
  die alte Fassung ausliefern — das gehört zu Phase 14 (Updates), nicht hierher.
  Ebenso: ein eigenes Tray-Symbol statt `SystemIcons.Application`, und die
  DPAPI als echter Schlüsselspeicher statt localStorage.

---

## Phase 12 — Android-APK

**Status:** erledigt (31.07.2026)
**Tor:** nein
**Aufwand:** 3–4 Tage plus Gerätetests

### Umfang

- Capacitor-Projekt unter `clients/android/`
- `app/src/platform/capacitor.ts`: Preferences statt localStorage, Kamera
- Foreground-Service (`connectedDevice`) für die laufende Sitzung
- QR-Scanner für die Kopplung aus Phase 10

### Abnahme

- [x] `cd app && npm test` grün — **244 statt 205**, kein bestehender Test
      geändert (`git diff HEAD --stat -- "*.test.*"` leer). `npx tsc -b --force`
      ohne Fehler, `npm run build` läuft durch
- [x] Capacitor-Konfiguration vorhanden und in sich stimmig — `npx cap sync
      android` findet die drei Plugins und kopiert `app/dist`; `appId`,
      Java-Paket, `namespace` und `applicationId` lauten alle
      `app.remotedesktop.client`; die vier Namen in `registerPlugin<…>()` decken
      sich mit `capacitor.plugins.json` bzw. mit `@CapacitorPlugin(name =
      "SessionService")`
- [x] Neue Tests: `platform/capacitor.test.ts` (27) und `lib/pairingUri.test.ts`
      (12). Der Test „bleibt stumm, wenn das Schreiben scheitert" greift
      tatsächlich — ohne das `.catch` in `persist()` endet `npm test` mit 1
- [x] `cd agent.Tests && dotnet test` weiterhin 199 grün (nichts am Agent
      angefasst)
- [x] APK bauen — **erledigt am 31.07.2026 im Container**, nicht am Windows-
      Rechner. `npm run apk` ergibt eine 34-MB-Debug-APK. Geprüft am Ergebnis:
      `minSdkVersion=26`, `targetSdkVersion=36`, `SessionService` mit
      `foregroundServiceType=0x1` (= `DATA_SYNC`, siehe Notizen), alle drei eigenen
      Klassen im DEX, debug-signiert und damit installierbar
- [x] Verhalten beim Wegwischen — **am 31.07.2026 am Gerät bestätigt**:
      minimieren und über die Benachrichtigung zurück hält die Sitzung;
      Wegwischen beendet Sitzung und Benachrichtigung. Beides erst nach den drei
      Nachträgen unten, die dieser Lauf gefunden hat
- [ ] `offen: Hardware` — Gesten, Bildschirmtastatur und H.264-Latenz am Handy
      beurteilen. Der Vergleich gegen die PWA entfällt auf Wunsch: es zählt, ob
      die APK für sich brauchbar ist

### Notizen

- **Der Speicher ist synchron, die Brücke nicht.** `KeyValueStore.get()` gibt
  seit Phase 9 direkt einen Wert zurück, die Preferences antworten mit einer
  Zusage. Aufgelöst über einen Abzug im Arbeitsspeicher: `main.tsx` liest vor
  dem ersten Rendern alle Schlüssel ein, danach wird synchron daraus geantwortet
  und nebenher geschrieben. Die Schnittstelle asynchron zu machen wäre der
  ehrlichere, aber teurere Weg gewesen — er hätte jede Ansicht angesteckt, die
  `storage.getDevices()` liest, und dafür bestehende Tests gekostet.
- **`main.tsx` startet React jetzt erst nach `await`.** Ohne das stünde beim
  ersten Rendern ein leerer Speicher da und die App fragte nach dem Hub-Token,
  obwohl Geräte gekoppelt sind.
- **Die App spricht die Plugins über ihren Namen an, nicht über deren
  TypeScript-Aufsätze.** `app/` bekommt dadurch nur `@capacitor/core` als
  Abhängigkeit — und die wird dynamisch geladen, sodass sie im PWA-Bündel als
  eigener 7,7-kB-Brocken liegt, den der Browser nie holt. Über die Aufsätze wäre
  der Web-Ersatz des QR-Scanners samt `html5-qrcode` mitgekommen. Preis dafür:
  die Optionen des Scanners stehen in `SCAN_OPTIONS` ausgeschrieben, weil sonst
  der JS-Aufsatz sie ergänzt hätte.
- **Java statt Kotlin für den Dienst.** Kotlin verlangte das Gradle-Plugin und
  eine Version, die zur AGP-Fassung passt — beides hier nicht kompilierbar und
  damit nicht prüfbar. Der Vordergrunddienst ist reines Boilerplate; die
  Vorlage von `cap add android` ist Java. Der Kotlin-Anteil steht in der Abnahme
  von Phase 15, dort wird die Toolchain mit dem Widget zusammen eingezogen.
- **Vordergrunddienst-Fehler werden gemeldet, nicht geschluckt.** Startet er
  nicht, läuft die Sitzung trotzdem — sie stirbt nur beim Wegwischen. Genau das
  steht dann im Fehlerbanner. Beim Beenden wird der Fehler verworfen: dort ist
  nichts mehr zu retten.
- **Der Schlüsselspeicher ist dieselbe Ablage wie der gewöhnliche Speicher.**
  Android hätte `EncryptedSharedPreferences`, das wäre ein weiteres Plugin und
  eine zweite Ablage, aus der beim Wechsel des Sperrbildschirms Schlüssel
  verschwinden können. Die Preferences sind app-privat und ohne Root für andere
  Apps unlesbar — mehr als der localStorage eines Browsers, weniger als der
  Keystore. Wenn das nicht reicht, ist es eine eigene Entscheidung, keine
  Nebensache von Phase 12.
- **Der QR-Scan füllt nur die Felder aus und koppelt nicht von selbst.** Der
  Name, unter dem das Handy am Rechner erscheint, steht danach noch zur
  Änderung, und ein falsch erwischter Code fällt vor dem Absenden auf.
- **`SessionKeepAlive` steht in `platform/session.ts`,** nicht in `index.ts` —
  wie `PlatformError` in `errors.ts`, damit die Umsetzungen den Vorgabewert
  `noSessionKeepAlive` benutzen können, ohne einen Ringschluss zu bauen.
- **Zwei leere Vorlagendateien entfernt:** `ExampleUnitTest.java` und
  `ExampleInstrumentedTest.java` aus `com.getcapacitor.myapp`. Sie kamen aus der
  Vorlage und nennen ein Paket, das es hier nicht gibt.
- **Nachtrag 31.07.2026: Der Vordergrunddienst ist `dataSync`, nicht
  `connectedDevice`.** Auf dem Gerät stürzte die App bei jedem Verbindungsversuch
  ab. Seit Android 14 verlangt jeder Dienst-Typ eine Vorbedingung; für
  `connectedDevice` ist das eine Erlaubnis aus Bluetooth, NFC,
  `CHANGE_NETWORK_STATE`, `CHANGE_WIFI_STATE`, UWB oder USB. Die App hält keine
  davon, `startForeground` warf eine `SecurityException`, und die nahm aus
  `onStartCommand` heraus den ganzen Prozess mit. Eine dieser Erlaubnisse nur
  zum Freischalten des Typs zu erklären wäre ein Feigenblatt gewesen —
  übertragen wird fortlaufend Bild und Eingabe über das Netz, und dafür ist
  `dataSync` da. Der Umfang von Phase 12 nennt `connectedDevice`; das war unter
  Android 14 so nicht haltbar.
- **Nachtrag 31.07.2026: Der Dienst geht mit, wenn die App weggewischt wird.**
  Ohne `onTaskRemoved` blieb die Benachrichtigung stehen und behauptete eine
  Verbindung, die es nicht mehr gab — die Aufräumroutine in `App.tsx` greift dort
  nicht, weil es das JavaScript in diesem Augenblick nicht mehr gibt.
- **Nachtrag 31.07.2026: Der Dienst kann die App nicht mehr mitreißen.** Start
  und Stopp sind jetzt in `SessionService.onStartCommand` und in
  `SessionServicePlugin` gekapselt; scheitert der Dienst, meldet die App es im
  Fehlerband und die Sitzung läuft weiter — sie überlebt dann nur das Wegwischen
  nicht. Das ist der eigentliche Fehler gewesen: eine Bequemlichkeit durfte die
  ganze App beenden, und zwar reproduzierbar bei jedem Verbindungsversuch.
- **Nachtrag 31.07.2026: `minSdk` von 24 auf 26 angehoben.** Der erste echte
  Gradle-Lauf brach ab — der QR-Scanner bringt `io.ionic.libs:ionbarcode-android`
  mit, das mindestens 26 verlangt, und der Manifest-Merger lässt das nicht durch.
  Das war ohne SDK nicht zu sehen. Android 8 ist von 2017; Geräte darunter kommen
  für eine Fernsteuerung ohnehin nicht in Frage. Zwei dadurch tot gewordene
  Zweige in `SessionService.java` sind entfallen (Kanal-Anlage und
  `ContextCompat.startForegroundService` waren beide nur für < 26 nötig).
- **Aufgefallen, nicht gebaut (spätere Phasen):** Der Service Worker der PWA
  landet mit in der APK (`sw.js` unter den Assets). Nach einem Update kann er
  kurz die alte Oberfläche ausliefern — dieselbe Sache, die schon in Phase 11
  fürs Windows-Fenster notiert ist, und sie gehört zu Phase 14. Ebenso:
  `selfUpdate` steht auf `false`, bis der `PackageInstaller` da ist, und ohne
  Release-Keystore gibt es nur `assembleDebug`.
- **Nicht gebaut, gehört zu keiner Phase:** die Anzeige des QR-Codes im
  Windows-Fenster. Steht oben unter „Offene Aufräumarbeiten".

---

## Phase 13 — Aktionen am Agent · **TOR**

**Status:** erledigt (31.07.2026)
**Aufwand:** 4 Tage

**Die eine Regel: deklariert wird am Agent, aufgerufen wird per ID.** Der Client
schickt nie eine Kommandozeile. Details in `docs/PLAN-V2.md`, Abschnitt 5.

### Umfang

- `actions.json` neben `appsettings.json`, **beim Start validiert**: unbekannter
  `type`, fehlende Datei, `args` als Zeichenkette statt Array → Abbruch mit
  Klartext, nicht erst beim Auslösen
- Typen: `process`, `script`, `keys`, `url`, `sequence`
- `GET /api/actions` (lesend, über Netz) · `POST /api/actions/{id}/invoke`
- **Kein Schreib-Endpoint über das Netz.** Bearbeitet wird nur lokal im Fenster
  aus Phase 11
- `confirm`-Merker, den der Client als Rückfrage umsetzt

### Abnahme

- [x] `cd agent.Tests && dotnet test` grün — **254 statt 212**, kein bestehender
      Test geändert. `cd app && npm test` **249 statt 244**, `npx tsc -b` sauber
- [x] Tests belegen: `args` wird als Array übergeben, **nie** über eine Shell ·
      `script` startet nur hinterlegte Dateien · unbekannte ID → 404, nicht 500 ·
      Sequenzen halten ihre Verzögerungen ein — `ActionCatalogTests` (24),
      `ActionRunnerTests` (13), `ActionEndpointTests` (5). Der Test „Argumente
      gehen einzeln hinaus" prüft ausdrücklich, dass
      `ProcessStartInfo.Arguments` **leer** bleibt und `& calc.exe` als *ein*
      Argument ankommt statt als zweiter Befehl
- [x] `grep -rn "UseShellExecute = true" agent/` findet nichts — Rückgabewert 1.
      Alle fünf Fundstellen von `UseShellExecute` stehen auf `false`, die sechste
      ist der Kommentar in `ActionRunner.cs`, der erklärt warum
- [x] Kein Endpoint, der `actions.json` über das Netz beschreibt — in
      `ActionEndpoints.cs` gibt es genau ein `MapPost`, und das endet auf
      `/invoke`. `grep -rn "WriteAllText\|File.Create\|OpenWrite" agent/` trifft
      nur `SelfUpdater`, `AgentIdentity` und `ClientStore`. Zusätzlich fährt
      `ActionEndpointTests` PUT, POST und DELETE gegen `/api/actions` und
      `/api/actions/backup` und verlangt 404 oder 405 — damit fällt auch ein
      später versehentlich ergänzter Schreibweg auf
- [x] Nicht gefordert, aber gebaut: die App-Seite (`ActionsView.tsx`,
      `agentClient.getActions/invokeAction`, Eintrag in der Sidebar). Ohne sie
      hätte Phase 15 nichts, worauf sie ihr Widget setzen könnte, und der
      `confirm`-Merker hätte keinen Abnehmer

### Notizen

- **Zwei Abweichungen vom Beispiel im Plan, beide bewusst:**
  - `type: "url"` läuft über `explorer.exe <adresse>` und **nicht** über
    `UseShellExecute = true`. Letzteres wäre der bequeme Weg zum
    Standardbrowser und zugleich die einzige Stelle im ganzen Agent, an der
    Windows entscheidet, was eine Zeichenkette bedeutet. Die Abnahme verlangt
    ausdrücklich, dass es diese Stelle nicht gibt. Der Katalog lässt dafür nur
    `http` und `https` durch — `file:`, `ms-settings:` und `javascript:` wären
    sonst ein zweiter Weg, Beliebiges zu starten.
  - `"chord": ["LWin", "P"]` aus dem Plan wurde von `VirtualKeys` abgelehnt: dort
    stand nur `win` und `meta`. `lwin` als Alias ergänzt — dieselbe Taste, und
    in einer von Hand geschriebenen `actions.json` schreibt man sie so, wie
    Windows sie nennt. Bestehende `VirtualKeysTests` unverändert grün.
- **Zusätzliche Prüfungen, die der Umfang nicht nannte,** aber ohne die der
  Rest wenig wert wäre: Kennungen müssen `^[a-z0-9][a-z0-9-]*$` erfüllen (sie
  stehen im Pfad von `/invoke`), dürfen nicht doppelt vorkommen, `script`
  verlangt eine `.ps1`-Endung (sonst wäre der Typ ein zweiter Weg zu einer
  beliebigen `.exe`), Sequenzen dürfen keinen Kreis bilden (sonst liefen sie
  endlos), und Pausen liegen zwischen 0 und 60000 ms.
- **`GET /api/actions` gibt keine Pfade heraus** (`ActionSummary` statt
  `AgentAction`). Wer die Liste abfragen darf, muss nicht auch erfahren, welche
  Software auf dem Rechner liegt und wo. Ein Test prüft das am serialisierten
  JSON.
- **Fehler beim Auslösen gehen ins Log, nicht in die Antwort.** Die Rohmeldung
  nennt Pfade; die Antwort sagt nur, dass es nicht ging.
- **`ActionCatalog` und `ActionRunner` sind getrennt.** Der Katalog prüft beim
  Start, der Läufer entscheidet nichts mehr. Die Prüfungen bekommen
  `fileExists` bzw. das Warten hereingereicht — anders ließe sich hier ohne
  Windows weder eine fehlende Datei noch eine eingehaltene Pause belegen.
- **`IActionHost`** kapselt `Process.Start` und die Tastenausgabe. Nur dadurch
  lässt sich prüfen, *wie* gestartet wird; ohne diese Naht wäre die wichtigste
  Zusage der Phase behauptet statt belegt.
- **Neue Testabhängigkeit `Microsoft.AspNetCore.TestHost` (8.0.11).** Der
  404-Punkt der Abnahme steht am Endpunkt und nicht am Katalog — ohne
  Testserver wäre er nur behauptet gewesen. Der Server läuft ohne Zertifikat
  und ohne Windows, weil `ActionEndpoints` nur den Katalog und den Läufer
  braucht. Nebeneffekt: die Zusage „keine Pfade über das Netz" wird jetzt am
  echten JSON der Antwort geprüft, nicht am serialisierten Objekt.
- **Nicht gebaut, gehört zu keiner Phase:** ein Editor für `actions.json` im
  Windows-Fenster. Der Umfang nennt ihn („Bearbeitet wird nur lokal im Fenster
  aus Phase 11"), die Abnahme verlangt nur, dass es keinen Schreibweg über das
  Netz gibt — und den gibt es nicht. Bearbeitet wird die Datei heute mit einem
  Texteditor, was ebenfalls lokal ist. Steht oben unter „Offene
  Aufräumarbeiten".
- **Aufgefallen, nicht gebaut (spätere Phasen):** Die `actions.json` wird beim
  Start gelesen. Wer sie ändert, muss den Agent neu starten. Ein Nachladen zur
  Laufzeit wäre bequem, verschiebt aber die Prüfung wieder auf einen Zeitpunkt,
  an dem niemand hinsieht — das gehört zusammen mit dem Editor entschieden.
- **`elevated: true` gibt es nicht,** wie im Plan festgelegt: es öffnete ein
  UAC-Fenster auf dem Sperrdesktop, das aus der Ferne niemand bestätigen kann.

---

## Phase 14 — Updates über GitHub, Hub zu `waker`

**Status:** erledigt (01.08.2026)
**Tor:** nein
**Aufwand:** 5 Tage

### Umfang

- `SelfUpdater` auf GitHub Releases statt Hub; ECDSA-P-256-Signaturprüfung des
  Manifests, öffentlicher Schlüssel im Agent kompiliert
- `POST /api/update`, `version` und `protocol` in `/api/info`
- Android: Versionsprüfung, Download, `PackageInstaller`
- WOL als Netz-Fähigkeit: `wol.ts` nach C#, `POST /api/wol`, `canWake`
- **`siteId = sha256(gatewayMac)`** in Agent und Waker; der Client sucht den
  Waker selbst (`docs/PLAN-V2.md`, Abschnitt 3, „Mehrere Standorte, ein Knopf")
- `git mv hub/ waker/`: PWA-Auslieferung, Registry und `agentRelease.ts` raus.
  Übrig: WOL, `siteId`, Kopplung. Multi-Arch-Image
- `.github/workflows/release.yml`
- **Die App muss einen Neustart des Agents überstehen.** Heute bleibt sie
  getrennt und muss selbst neu gestartet werden (am 31.07.2026 auf dem Gerät
  gesehen). Ein Selbst-Update startet den Agent bei *jedem* Durchlauf neu —
  ohne selbsttätiges Wiederverbinden wäre die Funktion unbrauchbar. Betroffen
  ist `lib/inputChannel.ts` (der Kanal, an dem der Verbindungszustand hängt) und
  die Sitzung darüber: nach einem Neustart ist das alte Sitzungstoken ungültig,
  es muss also neu über `/api/session/challenge` angemeldet werden

### Abnahme

- [x] Beide Testläufe grün — Agent **310 statt 259**, App **290 statt 249**,
      dazu der neue Waker mit **69**. `npx tsc -b --force` sauber,
      `npm run build` läuft durch, `desktop/` baut mit 0 Warnungen. **Ein**
      bestehender Test wurde geändert, siehe Notizen
- [x] Tests belegen: manipuliertes Manifest wird abgelehnt
      (`ManifestVerifierTests.Ein_manipuliertes_Manifest_faellt_durch`, dazu
      fremder Schlüssel, fehlende Unterschrift und der Auslieferungszustand
      ohne Schlüssel) · gleiche `siteId` wird gefunden, fremde nicht
      (`wake.test.ts`: „ein Waker mit derselben Kennung wird gefunden" /
      „ein Waker aus einem fremden Netz wird nicht gefunden") · kein Kandidat →
      Knopf aus, kein Fehler („ohne Kandidat gibt es keinen Knopf und keinen
      Fehler"; in `DeviceListView` ist der Knopf dann `disabled` mit der
      Begründung aus `explainMissingCandidate`)
- [x] Tests belegen: nach einem Abriss meldet sich der Client neu an
      (`inputChannel.reconnect.test.ts`: „ein Versuch, der nie zustande kam,
      führt zu einer neuen Anmeldung" — und die Gegenprobe „eine stehende
      Verbindung wird nicht grundlos neu angemeldet") · die Wiederholversuche
      geben auf („die Wiederholversuche geben irgendwann auf", „nach dem
      Aufgeben wird nicht weiter versucht", „ein Netzwechsel setzt den Zähler
      zurück")
- [x] `waker/` enthält keine Geräteliste mehr — `grep -rn "devices" waker/src`
      trifft nur Kommentare, einen Testnamen und die Zeile in `config.ts`, die
      erklärt, dass es sie nicht mehr gibt. Die sieben Routen sind `/info`,
      `/wol`, `/pair/code`, `/pair`, `/session/challenge`, `/session`,
      `/clients/{id}`. `routes.test.ts` verlangt für `/api/devices`,
      `/api/devices/status` und `/api/wol/pc` ausdrücklich 404 — damit fällt
      auch eine später versehentlich ergänzte Liste auf
- [x] Nicht gefordert, aber gebaut: die APK baut mit dem neuen
      `AppUpdate`-Plugin durch (`npm run apk`, 35 MB);
      `REQUEST_INSTALL_PACKAGES` steht im Manifest der fertigen Datei, und
      `AppUpdatePlugin` wie `ApkInstaller` liegen im DEX
- [ ] `offen: Hardware` — echter Weckvorgang, Selbst-Update auf dem PC, und der
      Agent-Neustart bei laufender App: die Sitzung muss von allein
      zurückkommen

### Notizen

- **Der Release-Schlüssel ist absichtlich leer.** `ReleaseKeys.PublicKey` steht
  im Repo auf `""`, und damit ist das Selbst-Update aus — der Agent sagt das
  beim Start im Log. Anders geht es nicht: der öffentliche Schlüssel muss
  einkompiliert sein, und den privaten dazu darf es hier nirgends geben. Wer
  Releases baut, erzeugt das Paar einmal mit `node scripts/release-key.mjs`.
- **Node unterschreibt, .NET prüft — das ist geprüft.**
  `ManifestVerifierTests.Ein_Manifest_aus_dem_Signierskript_wird_angenommen`
  hält Bytes und Unterschrift, die tatsächlich aus `scripts/sign-manifest.mjs`
  stammen. Beide Seiten müssen IEEE P1363 benutzen (r und s hintereinander,
  nicht DER); ohne diesen Test fiele eine Abweichung erst beim ersten echten
  Release auf — und dort sähe sie aus wie ein Angriff. Dasselbe für die
  Standort-Kennung: `SiteIdentityTests.Der_Agent_rechnet_dieselbe_Kennung_wie_der_Waker`
  und `waker/src/site.test.ts` („rechnet genauso wie der Agent") halten
  denselben festen Hashwert.
- **`/api/update` bekam kein eigenes Recht, sondern `power`.** Ein Update
  tauscht den Agent aus und startet ihn neu — derselbe Eingriff, den `power`
  ohnehin erlaubt, nur harmloser. Ein siebtes Recht hätte jedes bereits
  gekoppelte Gerät ausgesperrt, weil in dessen `clients.json`-Eintrag nur die
  sechs von damals stehen.
- **Der Neustart wird am geschlossenen Kanal erkannt, nicht an einem
  Statuscode.** Ein WebSocket bekommt keinen — er wird einfach zugemacht.
  `InputChannel` merkt sich deshalb, ob ein Versuch je zustande kam: nur wenn
  nicht, wird der Ausweis verworfen und neu angemeldet. Bei jedem Abriss neu
  anzumelden wäre eine unnötige Runde und bei jedem WLAN-Zucken spürbar.
  Aufgegeben wird nach zehn Versuchen (mit Verdopplung gut eine Minute) —
  endlos weiterzuversuchen hieße, für immer auf „verbinde…" zu stehen, während
  der Rechner in Wahrheit aus ist.
- **Der Waker hat die Kopplung des Agents komplett nachgebaut** — Code,
  öffentlicher Schlüssel, Challenge-Response, Sitzungstoken, Widerruf, nur in
  Node. Er musste identisch sein: die App unterschreibt mit demselben Schlüssel
  und denselben Aufrufen, egal ob am anderen Ende ein PC oder die NAS steht.
- **Der Waker braucht jetzt ein eigenes Zertifikat.** Das ist beim Umbau
  aufgefallen und stand so nicht im Umfang: der Hub lieferte die PWA aus, also
  war er dieselbe Herkunft. Die APK läuft unter `https://localhost`, das
  Windows-Fenster unter `https://app.remotedesktop.invalid` — von dort lässt
  kein Browser eine `http://`-Anfrage durch. Der Waker terminiert deshalb
  selbst (`CERTIFICATE_PATH` / `KEY_PATH` aus `tailscale cert`) und setzt
  CORS-Kopfzeilen wie der Agent. Ohne Zertifikat startet er trotzdem und sagt
  es im Log.
- **Mit dem Hub ist auch der Online-Zustand umgezogen.** Er kam aus
  `GET /api/devices/status`; ohne Geräteliste gibt es das nicht mehr. Die App
  fragt jetzt selbst `/health` an jedem Knoten (`lib/reachability.ts`) — der
  einzige Endpunkt ohne Ausweis. Das ist ohnehin die ehrlichere Auskunft: dass
  die NAS einen Rechner erreicht, heißt nicht, dass das Handy es auch tut.
- **Die Hub-Token-Abfrage beim Start ist ersatzlos weg**, dazu `hubClient.ts`,
  `hubDeviceSource` und `storage.getHubToken`. Der Einstieg ist jetzt für jeden
  Rechner derselbe: koppeln. Damit erledigt sich der Punkt „`hubClient.ts`
  benutzt relative Pfade" aus den Aufräumarbeiten am Dokumentende — die Datei
  gibt es nicht mehr.
- **Ein bestehender Test wurde geändert:** `capacitor.test.ts` hieß „Selbst-
  Update steht **bis Phase 14** auf false" und prüfte genau den Zustand, den
  diese Phase ablöst. Der Test hält jetzt fest, dass die APK es kann; die
  Begründung im Testkörper ist neu (ein Knopf und ein Systemdialog). Sonst
  wurde kein `.test.*` aus dem Bestand angefasst — die vier gelöschten
  Testdateien im Waker (`config.test.ts`, `auth.test.ts`, `probe.test.ts`)
  prüften Code, den diese Phase entfernt: Gerätekonfiguration, Hub-Token und
  den Online-Check der Registry. `wol.test.ts` steht unverändert.
- **Die Android-APK prüft keine eigene Signatur** — sie braucht keine. Android
  lässt eine APK nur über eine installierte drüber, wenn sie mit demselben
  Schlüssel unterschrieben ist, und zeigt außerhalb von Google Play immer einen
  Bestätigungsdialog. Beides zusammen ist stärker als eine selbstgebaute
  Prüfung. Verglichen wird deshalb nur die Fassung gegen den Release-Tag;
  `versionName` und `versionCode` kommen im CI aus dem Tag statt aus der
  `build.gradle`.
- **`docs/SICHERHEIT.md` fortgeschrieben**, wie der Umfang es verlangt: die
  Token-Bündelung des Hubs steht jetzt als behobener Befund mit Schwere *hoch*
  darin, dazu die Signaturkette des Updates, die bewusst schwache Absicherung
  des Weckens und was zu tun ist, wenn der Release-Schlüssel abhandenkommt.
  `docs/ARCHITEKTUR.md` und `CLAUDE.md` nennen jetzt den Waker.
- **Nicht gebaut, gehört zu keiner Phase:** ein Weg, einen Waker zu koppeln,
  ohne dessen Kopplungscode auf der NAS von Hand zu holen. Steht oben unter
  „Offene Aufräumarbeiten" nicht — es blockiert nichts und ist einmalig.
- **Nachtrag 03.08.2026: Das Selbst-Update der APK hat keinen Aufrufer.** Am
  Gerät kommt kein Hinweis und keine Rückfrage — zu Recht: `grep -rn
  "update\.check\|\.update\." app/src --include=*.tsx` findet für die App
  keine einzige Stelle. Der ganze Unterbau steht (`platform/appUpdate.ts`,
  `updateService` in `capacitor.ts`, nativ `AppUpdatePlugin` und `ApkInstaller`,
  `REQUEST_INSTALL_PACKAGES` im Manifest) und ist geprüft; es fehlt genau das
  Stück dazwischen. Beim Agent gibt es den Knopf (`PowerView.tsx` ruft
  `agent.update()`), bei der APK nie. Die Abnahme dieser Phase hat das auch
  nicht behauptet — sie belegt nur, dass die APK *mit* dem Plugin durchbaut.
  Zwei weitere Dinge stünden auch dann noch im Weg: es gibt **kein Release**
  (`git remote -v` leer, `git tag` leer — `findLatestApk` liefert korrekt
  `undefined`), und die installierte APK ist **debug-signiert**, während der
  Workflow mit dem Release-Keystore signiert. Android lässt eine APK nur über
  eine installierte, wenn beide denselben Schlüssel tragen; die erste
  Release-Fassung muss also einmal von Hand installiert werden. Das
  Verdrahten der Oberfläche steht unten unter „Aufräumarbeiten zum Schluss",
  der Rest bei den Hardware-Punkten.
- **Aufgefallen, nicht gebaut (spätere Phasen):** `screenChannel.ts` verbindet
  nach einem Abriss nicht selbsttätig neu; nach einem Agent-Neustart kommt die
  Eingabe zurück, das Bild braucht einen Wechsel der Ansicht. Das gehört zum
  Bild-Weg und nicht zur Sitzungslogik dieser Phase. Ebenso: der Dockhand-Stack
  auf der NAS zeigt noch auf `hub/Dockerfile` und muss beim nächsten Deploy auf
  `waker/Dockerfile` umgestellt werden — das ist Hardware-Arbeit.

---

## Phase 15 — Android-Flächen

**Status:** erledigt (03.08.2026)
**Tor:** nein
**Aufwand:** 4–5 Tage

Widget mit Aktionsraster, Quick-Settings-Tile, dynamische App-Shortcuts.
Weckknopf nur aktiv, wenn ein Kandidat mit passender `siteId` erreichbar ist.

### Abnahme

- [x] Kotlin-Anteile vorhanden und in sich stimmig — zehn Klassen unter
      `clients/android/android/app/src/main/java/app/remotedesktop/client/surfaces/`,
      alle im DEX der gebauten APK. `./gradlew assembleDebug testDebugUnitTest`
      läuft ohne Fehler und ohne Warnung durch; im Manifest der fertigen APK
      stehen `ActionWidget` (Receiver, nicht freigegeben, mit
      `APPWIDGET_UPDATE` und `@xml/widget_actions_info`), `WakeTile` (Dienst,
      freigegeben, `BIND_QUICK_SETTINGS_TILE`) und `ShortcutRelay` (Aktivität,
      nicht freigegeben)
- [x] Neuer Kotlin-Testlauf: **9 grün** (`SignaturesTest` 5,
      `SurfaceBoardTest` 4). Die wichtigste Zusage steht darin: was der native
      Teil unterschreibt, nimmt der Agent an — geprüft gegen den
      P1363-Prüfer der JVM, weil .NET dasselbe Format verlangt
- [x] `cd app && npm test` **301 statt 290**, kein bestehender Test geändert
      (`git diff HEAD --stat -- "*.test.*" "*Tests.cs"` leer). `npx tsc -b`
      sauber, `npm run build` läuft durch. Agent weiterhin **310**, Waker **69**
      (an beiden nichts angefasst)
- [x] Der Weckknopf ist nur da, wenn wirklich jemand wecken kann: die Kachel
      fragt beim Aufklappen erst das Ziel und dann den Boten mit `/health` und
      steht sonst auf `STATE_UNAVAILABLE` samt Begründung. Die Auswahl des
      Boten nach `siteId` macht weiterhin `lib/wake.ts` — sie ist nicht
      zweitgeschrieben, siehe Notizen
- [ ] `offen: Hardware` — Widget, Kachel und Kürzel auf dem Gerät: erscheinen
      sie in der Auswahl, lösen sie aus, kommt bei einem Fehlschlag die Meldung

### Notizen

- **Die Flächen laufen ohne die App — das ist die ganze Schwierigkeit.** Wer
  ein Widget antippt, hat keine WebView, kein React und keinen Speicher, aus
  dem sich etwas lesen ließe. Der native Teil muss sich also selbst am Agent
  anmelden: Challenge holen, mit dem Geräteschlüssel unterschreiben,
  Sitzungstoken holen, auslösen. Drei Anfragen je Tipp, kein gemerktes Token —
  für das gäbe es keinen Ort, an dem es sicherer läge als der Schlüssel, aus
  dem man sich jederzeit ein neues holt.
- **Die Aufteilung: TypeScript entscheidet *wer*, Kotlin entscheidet *jetzt*.**
  Die App legt beim Verbinden einen Steckbrief ab (`lib/surfaceBoard.ts` →
  `SurfacesPlugin` → eigene Ablage): Rechner, Aktionen, und wer ihn wecken
  könnte. Die Auswahl des Weckboten nach `siteId` bleibt damit an genau einer
  Stelle — sie ein zweites Mal in Kotlin zu schreiben wäre die Sorte
  Doppelung, die erst auffällt, wenn beide Seiten verschieden falsch sind. Die
  Fläche prüft nur noch, ob dieser Bote *gerade* antwortet; das kann der
  Steckbrief nicht wissen, er ist womöglich Tage alt.
- **Der private Schlüssel wird nicht kopiert.** Er wird dort gelesen, wo die
  App ihn ohnehin hält (`CapacitorStorage`, Schlüssel
  `remotedesktop.clientKey`). Eine zweite Kopie wäre ein zweiter Ort, an dem er
  abhandenkommt, und beim Entkoppeln einer, den jemand zu leeren vergisst.
- **Aktionen mit `confirm` erscheinen auf keiner Fläche.** Ein Widget kann
  nicht nachfragen — es hat keine Oberfläche, in der eine Rückfrage stünde. Sie
  trotzdem anzubieten hieße, den Merker aus Phase 13 still auszuhebeln, und
  zwar bei genau den Aktionen, die ihn tragen. Ein Test hält das fest.
- **Kein `WorkManager`, anders als in `PLAN-V2.md`, Abschnitt 6 skizziert.** Er
  bringt Wiederholungen mit, und die sind hier falsch: eine Aktion startet ein
  Programm oder drückt Tasten, und beides ein zweites Mal auszuführen ist kein
  Wiederherstellen, sondern ein zweiter Eingriff. Stattdessen `goAsync()` mit
  einem eigenen Strang — ein Rundruf hat gut zehn Sekunden, drei Anfragen über
  Tailscale brauchen Bruchteile davon. Ein Fehlschlag wird gemeldet und nicht
  wiederholt.
- **Die Unterschrift wird von Hand nach IEEE P1363 umgerechnet.** Java liefert
  DER, der Agent prüft P1363 — dieselbe Falle wie in Phase 14, nur in der
  dritten Sprache. `SHA256withECDSAinP1363Format` zu verlangen wäre kürzer
  gewesen, den Namen kennt aber nicht jede Android-Fassung ab API 26, und ein
  Fehlschlag käme erst auf dem Gerät heraus — wo er wie ein Angriff aussieht.
  Deshalb steht die Umrechnung selbst da und wird gegen den P1363-Prüfer der
  JVM geprüft.
- **`java.util.Base64` statt `android.util.Base64`,** durchgängig. Ersteres
  gibt es seit API 26 (das ist die Untergrenze dieser App) und es läuft auch in
  einem gewöhnlichen JVM-Testlauf. Letzteres ist dort eine leere Hülle, und die
  Signaturprüfung wäre damit nicht prüfbar gewesen. Aus demselben Grund liegt
  die echte `org.json` als Testabhängigkeit dabei.
- **Kotlin ist jetzt im App-Modul eingezogen** — wie es in den Notizen zu
  Phase 12 angekündigt war. Das Gradle-Plugin steht in derselben Fassung
  (2.2.20) da wie beim QR-Scanner aus `node_modules`, und Java wie Kotlin
  übersetzen auf JVM 21; ohne beides bricht der Bau ab. Der Java-Bestand aus
  Phase 12 und 14 wurde **nicht** umgeschrieben: eine Übersetzung ohne Anlass
  hätte nur Risiko ohne Gewinn gebracht.
- **Die Kachel steht beim Aufklappen zuerst auf „Prüfe…".** Sie mit dem Zustand
  von vorhin zu zeigen wäre der Fehler, den man auf halbem Weg zum Schreibtisch
  bemerkt: getippt, nichts passiert, Rechner war längst an.
- **`ShortcutRelay` ist nicht nach außen freigegeben,** ebenso wenig der
  Rundruf des Widgets. Beide lösen auf Zuruf Programme auf dem PC aus; für
  fremde Apps auf demselben Handy haben sie nichts zu bieten. Ob das
  Startprogramm ein nicht freigegebenes Ziel starten darf, ist der eine Punkt
  an den Kürzeln, der sich hier nicht belegen lässt — er steht bei den
  Hardware-Punkten.
- **Aufgefallen, nicht gebaut (gehört zu keiner Phase):** Das Widget zeigt
  immer den zuletzt benutzten Rechner. Wer zwei Rechner nebeneinander benutzt,
  hätte gern zwei Widgets mit fester Zuordnung — dafür bräuchte es eine
  Einrichtungsansicht beim Ablegen (`configure`-Aktivität). Steht unten unter
  „Aufräumarbeiten zum Schluss". Ebenso: die Kachel ließe sich ab Android 13
  per `requestAddTileService` aus der App heraus anbieten, statt darauf zu
  warten, dass jemand sie in den Schnelleinstellungen findet.

---

## Phase 16 — Veröffentlichung · **TOR**

**Status:** erledigt (04.08.2026)
**Aufwand:** 5–7 Tage

Einrichtungsassistent (Tailscale mitliefern, `tailscale up`, Kopplung per QR),
Fehlermeldungen ohne Vorwissen, Coord-Adresse aus der Konfiguration, Lizenz,
README für Fremde, Historie auf Geheimnisse prüfen, Sprachentscheidung
umsetzen.

### Entscheidungen (04.08.2026 von David getroffen)

| Frage | Entscheidung |
|---|---|
| Sprache der Oberfläche | **Deutsch bleiben.** Kein i18n. Zusätzlich ein englischer README, damit Fremde vor dem Installieren erkennen, ob das Programm für sie ist |
| Lizenz | **Apache-2.0** — ausdrückliche Haftungs- und Patentklausel, wie in `PLAN-V2.md` empfohlen |
| Assistent | **Modularer Installer** (Inno Setup) mit einzeln wählbaren Komponenten Agent · Client · Tailscale. Dazu: **Agent und Client teilen sich eine Oberfläche**, und in deren Einstellungen ist wählbar, ob der Autostart nur den Agent, nur den Client oder beides startet |

### Abnahme

- [x] Lizenzdatei vorhanden — `LICENSE`, Apache-2.0 im Wortlaut, 202 Zeilen,
      Copyright-Zeile ausgefüllt. Der Installer zeigt sie vor der Installation
      (`LicenseFile=..\LICENSE`)
- [~] Historie auf Geheimnisse geprüft — **die Prüfung aus der Abnahme taugt so
      nicht** und wurde durch eine genaue ersetzt, siehe Notizen. Ergebnis der
      genauen Prüfung: **nichts gefunden.** Keine echte MAC (alle Treffer sind
      Testwerte wie `aa:bb:cc:dd:ee:ff`), kein echter Tailnet-Name (alle
      `example.ts.net`, `DEIN-TAILNET`, `tail1234`), kein Token-Wert (alle
      Treffer sind Platzhalter wie `HIER-EIN-LANGES-ZUFALLSTOKEN…` oder
      CI-Variablen wie `$KEYSTORE_PASSWORD`). `agent/appsettings.json` liegt als
      einzige Konfigurationsdatei im Repo und enthält nur Port und zwei Pfade
- [~] Eine fremde Person käme mit README und Assistent allein zurecht — gebaut
      und belegbar, aber nicht *bewiesen*: ein echter Fremder an einem echten
      Windows-Rechner steht bei den Hardware-Punkten. Belegt ist: `README.md`
      führt von „was brauche ich" über Installation, Einrichtung, Kopplung bis
      zu einer Tabelle „wenn etwas nicht geht"; der Assistent nennt drei bis
      vier Schritte mit je einem Satz Begründung und einem Knopf; und ein Test
      hält die Sprache dieser Sätze frei von Fachwörtern
      (`Jeder_Schritt_erklaert_sich_ohne_Fachwort`)
- [x] Neuer Testlauf `setup.Tests` — **30 grün**. Er prüft die Regeln, die
      Installer und Fenster teilen: was nicht installiert wird, startet auch
      nicht mit · „kein Autostart" deinstalliert den Dienst nicht · Tailscale
      allein ist keine Installation · der Koordinator muss https sein · bei der
      Vorgabe entfällt `--login-server` · die Argumente gehen einzeln hinaus
- [x] `desktop/` baut mit **0 Warnungen, 0 Fehlern**, jetzt samt
      `SettingsWindow` und dem Verweis auf `setup/`. `dotnet publish` des
      Clients läuft auf Linux durch — der Release-Workflow legt ihn als
      `publish/client.zip` bei
- [x] Alle bestehenden Läufe unverändert grün: App **301**, Agent **310**,
      Waker **69**, Kotlin **9**. Kein bestehender Test geändert
- [ ] `offen: Hardware` — Installer unter Windows übersetzen (`iscc`) und
      durchspielen: alle vier Komponentenkombinationen, Autostart-Häkchen,
      Tailscale-Download, Dienstanlage, Deinstallation

### Notizen

- **Die Abnahmeprüfung auf Geheimnisse war falsch gestellt.** `git log -p |
  grep -Ei "(token|[0-9a-f]{2}:){5}"` sucht das *Wort* „token" und findet
  dadurch jede Codezeile, jeden Kommentar und jeden Testnamen, in dem es
  vorkommt — Hunderte Treffer, alle harmlos. Eine Prüfung, die immer anschlägt,
  prüft nichts. Ersetzt durch drei genaue Suchen, die in den Notizen unten
  stehen und deren Ergebnis oben festgehalten ist: MAC-Muster ohne die
  bekannten Platzhalter, `*.ts.net`-Namen, und Zuweisungen der Form
  `token|secret|password = "…"` mit mindestens zwölf Zeichen. Zusätzlich:
  welche Konfigurationsdateien je zum Repo hinzugefügt wurden
  (`git log --all --diff-filter=A --name-only`) — genau eine, und die ist sauber.
- **Der Installer ist modular, weil die Rechner es sind.** Ein Rechner im
  Keller braucht nur den Agent und nie ein Fenster; ein Arbeitslaptop nur den
  Client und ausdrücklich **keinen** Dienst, der Fremdzugriff erlaubt. Die
  `[Types]` bilden die drei üblichen Fälle ab, `[Components]` erlaubt jede
  Mischung.
- **Agent und Client teilen sich eine Oberfläche, obwohl sie zwei Programme
  sind.** Für den Menschen davor ist es eins. `desktop/SettingsWindow.cs` zeigt
  beides: was an der Einrichtung noch fehlt, und was beim Anmelden starten soll.
  Was auf diesem Rechner überhaupt liegt, liest das Fenster selbst
  (`ClientTrayContext.InstalledSelection`) — ein Autostart für einen Teil, den
  es hier nicht gibt, wird gar nicht erst angeboten.
- **Der Autostart trennt Dienst und Fenster, weil Windows es tut.** Der Agent
  ist ein Dienst (Starttyp `auto` oder `demand`, gesetzt über `sc.exe`), der
  Client ein Eintrag unter `HKCU\…\Run`. Ersteres gilt für den Rechner,
  Letzteres für den angemeldeten Menschen — für alle Benutzer zu entscheiden
  wäre eine Anmaßung und bräuchte Adminrechte, die der Client sonst nirgends
  verlangt.
- **„Kein Autostart" deinstalliert nichts.** Der Dienst geht auf `demand`, nicht
  weg. Sonst verlöre jemand, der den Autostart abschaltet, die Möglichkeit, den
  Agent später von Hand zu starten. Ein Test hält das fest.
- **Ein eigenes Projekt `setup/`, damit die Einrichtung prüfbar ist.** Es hat
  kein WinForms und kein `-windows` im Zielframework; alles, was Windows
  braucht — Registry, `sc.exe`, `tailscale status` —, steckt hinter
  Schnittstellen (`IAutostartHost`, `ISetupProbe`) und liegt in
  `desktop/WindowsSetup.cs`. Das ist die Antwort auf den Punkt aus Phase 11, wo
  ein Testprojekt für `desktop/` verworfen wurde, weil sich die Assembly ohne
  WinForms-Laufzeit nicht laden lässt: nicht das Fenster testbar machen, sondern
  die Entscheidungen aus ihm herausziehen.
- **Tailscale wird heruntergeladen, nicht mitgeliefert** — anders als in
  `PLAN-V2.md`, Abschnitt 4b („der Installer bringt ihn mit"). Es ist ein
  fremdes Programm mit eigenem Aktualisierungsweg; eine mitgelieferte Fassung
  veraltet im Paket, und niemand merkt es. Scheitert der Download, bricht die
  Installation **nicht** ab: Agent und Client sind dann da, und die Einrichtung
  im Fenster führt zum fehlenden Schritt hin. Wegen eines fremden Servers alles
  zurückzurollen wäre die schlechtere Antwort.
- **Die Koordinator-Adresse steht in einer Datei, nicht im Programm.**
  `%ProgramData%\RemoteDesktopAgent\setup.json`, gelesen von
  `CoordinatorConfig`. Vorgabe ist Tailscale; steht dort etwas anderes, bekommt
  `tailscale up` ein `--login-server`. Damit bleibt Modell B aus dem Plan
  (eigener Koordinator, Headscale oder das dort skizzierte `rdcoord`) ohne Umbau
  möglich — nachträglich eingezogen wäre das teuer. Nur `https` wird
  angenommen: über den Koordinator läuft der Schlüsselaustausch des ganzen
  Netzes.
- **Vier Meldungen umgeschrieben, eine davon war schlicht falsch.**
  `agentClient.ts` verwies bei einem abgelehnten Zugang auf `devices.json` —
  eine Datei, die es seit Phase 14 nicht mehr gibt. Die anderen drei nannten
  „den Agent" oder „die Eingabe-Verbindung" und sagten nicht, was zu tun ist.
  Sie nennen jetzt den nächsten Handgriff (Tailscale prüfen, neu koppeln,
  Zertifikat neu holen). Kein Test hing an ihnen; die Treffer in
  `transport/*.test.ts` sind eigene Fehlerobjekte der Tests.
- **Sprache: deutsch, mit einem englischen README.** `README.en.md` sagt im
  zweiten Absatz, dass Oberfläche und Doku deutsch sind — damit niemand
  installiert und dann feststellt, dass er nichts lesen kann.
- **Nicht gebaut, gehört zu keiner Phase:** der Installer wird von Hand unter
  Windows übersetzt. Ein zweiter Workflow-Job auf `windows-latest` mit
  `choco install innosetup` wäre der nächste Schritt; alles, was er bräuchte,
  legt der Workflow inzwischen bereit (`publish/client.zip`). Steht in
  `installer/README.md` unter „Offen".
- **Aufgefallen, nicht gebaut:** Das Repo hat keinen Remote und keinen Tag.
  Veröffentlichen ist ein Handgriff außerhalb dieses Containers und
  ausdrücklich deine Entscheidung — diese Phase macht das Repo dafür bereit,
  sie führt es nicht aus.

---

## Aufräumarbeiten zum Schluss

Liegengebliebenes, das **niemanden aufhält**. Wird nach Phase 16 abgearbeitet,
nicht vorher — es blockiert keine Phase, und einzeln eingeschoben würde es den
Bau nur zerfasern. Wer hier etwas erledigt, löscht die Zeile.

- **`%USERPROFILE%` und Konsorten werden in `args` nicht aufgelöst.** Am
  31.07.2026 auf dem Gerät gesehen: `explorer.exe` mit
  `["%USERPROFILE%\\Downloads"]` öffnete „Dokumente" statt „Downloads". Das ist
  die richtige Folge davon, dass keine Shell im Spiel ist — nur hat man dann
  keinen Weg, den eigenen Benutzerordner zu benennen. Denkbar: eine kurze,
  fest verdrahtete Liste von Platzhaltern (`${USERPROFILE}`, `${DESKTOP}`, …),
  die der Katalog **beim Start** auflöst. Keine allgemeine Expansion, sonst ist
  die Regel aus Phase 13 wieder aufgeweicht.
- **`VirtualKeys` kennt keine Satzzeichen.** `"chord": ["LWin", "."]` für den
  Emoji-Picker wird abgelehnt („nennt die unbekannte Taste '.'"). Buchstaben und
  Funktionstasten sind da, `OEM_PERIOD`, `OEM_COMMA` und Verwandte fehlen.
- **Editor für `actions.json` im Windows-Fenster.** Phase 13 hat den
  Schreibweg über das Netz bewusst nicht gebaut — bearbeitet wird die Datei
  heute mit einem Texteditor. Ein Editor im Fenster aus Phase 11 wäre bequemer
  und bliebe lokal. Dazu gehört die Frage, ob der Agent die Datei danach neu
  einliest oder ob ein Neustart bleibt (heute: Neustart, weil die Prüfung sonst
  auf einen Zeitpunkt rutscht, an dem niemand hinsieht).
- **Widget je Rechner statt „der zuletzt benutzte".** Heute zeigt das Widget
  immer den Rechner, mit dem zuletzt gearbeitet wurde. Wer PC und Laptop
  nebeneinander benutzt, hätte gern zwei Widgets mit fester Zuordnung — dafür
  braucht es eine Einrichtungsansicht beim Ablegen (`configure`-Aktivität) und
  einen Steckbrief je Rechner statt einem. Ebenfalls hierher: die Kachel ließe
  sich ab Android 13 per `requestAddTileService` aus der App heraus anbieten,
  statt darauf zu warten, dass jemand sie in den Schnelleinstellungen findet.
- **`agentkey.txt` per ACL absichern** — steht ausführlich unten unter den
  Hardware-Punkten. Es ist keine reine Prüfung, sondern eine kleine Änderung:
  `Agent:IdentityPath` in die `appsettings.json` aufnehmen und auf
  `C:\ProgramData\RemoteDesktopAgent\` zeigen lassen.

## Nachtrag 04.08.2026 — Updates ohne Dateikopieren

Nach Phase 16 auf Wunsch gebaut (Commit „feat: Updates ohne Dateikopieren"). Es
schließt den Punkt „Selbst-Update der APK an die Oberfläche hängen" aus der
Liste oben und zwei Lücken, die dabei auffielen:

- **Die APK hat jetzt einen Aufrufer.** `views/AppUpdateView.tsx` prüft beim
  Öffnen der Energie-Seite und zeigt ein Angebot, wenn eines dasteht. Die
  Zustandslogik liegt in `lib/appUpdateState.ts` und ist geprüft (5 Tests) —
  vor allem die Zusage, dass „alles aktuell" **nicht** angezeigt wird: eine
  Zeile, die bei jedem Öffnen dasteht und nie etwas zu sagen hat, ist Lärm.
- **Der Windows-Client konnte sich gar nicht aktualisieren.** Der Agent kann es
  seit Phase 14, das Fenster nie. Jetzt geht es über den **Installer**, nicht
  über einzelne Dateien: der Client ist ein Ordner mit Abhängigkeiten, der
  Agent ein Dienst, der erst gestoppt werden muss. `setup/ReleaseCheck.cs`
  (7 Tests) findet den Installer im Release, `desktop/ClientUpdate.cs` lädt und
  startet ihn, das Einstellungsfenster hat den Knopf.
- **Der Installer stoppte den Dienst nicht.** Damit wäre jedes Update an genau
  der Datei gescheitert, um die es geht — eine laufende `.exe` lässt sich unter
  Windows nicht ersetzen. `PrepareToInstall` hält ihn an, `sc start` wirft ihn
  danach wieder an. Außerdem legt der Installer den Dienst nur noch beim ersten
  Mal an und zieht danach nur den Starttyp nach; ein zweites `sc create` wäre
  fehlgeschlagen und der Fehler für niemanden von einem echten zu unterscheiden.
- **Der Installer wird jetzt im CI gebaut.** Zweiter Job auf `windows-latest`
  (`choco install innosetup`), der die Artefakte des ersten übernimmt und seine
  `.exe` an dasselbe Release hängt. Damit ist die Kette vollständig: ein Tag,
  und alle drei Teile holen sich den Rest selbst.
- **`docs/RELEASE.md`** beschreibt die einmalige Einrichtung (Remote,
  Release-Schlüssel, Android-Keystore, erste Installation von Hand) und was ab
  dann je Fassung passiert. Diese fünf Schritte sind der Rest, der **nicht**
  automatisierbar ist — sie brauchen ein GitHub-Konto und einen Windows-Rechner.

`offen: Hardware` bleibt: die Kette einmal von einem Tag bis zum aktualisierten
Gerät durchspielen, auf beiden Plattformen.

## Zurückgestellt

Phasen 17–20 (Tailscale ablösen) stehen in `docs/PLAN-V2.md`, Abschnitt 4a und
9. **Nicht beginnen** — die Entscheidung darüber fällt nach Phase 16.

## Gesammelte Hardware-Punkte

Was hier nicht prüfbar war und am echten Gerät nachgeholt werden muss.

### Am 31.07.2026 auf PC und Handy bestätigt

Ein voller Durchlauf mit echtem Agent, Windows-Client und der APK. Er hat vier
Fehler gefunden, die keine Testsuite zeigen konnte — sie stehen bei den Phasen
12 und 13 in den Notizen. Erledigt und damit hier abgeschlossen:

- **Phase 10:** Kopplung durchgespielt. `/api/pair/code` liefert von einem
  anderen Gerät im Tailnet tatsächlich 403. **Achtung für spätere Läufe:** vom
  Rechner selbst muss man `https://localhost:8443` nehmen — über den
  MagicDNS-Namen kommt auch dort 403, weil die Verbindung über die
  Tailscale-Schnittstelle geht und damit nicht von der Loopback-Adresse.
- **Phase 11:** Selbstverbindungssperre am lebenden Objekt — der Client auf dem
  Rechner des Agents endet mit einer Meldung statt mit einem Bild im Bild.
- **Phase 11:** Kopplungsfenster gegen den laufenden Agent: Code und QR-Code
  werden angezeigt.
- **Phase 12:** QR-Scan am Handy — Kamera-Erlaubnis kommt beim ersten Scan,
  Rechnername und Code werden korrekt übernommen.
- **Phase 12:** Benachrichtigungserlaubnis ab Android 13 — die Sitzung startet
  bei Zustimmung wie bei Ablehnung, die App hängt nicht an der Rückfrage.
- **Phase 12:** Der Vordergrunddienst hält die Sitzung: minimieren, warten, über
  die Benachrichtigung zurück — die Verbindung lebt. Wegwischen beendet Sitzung
  **und** Benachrichtigung.
- **Phase 13:** Alle fünf Arten am laufenden Rechner ausgelöst — `process` mit
  Argumenten, `script` samt Rückfrage, `keys` (`LWin+d`, `LWin+s`), `url`,
  `sequence`. `explorer.exe <adresse>` öffnet tatsächlich den Standardbrowser;
  die 30 ms Haltezeit reichen für die Kombinationen.
- **Phase 13:** Der Abbruch beim Start greift — falscher Pfad wie auch `args`
  als Zeichenkette beenden den Agent mit einer Meldung im Klartext.

### Weiterhin offen

- **Phase 11:** Den Windows-Client vollständig durchgehen: Tray-Symbol, Fenster
  öffnen und verstecken, Pointer Lock im Touchpad, echte Tastatur, und ob die
  Meldung bei fehlender WebView2-Runtime wirklich kommt. Bisher ist nur belegt,
  dass er startet und das Kopplungsfenster zeigt.
- **Phase 12:** Gesten, Bildschirmtastatur und H.264-Latenz am Handy beurteilen.
  Der ursprünglich geplante Vergleich gegen die PWA entfällt auf Wunsch — es
  zählt, ob die APK für sich brauchbar ist.
- **Phase 10:** `agentkey.txt` enthält den privaten Schlüssel im Klartext. Er
  muss unter `C:\ProgramData\RemoteDesktopAgent\` liegen und dieselben ACLs
  bekommen wie `cert.key` (Schritt 3 der `agent/README.md`). Bisher legt der
  Agent die Datei nur an, ohne Rechte zu setzen, und die Vorgabe zeigt neben die
  `.exe`. Siehe „Aufräumarbeiten zum Schluss".
- **Phase 15:** Die drei Flächen am Gerät: erscheint das Widget in der Auswahl
  und zeigt es die Aktionen des zuletzt benutzten Rechners · löst ein Tipp aus,
  und kommt bei einem Fehlschlag die Meldung statt Stille · findet sich die
  Kachel in den Schnelleinstellungen, und stimmen ihre beiden Zustände (läuft →
  schlafen legen, schläft → wecken, niemand wach im Netz → nicht verfügbar) ·
  erscheinen die Kürzel beim langen Druck auf das App-Symbol. Der eine Punkt
  mit echtem Zweifel: ob das Startprogramm `ShortcutRelay` startet, obwohl die
  Aktivität nicht nach außen freigegeben ist. Falls nicht, ist die Antwort
  **nicht** `exported="true"` — dann müsste das Kürzel die App öffnen und die
  Aktion dort auslösen.
- **Phase 16:** Den Installer unter Windows mit `iscc` übersetzen und
  durchspielen: die drei `[Types]` und eine eigene Mischung, beide
  Autostart-Häkchen, der Tailscale-Download (auch der Fall „schlägt fehl"), die
  Dienstanlage mit `auto` und mit `demand`, und die Deinstallation. Danach der
  eigentliche Punkt der Phase: **jemanden, der das Projekt nicht kennt, mit
  README und Assistent allein lassen** und zusehen, wo er hängenbleibt.
- **Phase 14 (Selbst-Update der APK, Kette von vorn):** Remote setzen,
  `ANDROID_KEYSTORE_BASE64` samt Passwörtern und Alias als GitHub-Geheimnisse
  hinterlegen, einen Tag schieben — erst dann gibt es ein Release mit
  `remotedesktop.apk`, das `findLatestApk` finden kann. Danach **einmalig** am
  Handy die debug-signierte Fassung deinstallieren und die Release-APK von Hand
  installieren; ab da greift das Update über sich selbst.
- **Phase 14:** echter Weckvorgang (vom Laptop aus und von der NAS), das
  Selbst-Update auf dem PC — dazu muss vorher einmal `scripts/release-key.mjs`
  gelaufen und der öffentliche Schlüssel eingetragen sein —, und der
  Agent-Neustart bei laufender App: die Sitzung muss von allein zurückkommen.
  Ebenfalls offen: der Dockhand-Stack zeigt noch auf `hub/Dockerfile` und muss
  beim nächsten Deploy auf `waker/Dockerfile` umgestellt werden, samt
  `tailscale cert` für die NAS und einmaligem Koppeln des Wakers.
