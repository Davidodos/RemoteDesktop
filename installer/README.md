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

1. **Tailscale** herunterladen und still installieren — nur wenn es angehakt ist
   *und* noch nicht daliegt. Heruntergeladen statt mitgeliefert: eine
   mitgelieferte Fassung veraltet im Paket, und niemand merkt es. Scheitert der
   Download, bricht die Installation **nicht** ab; die Einrichtung im Fenster
   führt dann zum fehlenden Schritt hin.
2. **Agent** nach `Program Files`, Dienst `RemoteDesktopAgent` anlegen. Der
   Starttyp kommt aus dem Autostart-Häkchen (`auto` oder `demand`).
3. **`%ProgramData%\RemoteDesktopAgent`** anlegen — nur für Administratoren und
   das System. Dort liegen Zertifikat, privater Schlüssel und die Liste der
   gekoppelten Geräte.
4. **Client** nach `Program Files\RemoteDesktop\client`, Startmenü-Eintrag,
   auf Wunsch ein Eintrag unter `HKCU\...\Run`.
5. Zum Schluss **das Fenster öffnen**, das durch die restliche Einrichtung
   führt.

Deinstalliert wird der Dienst mit `sc stop` und `sc delete`. Die gekoppelten
Geräte in `%ProgramData%` bleiben absichtlich stehen — wer neu installiert, will
meist nicht alle Handys neu koppeln.

## Im CI

Der Release-Workflow baut ihn seit dem 04.08.2026 selbst: ein zweiter Job auf
`windows-latest` (`choco install innosetup`) übernimmt die Artefakte des ersten
und hängt `RemoteDesktop-Setup-<fassung>.exe` an dasselbe Release. Der Befehl
oben bleibt der Weg, um ihn zwischendurch von Hand zu prüfen.

Der Windows-Client holt sich genau diese Datei, wenn jemand im
Einstellungsfenster auf *Nach Updates suchen* drückt — deshalb muss der Anhang
mit `RemoteDesktop-Setup` beginnen (`setup/ReleaseCheck.cs`).
