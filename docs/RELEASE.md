# Eine neue Fassung veröffentlichen

Ziel: **niemand kopiert mehr Dateien.** Ein Tag genügt; Agent, Windows-Client
und Android-App holen sich den Rest selbst.

Das funktioniert erst, wenn die Einrichtung unten **einmal** gemacht ist. Danach
ist jede weitere Fassung ein `git tag`.

---

## Einmalig einrichten

Diese Schritte kann nur jemand machen, der Zugang zum GitHub-Konto und zu einem
Windows-Rechner hat.

### 0. Öffentlich oder privat — und warum das hier keine Geschmacksfrage ist

**Das Repository muss öffentlich sein, damit die Updates funktionieren.**

Agent, Windows-Client und App fragen die GitHub-API **ohne Anmeldung**
(`api.github.com/repos/…/releases/latest`) und laden die Anhänge über
`browser_download_url`. Bei einem privaten Repository antwortet die API mit 404
und der Download verlangt ein Token — es gäbe also nichts zu finden und nichts
zu holen. Ein Token in eine App zu legen, die auf fremden Geräten läuft, wäre
kein Ausweg, sondern ein weitergegebenes Geheimnis.

Nebenbei: für öffentliche Repositories sind GitHub-Actions-Minuten unbegrenzt.
Bei privaten zählt der `windows-latest`-Läufer des Installer-Jobs **doppelt**
gegen das Freikontingent.

Wer nicht veröffentlichen will, hat zwei Wege: die Releases von einem eigenen
Server ausliefern (dann sind drei Adressen zu ändern, siehe Schritt 1), oder
beim Kopieren von Hand bleiben.

### 1. Repository anlegen und verbinden

```bash
git remote add origin git@github.com:Davidodos/RemoteDesktop.git
git push -u origin master
```

Steht das Repository unter einem anderen Namen, müssen drei Stellen mit:
`app/src/platform/appUpdate.ts` (`RELEASE_REPOSITORY`),
`setup/ReleaseCheck.cs` (`LatestReleaseUrl`) und `agent/Services/GitHubRelease.cs`.

### 2. Release-Schlüssel erzeugen

Er entscheidet, welchem Update der Agent glaubt. **Ohne ihn ist das
Selbst-Update des Agents aus** — er sagt das beim Start im Log.

```bash
node scripts/release-key.mjs
```

- Den **öffentlichen** Teil in `agent/Services/ReleaseManifest.cs` bei
  `ReleaseKeys.PublicKey` eintragen und einchecken.
- Den **privaten** Teil als Repository-Secret `RELEASE_PRIVATE_KEY` hinterlegen
  (GitHub → Settings → Secrets and variables → Actions), dann die Ausgabe
  schließen und vergessen.

> Wer den privaten Schlüssel hat, kann jedem Agent eine beliebige `.exe`
> unterschieben — und der Agent hat vollständige Kontrolle über den Rechner. Er
> gehört nirgendwo hin außer in das Secret.

### 3. Android-Signaturschlüssel erzeugen

Android lässt eine APK nur über eine installierte drüber, wenn **derselbe**
Schlüssel sie unterschrieben hat. Deshalb: einmal erzeugen, nie verlieren.

```bash
keytool -genkeypair -v -keystore release.keystore -alias remotedesktop \
  -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 release.keystore    # Ausgabe als Secret hinterlegen
```

