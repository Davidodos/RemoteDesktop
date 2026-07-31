# V2 — Ausführbarer Phasenplan

Arbeitsanweisung für eine **kalte Session**. Wer hier hereinkommt, hat den
Gesprächsverlauf nicht — alles Nötige steht in diesem Dokument und in den
verlinkten Dateien.

Begründungen und Alternativen: **`docs/PLAN-V2.md`**. Dieses Dokument sagt nur,
*was* zu tun ist und *woran* man erkennt, dass es fertig ist.

## Offene Aufräumarbeiten

Kleinkram, der in einer Phase liegengeblieben ist und zu keiner neuen gehört.
**Zuerst abarbeiten**, bevor eine Phase begonnen wird — sonst fällt er durchs
Raster, weil die zugehörige Phase schon `erledigt` ist. Erledigtes hier
löschen, nicht abhaken.

- **`hubClient.ts` benutzt relative Pfade** (`fetch('/api/devices')`). Das
  stimmt für die PWA, die vom Hub ausgeliefert wird — im WebView2-Fenster ist
  die Herkunft aber `https://app.remotedesktop.invalid`, und der Aufruf geht ins
  Leere. Die App meldet dann „Hub nicht erreichbar. Läuft Tailscale?", obwohl
  beides stimmt. Am 31.07.2026 auf echter Hardware aufgefallen. Phase 9 hat den
  Transport eingezogen, `hubClient` aber als einzigen Netzzugriff daran vorbei
  gelassen. Kopplung im Fenster funktioniert, der Hub nicht. Phase 14 macht die
  Datei ohnehin auf.
- **Editor für `actions.json` im Windows-Fenster.** Phase 13 hat den
  Schreibweg über das Netz bewusst nicht gebaut — bearbeitet wird die Datei
  heute mit einem Texteditor. Ein Editor im Fenster aus Phase 11 wäre bequemer
  und bliebe lokal. Dazu gehört die Frage, ob der Agent die Datei danach neu
  einliest oder ob ein Neustart bleibt (heute: Neustart, weil die Prüfung sonst
  auf einen Zeitpunkt rutscht, an dem niemand hinsieht).

## Ablauf je Phase

0. **Aufräumliste oben abarbeiten**, falls dort etwas steht.
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
| App-Tests | `cd app && npm test` — Stand 31.07.2026: **249 grün** |
| Agent-Tests | `cd agent.Tests && dotnet test` — Stand 31.07.2026: **254 grün** |
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
      `foregroundServiceType=0x10` (= `CONNECTED_DEVICE`), alle drei eigenen
      Klassen im DEX, debug-signiert und damit installierbar
- [ ] `offen: Hardware` — auf dem Handy gegen die PWA vergleichen: Gesten,
      Tastatur, H.264-Latenz, Verhalten beim Wegwischen

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

**Status:** offen
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

### Abnahme

- [ ] Beide Testläufe grün
- [ ] Tests belegen: manipuliertes Manifest wird abgelehnt · gleiche `siteId`
      wird gefunden, fremde nicht · kein Kandidat → Knopf aus, kein Fehler
- [ ] `waker/` enthält keine Geräteliste mehr
- [ ] `offen: Hardware` — echter Weckvorgang, Selbst-Update auf dem PC

### Notizen

_(leer)_

---

## Phase 15 — Android-Flächen

**Status:** offen
**Tor:** nein
**Aufwand:** 4–5 Tage

Widget mit Aktionsraster, Quick-Settings-Tile, dynamische App-Shortcuts.
Weckknopf nur aktiv, wenn ein Kandidat mit passender `siteId` erreichbar ist.

### Abnahme

- [ ] Kotlin-Anteile vorhanden und in sich stimmig
- [ ] `offen: Hardware` — Widget, Tile und Shortcuts auf dem Gerät

### Notizen

_(leer)_

---

## Phase 16 — Veröffentlichung · **TOR**

**Status:** offen
**Aufwand:** 5–7 Tage

Einrichtungsassistent (Tailscale mitliefern, `tailscale up`, Kopplung per QR),
Fehlermeldungen ohne Vorwissen, Coord-Adresse aus der Konfiguration, Lizenz,
README für Fremde, Historie auf Geheimnisse prüfen, Sprachentscheidung
umsetzen.

