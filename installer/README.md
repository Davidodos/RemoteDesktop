# Installer

`RemoteDesktop.iss` ist ein [Inno-Setup-6](https://jrsoftware.org/isinfo.php)-Skript.
Es baut die `RemoteDesktop-Setup-<fassung>.exe`, mit der Fremde das Programm
installieren — und zwar modular: **Agent**, **Client** und **Tailscale** lassen
sich einzeln an- und abwählen.

## Warum modular

| Rechner | sinnvolle Auswahl |
|---|---|
| Der PC, den du fernsteuern willst | Agent (+ Client, wenn du von dort auch steuern willst) |
| Arbeitslaptop, der nur steuern soll | nur Client — dann läuft dort kein Dienst, der Zugriff erlaubt |
| Rechner, auf dem Tailscale schon läuft | Tailscale abwählen |

Die Häkchen zum Autostart hängen an den Komponenten: was nicht installiert wird,
kann auch nicht mitstarten. Dieselbe Regel steht in `setup/Selection.cs` und ist
dort geprüft (`Was_nicht_installiert_wird_startet_auch_nicht_mit`) — der
Installer und das Einstellungsfenster benutzen dieselbe Bibliothek, damit die
beiden nicht auseinanderlaufen können.

## Bauen

Der Compiler `iscc.exe` läuft nur unter Windows. Was der Installer
*entscheidet*, liegt deshalb in `setup/` und wird auf jedem Rechner geprüft;
hier steht nur, was er *tut*.

```powershell
# Voraussetzung: publish/ aus dem Release-Workflow liegt daneben,
#   publish/release/RemoteDesktopAgent.exe   und
#   publish/client/                          (entpacktes client.zip)
iscc installer\RemoteDesktop.iss /DVersion=1.2.0
```

Heraus kommt `installer\Output\RemoteDesktop-Setup-1.2.0.exe`.

## Was er tut

1. **Dateien ablegen**: Fenster, Agent und die Weboberfläche nebeneinander nach
   `Program Files\RemoteDesktop`, dazu die `appsettings.json` (nur, wenn sie
   noch nicht da ist).
2. **`%ProgramFiles%\RemoteDesktop\data`** anlegen — nur für Administratoren und
   das System. Dort liegen Zertifikat, privater Schlüssel, gekoppelte Geräte und
   das Netzprofil.
3. **Startmenü-Eintrag** anlegen.
4. Zum Schluss **das Fenster öffnen**, das durch die Einrichtung führt.

Mehr tut er nicht — und das ist seit v1.3.0 der Punkt. Kein Dienst, kein
Autostart-Eintrag, kein Tailscale, kein gestarteter Agent: was auf diesem
Rechner aktiv wird, entscheidet die Einrichtung im Fenster
(`desktop/Pages/SetupPage.cs`). Vier Häkchen im Installer wurden gesetzt, bevor
irgendjemand die Frage verstanden hatte.

Deinstalliert wird der Dienst mit `sc stop` und `sc delete`, und der Datenordner
wird über `[UninstallDelete]` mit weggeräumt: was zum Programm gehört, soll auch
mit ihm verschwinden. Ein **Update** rührt ihn dagegen nicht an — der Installer
kennt seinen Inhalt nicht, also überstehen Kopplungen und die eigene
Zertifizierungsstelle jede neue Fassung.

## Im CI

Der Release-Workflow baut ihn seit dem 04.08.2026 selbst: ein zweiter Job auf
`windows-latest` (`choco install innosetup`) übernimmt die Artefakte des ersten
und hängt `RemoteDesktop-Setup-<fassung>.exe` an dasselbe Release. Der Befehl
oben bleibt der Weg, um ihn zwischendurch von Hand zu prüfen.

Der Windows-Client holt sich genau diese Datei, wenn jemand im
Einstellungsfenster auf *Nach Updates suchen* drückt — deshalb muss der Anhang
mit `RemoteDesktop-Setup` beginnen (`setup/ReleaseCheck.cs`).