Vier Secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`. Die `release.keystore` danach
sicher aufbewahren — geht sie verloren, kann **keine** installierte App je
wieder aktualisiert werden; sie muss dann deinstalliert und neu installiert
werden.

### 4. Erste Fassung ausrollen

```bash
git tag v1.0.0 && git push origin v1.0.0
```

Der Workflow baut Agent, Oberfläche, Client und APK, unterschreibt das Manifest,
baut den Installer auf einem Windows-Läufer und hängt alles an das Release.

### 5. Einmal von Hand installieren

Das ist der letzte manuelle Schritt, und er ist unvermeidlich:

- **Windows:** `RemoteDesktop-Setup-1.0.0.exe` aus dem Release ausführen. Was
  vorher von Hand nach `C:\RemoteDesktopAgent\` kopiert wurde, kann weg —
  vorher `%ProgramFiles%\RemoteDesktop\data\` sichern (eine Deinstallation räumt
  ihn seit v1.3.0 mit weg), dort liegen Zertifikat und
  gekoppelte Geräte.
- **Android:** die vorhandene, debug-signierte App **deinstallieren**, dann
  `remotedesktop.apk` aus dem Release installieren. Ohne das lehnt Android jedes
  Update ab, weil die Signaturen nicht zusammenpassen. Danach einmal neu koppeln.

---

## Ab dann: jede weitere Fassung

```bash
git tag v1.1.0 && git push origin v1.1.0
```

### Wenn der Tag ankommt, aber kein Lauf startet

Kam am 06.08.2026 bei `v1.2.1` vor: der Tag stand auf GitHub, annotiert, auf dem
richtigen Commit — und in *Actions* war trotzdem **kein** Eintrag, auch kein
fehlgeschlagener. Von außen ist dagegen nichts zu machen; wiederholtes Löschen
und neu Setzen des Tags half nicht.

Der Weg daran vorbei: Reiter **Actions → Release → Run workflow**, und dort oben
als Ref **den Tag** wählen (nicht `master`). Der Ablauf ist derselbe, die Fassung
kommt weiterhin aus dem Namen des Tags. Wird versehentlich ein Branch gewählt,
bricht der erste Schritt mit einer Erklärung ab, statt eine Fassung namens
„master" zu bauen.

Als Zweites hilft oft ein **frischer Tag-Name** (`v1.2.2` statt noch einmal
`v1.2.1`). Ein Name, den der Server noch nie gesehen hat, umgeht alles, was an
Resten des alten hängen könnte.

Und auf den Geräten:

| Wo | Was passiert |
|---|---|
| **Agent** | Prüft kurz nach jedem Start selbst und tauscht seine eigene `.exe`. Sofort geht es über `POST /api/update` |
| **Windows-Fenster** | *Übersicht → Updates → Nach Updates suchen*. Lädt den Installer und startet ihn; der beendet Agent und Fenster, ersetzt alles und startet den Agent wieder |
| **Von einem gekoppelten Gerät aus** | Geräteliste → *⋯* → *Aktualisieren*. Der Knopf erscheint nur bei einem Rechner mit älterer Fassung. Windows fragt dabei nichts nach — der Agent läuft ohnehin erhöht |
| **Android-App** | Sucht bei jedem Start und meldet sich mit einem Band, wenn es etwas gibt; sonst über *Einstellungen → Updates*. Ein Knopf, dann der Systemdialog von Android |

Der Installer merkt sich an seiner AppId, welche Komponenten beim letzten Mal
gewählt waren — ein Update installiert nichts nach, was jemand bewusst
weggelassen hat.

### Was ein Update stehen lässt und was nicht

**Es bleibt:** der Ordner `{app}\data` (Zertifikate, private Schlüssel,
`clients.json` mit den gekoppelten Geräten, `setup.json` mit den Antworten aus
der Einrichtung, `devicename.txt`, `hotkey.txt`) und der `localStorage` des
Fensters unter `%localappdata%\RemoteDesktop` — dort liegt seine Geräteliste.

**Es geht weg:** die Rückstände des Agent-Selbst-Updates (`.old`, `.new`,
`.update` — das `.old` ist eine startbare Programmdatei einer älteren Fassung),
der Ordner `{app}\app` mit der alten Oberfläche, und die Code-Caches von
WebView2. Am Handy räumt `UpgradeCleanup` beim ersten Start nach einem
Fassungswechsel den WebView-Zwischenspeicher weg; Preferences, `localStorage`
und der Ordner `host/` bleiben.

**Beim Deinstallieren geht alles:** erst werden Agent und Fenster beendet, dann
verschwinden `{app}` als Ganzes, `%localappdata%\RemoteDesktop` in allen
Profilen, die geplante Aufgabe und der Dienst einer älteren Installation. Danach
ist nichts mehr von Hand wegzuräumen.

---

## Wenn etwas nicht geht

| Beobachtung | Ursache |
|---|---|
| Agent-Log sagt „Selbst-Update ist aus" | Schritt 2 fehlt: `ReleaseKeys.PublicKey` ist leer |
| Agent lehnt das Update ab | Manifest mit einem anderen Schlüssel unterschrieben als dem einkompilierten. Nach einem Schlüsselwechsel muss jeder Agent einmal von Hand auf die neue Fassung |
| Android sagt „App nicht installiert" | Die installierte Fassung trägt eine andere Signatur — siehe Schritt 5 |
| APK installiert, App-Details zeigen die neue Fassung, die Oberfläche ist die alte — beim zweiten Start ist sie da | Der Service Worker der PWA hatte sich auch in der APK angemeldet und beantwortete jeden Start aus seinem Zwischenspeicher von gestern. Behoben ab v1.3.6: dort meldet sich die App beim ersten Start selbst ab. Der Übergang dorthin kostet noch einmal genau diesen zweiten Start, danach nie wieder |
| Windows-Client findet nichts | Das Release hat keinen Anhang, der mit `RemoteDesktop-Setup` beginnt. Der Installer-Job im Workflow ist fehlgeschlagen |
| Ein Skript im CI endet mit „Permission denied" (Exit 126) | Das Ausführbar-Bit fehlt in Git. In diesem Repository gilt `core.fileMode=false` (CIFS-Mount), deshalb landet alles als 644 — und lokal sieht man es nicht, weil der Mount 777 zeigt. Heilung: `git update-index --chmod=+x <datei>` |
| Update bricht mit Dateizugriffsfehler ab | Der Agent lief noch. Der Installer beendet ihn und wartet auf sein Ende (`PrepareToInstall`); schlägt das fehl, hilft `schtasks /End /TN RemoteDesktopAgent` von Hand |
| „Aktualisieren" am gekoppelten Gerät sagt „nicht gültig unterschrieben" | Das Release hat kein `installer.json` (+ `.sig`), oder es ist mit einem anderen Schlüssel unterschrieben. Der Installer-Job im Workflow signiert es; ohne `RELEASE_PRIVATE_KEY` bricht er ab |
| Am Handy tut „Jetzt installieren" nichts | Bis v1.3.x: der Bestätigungsdialog wurde nie gestartet (siehe 31m in `TASKS-V4.md`). Danach: der Schalter „Unbekannte Apps installieren" fehlt — die App öffnet die Einstellung jetzt selbst und sagt es |

Was zu tun ist, wenn der Release-Schlüssel abhandenkommt, steht in
[`SICHERHEIT.md`](SICHERHEIT.md#wenn-der-release-schlüssel-verloren-geht).
