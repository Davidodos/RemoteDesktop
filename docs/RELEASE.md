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
  vorher `%ProgramData%\RemoteDesktopAgent\` sichern, dort liegen Zertifikat und
  gekoppelte Geräte.
- **Android:** die vorhandene, debug-signierte App **deinstallieren**, dann
  `remotedesktop.apk` aus dem Release installieren. Ohne das lehnt Android jedes
  Update ab, weil die Signaturen nicht zusammenpassen. Danach einmal neu koppeln.

---

## Ab dann: jede weitere Fassung

```bash
git tag v1.1.0 && git push origin v1.1.0
```

Und auf den Geräten:

| Wo | Was passiert |
|---|---|
| **Agent** | Prüft kurz nach jedem Start selbst und tauscht sich aus. Sofort geht es über *Energie → Auf Updates prüfen* in der App |
| **Windows-Client** | Tray → *Einrichtung…* → *Updates* → *Nach Updates suchen*. Lädt den Installer und startet ihn; der stoppt den Dienst, ersetzt beide Teile und startet neu |
| **Android-App** | Zeigt beim Öffnen von *Energie* selbst an, dass eine neue Fassung bereitliegt. Ein Knopf, dann der Systemdialog von Android |

Der Installer merkt sich an seiner AppId, welche Komponenten beim letzten Mal
gewählt waren — ein Update installiert nichts nach, was jemand bewusst
weggelassen hat.

---

## Wenn etwas nicht geht

| Beobachtung | Ursache |
|---|---|
| Agent-Log sagt „Selbst-Update ist aus" | Schritt 2 fehlt: `ReleaseKeys.PublicKey` ist leer |
| Agent lehnt das Update ab | Manifest mit einem anderen Schlüssel unterschrieben als dem einkompilierten. Nach einem Schlüsselwechsel muss jeder Agent einmal von Hand auf die neue Fassung |
| Android sagt „App nicht installiert" | Die installierte Fassung trägt eine andere Signatur — siehe Schritt 5 |
| Windows-Client findet nichts | Das Release hat keinen Anhang, der mit `RemoteDesktop-Setup` beginnt. Der Installer-Job im Workflow ist fehlgeschlagen |
| Ein Skript im CI endet mit „Permission denied" (Exit 126) | Das Ausführbar-Bit fehlt in Git. In diesem Repository gilt `core.fileMode=false` (CIFS-Mount), deshalb landet alles als 644 — und lokal sieht man es nicht, weil der Mount 777 zeigt. Heilung: `git update-index --chmod=+x <datei>` |
| Update bricht mit Dateizugriffsfehler ab | Der Agent-Dienst lief noch. Der Installer stoppt ihn (`PrepareToInstall`); schlägt das fehl, hilft `sc stop RemoteDesktopAgent` von Hand |

Was zu tun ist, wenn der Release-Schlüssel abhandenkommt, steht in
[`SICHERHEIT.md`](SICHERHEIT.md#wenn-der-release-schlüssel-verloren-geht).
