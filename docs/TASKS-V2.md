# V2 — Ausführbarer Phasenplan

Arbeitsanweisung für eine **kalte Session**. Wer hier hereinkommt, hat den
Gesprächsverlauf nicht — alles Nötige steht in diesem Dokument und in den
verlinkten Dateien.

Begründungen und Alternativen: **`docs/PLAN-V2.md`**. Dieses Dokument sagt nur,
*was* zu tun ist und *woran* man erkennt, dass es fertig ist.

## Ablauf je Phase

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
| App-Tests | `cd app && npm test` — Stand 30.07.2026: **108 grün** |
| Agent-Tests | `cd agent.Tests && dotnet test` — Stand 30.07.2026: **137 grün** |
| Typprüfung | `cd app && npx tsc -b` |
| Nicht vorhanden | Android SDK / Gradle, Windows, echte Hardware |

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
- [x] `cd app && npx tsc -b` ohne Fehler
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
- **Kleine offene Aufräumarbeit:** `app/tsconfig.tsbuildinfo` ist ein
  Build-Artefakt, wurde aber im Ausgangs-Commit mitversioniert. In
  `.gitignore` steht es jetzt; einmalig fehlt noch
  `git rm --cached app/tsconfig.tsbuildinfo`. Sonst taucht die Datei in jedem
  Diff auf.

---

## Phase 10 — Kopplung und Geräteschlüssel · **TOR**

**Status:** offen
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

- [ ] `cd agent.Tests && dotnet test` grün
- [ ] Tests belegen: Code läuft nach 5 Minuten ab · Code funktioniert kein
      zweites Mal · falscher Scope wird abgelehnt · widerrufener Client wird
      abgelehnt · Signaturprüfung schlägt bei manipulierter Challenge fehl
- [ ] `cd app && npm test` grün
- [ ] `grep -ri "token" agent/ --include=*.cs` zeigt keinen Pfad mehr, der ein
      Klartext-Token persistiert
- [ ] Der alte Token-Weg funktioniert weiter, bis Phase 12 fertig ist —
      sonst sperrt man sich vom eigenen PC aus

### Notizen

_(leer)_

---

## Phase 11 — Windows-Client

**Status:** offen
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

- [ ] `dotnet build` für `desktop/` läuft durch
- [ ] `cd app && npm test` grün
- [ ] Selbstverbindungssperre hat einen Test
- [ ] `offen: Hardware` — tatsächlicher Start unter Windows, Tray, Pointer Lock

### Notizen

_(leer)_

---

## Phase 12 — Android-APK

**Status:** offen
**Tor:** nein
**Aufwand:** 3–4 Tage plus Gerätetests

### Umfang

- Capacitor-Projekt unter `clients/android/`
- `app/src/platform/capacitor.ts`: Preferences statt localStorage, Kamera
- Foreground-Service (`connectedDevice`) für die laufende Sitzung
- QR-Scanner für die Kopplung aus Phase 10

### Abnahme

- [ ] `cd app && npm test` grün
- [ ] Capacitor-Konfiguration vorhanden und in sich stimmig
- [ ] `offen: Hardware` — APK bauen, auf dem Handy gegen die PWA vergleichen:
      Gesten, Tastatur, H.264-Latenz, Verhalten beim Wegwischen

### Notizen

_(leer)_

---

## Phase 13 — Aktionen am Agent · **TOR**

**Status:** offen
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

- [ ] `cd agent.Tests && dotnet test` grün
- [ ] Tests belegen: `args` wird als Array übergeben, **nie** über eine Shell ·
      `script` startet nur hinterlegte Dateien · unbekannte ID → 404, nicht 500 ·
      Sequenzen halten ihre Verzögerungen ein
- [ ] `grep -rn "UseShellExecute = true" agent/` findet nichts
- [ ] Kein Endpoint, der `actions.json` über das Netz beschreibt

### Notizen

_(leer)_

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

_(leer)_