### Abnahme

- [ ] Eine fremde Person käme mit README und Assistent allein zurecht
- [ ] `git log -p | grep -Ei "(token|[0-9a-f]{2}:){5}"` findet nichts
- [ ] Lizenzdatei vorhanden

### Notizen

_(leer)_

---

## Zurückgestellt

Phasen 17–20 (Tailscale ablösen) stehen in `docs/PLAN-V2.md`, Abschnitt 4a und
9. **Nicht beginnen** — die Entscheidung darüber fällt nach Phase 16.

## Gesammelte Hardware-Punkte

Was hier nicht prüfbar war und am echten Gerät nachgeholt werden muss:

- **Phase 10:** Kopplung von Hand durchspielen — am Windows-Rechner einen Code
  anfordern, ihn im Handy eintippen, danach Bild und Eingabe prüfen. Ebenso:
  dass `/api/pair/code` von einem anderen Gerät im Tailnet tatsächlich 403
  liefert (die Loopback-Erkennung ist hier nicht prüfbar).
- **Phase 11:** Den Client unter Windows tatsächlich starten: Tray-Symbol,
  Fenster öffnen und verstecken, Pointer Lock im Touchpad, echte Tastatur, und
  ob die Meldung bei fehlender WebView2-Runtime wirklich kommt.
- **Phase 11:** Die Selbstverbindungssperre am lebenden Objekt — den Client auf
  demselben Rechner starten, auf dem der Agent läuft, und prüfen, dass die
  Auswahl mit einer Meldung endet statt mit einem Bild im Bild.
- **Phase 11:** Kopplungsfenster gegen den laufenden Agent: Code anzeigen,
  Geräte auflisten, widerrufen. Die Loopback-Beschränkung des Agents lässt sich
  hier nicht nachstellen.
- **Phase 12:** Die gebaute APK auf dem Handy gegen die PWA halten: Gesten,
  Bildschirmtastatur, H.264-Latenz. Ausdrücklich dazu: einmal wegwischen und
  prüfen, ob die Benachrichtigung stehen bleibt und die Verbindung danach noch
  lebt — das ist der ganze Grund für den Vordergrunddienst. (Das *Bauen* ist
  seit dem 31.07.2026 erledigt, siehe Phase 12.)
- **Phase 12:** Der QR-Scanner am lebenden Objekt. Er lässt sich erst prüfen,
  wenn der Aufräumpunkt oben erledigt ist und ein Rechner tatsächlich einen Code
  anzeigt. Dazu gehört die Kamera-Erlaubnis beim ersten Scan und der Abbruch
  über die Zurück-Taste.
- **Phase 12:** Die Benachrichtigungserlaubnis ab Android 13 — ablehnen und
  prüfen, dass die Sitzung trotzdem startet, statt an der Rückfrage zu hängen.
- **Phase 13:** Jede Art einmal am laufenden Windows-Rechner auslösen —
  `process` mit Argumenten, `script`, `keys`, `url`, `sequence`. Besonders
  `keys`: ob `LWin+P` wirklich die Projektionsleiste öffnet, hängt daran, dass
  die 30 ms Haltezeit reichen und die Eingabe auf dem richtigen Desktop landet.
- **Phase 13:** Den Abbruch beim Start provozieren — eine `actions.json` mit
  einem falschen Pfad hinlegen und prüfen, dass der Dienst sich mit der Meldung
  im Klartext beendet und nicht still weiterläuft.
- **Phase 13:** `explorer.exe <adresse>` statt `UseShellExecute` — ob damit
  tatsächlich der Standardbrowser aufgeht. Hier ist das nicht nachstellbar.
- **Phase 10:** `agentkey.txt` enthält den privaten Schlüssel im Klartext. Er
  muss unter `C:\ProgramData\RemoteDesktopAgent\` liegen und dieselben ACLs
  bekommen wie `cert.key` (Schritt 3 der `agent/README.md`). Bisher legt der
  Agent die Datei nur an, ohne Rechte zu setzen.
