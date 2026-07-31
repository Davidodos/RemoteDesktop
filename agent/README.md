# Agent — Einrichtung auf PC und Laptop

Auf beiden Windows-Rechnern durchführen. **Kein .NET, kein Git, kein Quellcode
nötig** — die fertige `.exe` ist self-contained und liegt auf der NAS.

Voraussetzung: Tailscale läuft und ist angemeldet, MagicDNS und
HTTPS-Zertifikate sind im Tailscale-Adminpanel aktiviert.

## 1. Dateien kopieren

Im Windows-Explorer `\\192.168.178.43\docker\remotedesktop\dist\` öffnen und
beide Dateien nach `C:\RemoteDesktopAgent\` kopieren:

- `RemoteDesktopAgent.exe`
- `appsettings.json`

> Nicht nach `C:\Program Files\` — dort bräuchte jeder Schreibzugriff
> Administratorrechte, und der Agent läuft bewusst im normalen Benutzerkontext.

Beim ersten Start warnt SmartScreen, weil die Datei nicht signiert ist:
**Weitere Informationen → Trotzdem ausführen**.

## 2. Eigenen Tailscale-Namen ermitteln

PowerShell (normal, ohne Admin):

```powershell
tailscale status --json | ConvertFrom-Json | Select-Object -ExpandProperty Self | Select-Object -ExpandProperty DNSName
```

Ergibt z.B. `pc.tail1234.ts.net.` — der Punkt am Ende gehört nicht dazu.

## 3. Zertifikat holen

Tailscale besorgt ein echtes Let's-Encrypt-Zertifikat. Ohne das blockt der
Browser die Verbindung, weil die App über HTTPS von der NAS kommt.

PowerShell **als Administrator**:

```powershell
mkdir C:\ProgramData\RemoteDesktopAgent -Force
cd C:\ProgramData\RemoteDesktopAgent
tailscale cert --cert-file cert.crt --key-file cert.key pc.tail1234.ts.net
```

`pc.tail1234.ts.net` durch den Namen aus Schritt 2 ersetzen.

`C:\ProgramData` vererbt Leserechte an `Users`, der Agent kommt also normalerweise
an den Schlüssel. Falls er beim Start doch über `cert.key` klagt:

```powershell
icacls C:\ProgramData\RemoteDesktopAgent\cert.key /grant "${env:USERNAME}:(R)"
```

> Die geschweiften Klammern sind nötig — ohne sie zieht PowerShell den
> folgenden Doppelpunkt noch zum Variablennamen und `icacls` bekommt
> `Ungültiger Parameter: "(R)"`.

## 4. Token erzeugen

Seit Phase 10 ist das **freiwillig**: gekoppelte Clients (siehe „Kopplung"
unten) brauchen es nicht. Solange noch ein Gerät über die NAS-Geräteliste
angebunden ist, muss es aber gesetzt sein — sonst sperrst du dich von dem
Rechner aus, an dem du gerade nicht sitzt. **Ausgabe notieren.**

```powershell
$token = -join ((1..48) | ForEach-Object { '{0:x2}' -f (Get-Random -Max 256) })
$token
[Environment]::SetEnvironmentVariable("REMOTEDESKTOP_TOKEN", $token, "User")
```

## 5. Firewall öffnen

Windows blockt eingehende Verbindungen auf Port 8443 sonst stillschweigend.
PowerShell **als Administrator**:

```powershell
New-NetFirewallRule -DisplayName "RemoteDesktop Agent" `
                    -Direction Inbound -Protocol TCP -LocalPort 8443 `
                    -Action Allow -Profile Any
```

## 6. Erster Start von Hand

Neue PowerShell öffnen (damit die Umgebungsvariable aus Schritt 4 greift):

```powershell
cd C:\RemoteDesktopAgent
.\RemoteDesktopAgent.exe
```

Erwartete Ausgabe:

```
RemoteDesktop-Agent lauscht auf Port 8443 als PC
```

In einem **zweiten** Fenster prüfen:

```powershell
curl.exe https://pc.tail1234.ts.net:8443/health
# {"status":"ok"}
```

Läuft das, mit `Strg+C` beenden und weiter zu Schritt 7.

## 7. Autostart einrichten

Der Agent läuft **nicht** als Windows-Dienst. Dienste laufen in Session 0 und
können dem angemeldeten Desktop weder Maus- noch Tastatureingaben schicken —
genau das ist hier der Zweck. Stattdessen Autostart bei der Anmeldung:

```powershell
$me = "$env:USERDOMAIN\$env:USERNAME"
$action  = New-ScheduledTaskAction -Execute "C:\RemoteDesktopAgent\RemoteDesktopAgent.exe" `
                                   -WorkingDirectory "C:\RemoteDesktopAgent"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $me
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries `
                                         -DontStopIfGoingOnBatteries `
                                         -ExecutionTimeLimit 0 `
                                         -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName "RemoteDesktopAgent" `
                       -Action $action -Trigger $trigger -Settings $settings -User $me -RunLevel Highest
```

> `-User` braucht einen **qualifizierten** Kontonamen. Nur `$env:USERNAME`
> (also `david`) lehnt die Aufgabenplanung mit `Falscher Parameter` /
> `HRESULT 0x80070057` und dem Hinweis `(7,23):UserId:david` ab.
> `$env:USERDOMAIN` ist bei einem lokalen Konto der Computername.

Testen mit `Start-ScheduledTask -TaskName RemoteDesktopAgent`.

Das Fenster läuft sichtbar mit. Wer es versteckt haben will, legt die Aufgabe
stattdessen über die GUI der Aufgabenplanung an und wählt dort
„Unabhängig von der Benutzeranmeldung ausführen" — Achtung, dann greift wieder
die Session-0-Einschränkung.

## 8. Zertifikat automatisch erneuern

`tailscale cert` erneuert nur auf Aufruf, das Zertifikat läuft nach 90 Tagen ab.
Als Administrator:

```powershell
$renew = New-ScheduledTaskAction -Execute "tailscale" `
  -Argument "cert --cert-file C:\ProgramData\RemoteDesktopAgent\cert.crt --key-file C:\ProgramData\RemoteDesktopAgent\cert.key pc.tail1234.ts.net"
Register-ScheduledTask -TaskName "RemoteDesktopAgent-Cert" `
                       -Action $renew `
                       -Trigger (New-ScheduledTaskTrigger -Daily -At 4am) `
                       -RunLevel Highest
```

Danach den Agent neu starten, damit er das neue Zertifikat lädt.

## Fehlersuche

| Symptom | Ursache |
|---|---|
| `Kein Token konfiguriert` | PowerShell nach Schritt 4 nicht neu geöffnet |
| `Zertifikat nicht gefunden` | Schritt 3 übersprungen oder falscher Pfad in `appsettings.json` |
| `/health` antwortet nicht von außen | Firewall-Regel aus Schritt 5 fehlt |
| Zertifikatsfehler im Browser | Zertifikat abgelaufen — Schritt 3 wiederholen |
| Klicks landen daneben | Bekannte Baustelle bei Display-Skalierung, bitte melden |
| Eingaben kommen in einem Programm nicht an | Läuft es als Administrator? Dann braucht der Agent dieselbe Rechtestufe (UIPI) |

## Kopplung

Statt ein Token abzutippen, meldet sich jedes Gerät einmal mit einem eigenen
Schlüsselpaar an. Der Agent merkt sich nur den öffentlichen Teil in
`clients.json` — dort steht kein Geheimnis, und ein verlorenes Handy lässt sich
einzeln widerrufen, ohne die anderen Geräte anzufassen.

Ablauf:

1. **Am Rechner** einen Code anfordern. Er gilt fünf Minuten, lässt sich einmal
   verwenden und wird nach fünf Fehlversuchen verworfen:

   ```powershell
   Invoke-RestMethod -Method Post https://localhost:8443/api/pair/code
   ```

   Der Code steht auch im Log des Agents. `/api/pair/code` und `/api/clients`
   sind **nur vom Rechner selbst** erreichbar — über das Netz antworten sie mit
   403. Ab Phase 11 macht das ein Knopf im Tray-Fenster.

2. **Auf dem Handy** in der App „Gerät koppeln" wählen, Rechnernamen und Code
   eintippen. Fertig — ab dann weist sich die App mit einer Unterschrift aus.

Verwalten (ebenfalls nur lokal):

```powershell
Invoke-RestMethod https://localhost:8443/api/clients
Invoke-RestMethod -Method Delete https://localhost:8443/api/clients/<id>
```

Ein Widerruf wirkt sofort: die laufende Sitzung des Geräts wird mitgeschlossen.

### Rechte

Jeder gekoppelte Client bekommt eine Teilmenge von `screen`, `input`, `media`,
`power`, `actions`, `wake`. Fehlt eines, antwortet der Agent mit 403 statt die
Aktion auszuführen. Das alte Sammel-Token kennt diese Trennung nicht und darf
alles — auch deshalb wird es abgelöst.

## API

Alles außer `/health` und den Kopplungs-Endpunkten verlangt einen Ausweis — als
Header `Authorization: Bearer <token>` oder bei WebSockets als `?token=<token>`
(Browser können bei WebSockets keine Header setzen). Das ist entweder das
Sitzungstoken aus `/api/session` oder das alte Sammel-Token.

| Endpoint | Zweck |
|---|---|
| `GET /health` | Erreichbarkeit, ohne Auth |
| `POST /api/pair/code` | Kopplungscode anzeigen — **nur lokal** |
| `POST /api/pair` | Koppeln: `{"code","label","publicKey"}`, ohne Auth |
| `POST /api/session/challenge` | Challenge holen: `{"clientId"}`, ohne Auth |
| `POST /api/session` | Anmelden: `{"clientId","nonce","signature"}`, ohne Auth |
| `GET /api/clients` | Gekoppelte Geräte — **nur lokal** |
| `DELETE /api/clients/{id}` | Widerrufen — **nur lokal** |
| `GET /api/info` | Hostname, Monitorliste, virtueller Desktop |
| `POST /api/power` | `{"action":"sleep\|shutdown\|restart\|lock"}` |
| `POST /api/media` | `{"action":"playpause\|next\|prev\|stop\|volup\|voldown\|mute","repeat":1}` |
| `GET /api/actions` | Was dieser Rechner auf Zuruf tut — ohne Pfade und Argumente |
| `POST /api/actions/{id}/invoke` | Eine Aktion auslösen. Unbekannte Kennung → 404 |
| `WS /ws/input` | Eingabe-Stream |
| `GET /api/media/sessions` | Was gerade läuft: Titel, Interpret, App, Status |
| `GET /api/media/thumbnail` | Titelbild einer Sitzung, `?session=<id>` |
| `WS /ws/screen` | Bild-Stream als JPEG, `?monitor=N&fps=30` |
| `POST /api/webrtc/offer` | H.264-Stream aufbauen (SDP-Angebot der App) |
| `POST /api/webrtc/{id}/monitor` | Monitor wechseln, ohne die Verbindung zu lösen |
| `DELETE /api/webrtc/{id}` | Stream beenden |

### Protokoll `/ws/input`

Eine JSON-Nachricht pro Frame:

```jsonc
{"t":"move","monitor":0,"x":0.5,"y":0.5}   // Position 0..1 auf dem Monitor
{"t":"moverel","dx":12,"dy":-8}            // relativ, vom Trackpad
{"t":"down","button":"left"}               // halten / ziehen
{"t":"up","button":"left"}
{"t":"click","button":"right"}
{"t":"scroll","dy":3,"dx":0}               // Rasterschritte, positiv = hoch
{"t":"keydown","key":"shift"}
{"t":"keyup","key":"shift"}
{"t":"key","key":"escape","mods":["ctrl","shift"]}
{"t":"text","text":"Hallo Welt"}
```

Fehler kommen als `{"t":"error","message":"..."}` zurück, ohne die Verbindung
zu schließen. Bricht die Verbindung ab, löst der Agent alle noch gedrückten
Tasten selbstständig — sonst bliebe der PC nach einem Abbruch mitten im Drag
unbedienbar.

### Protokoll `/ws/screen`

Der Agent nimmt den Monitor über die Desktop Duplication API auf und schickt
**nur die geänderten Ausschnitte** als JPEG. Ein Vollbild kommt beim Verbinden,
nach `refresh` und nach jeder Unterbrechung der Aufnahme.

Binärnachrichten (Bild):

```
Byte 0-1  x       ┐ Position und Größe des Ausschnitts auf dem Monitor,
Byte 2-3  y       │ vorzeichenlos, Little-Endian
Byte 4-5  Breite  │
Byte 6-7  Höhe    ┘
ab Byte 8 JPEG
```

Ist der Stream heruntergeskaliert, ist das JPEG kleiner als der angegebene
Ausschnitt — der Client zeichnet es auf diese Größe.

Textnachlichten vom Agent:

```jsonc
{"t":"meta","monitor":0,"width":2560,"height":1440,"fps":30}
{"t":"stats","fps":29.8,"kbps":2400,"quality":75,"scale":1,"mode":"auto"}
{"t":"unavailable","message":"…"}   // Sperrbildschirm, UAC, Vollbildspiel
{"t":"available"}                   // Aufnahme geht wieder
{"t":"error","message":"…"}
```

Befehle der App:

```jsonc
{"t":"refresh"}                     // nächstes Bild vollständig
{"t":"pause"}                       // App im Hintergrund
{"t":"resume"}
{"t":"quality","value":"auto"}      // auto | high | medium | low
```

Im Modus `auto` regelt der Agent Qualität und Auflösung selbst: bleibt ein Bild
länger unterwegs als das Zeitbudget der eingestellten Bildrate, geht er eine
Stufe herunter; nach anhaltend schnellen Bildern wieder herauf.

### Medien-Sitzungen

`/api/media/sessions` liest die Windows-Medienübersicht aus — dieselbe Quelle,
aus der die Einblendung beim Drücken der Lautstärketasten ihre Titel bezieht.
Jede App, die sich dort anmeldet (Spotify, Browser, Windows-Medienplayer, VLC),
taucht mit Titel, Interpret und Wiedergabestatus auf.

`POST /api/media` nimmt zusätzlich ein Feld `session`. Ist es gesetzt, geht der
Befehl direkt an diese App; sonst wird die Medien-Taste gedrückt, die immer bei
der Anwendung landet, die Windows gerade für die vorderste hält. Lautstärke
läuft grundsätzlich über die Taste — die Sitzungs-Schnittstelle kennt sie nicht.

## Aktionen

Was dieser Rechner auf Zuruf tun darf, steht in `actions.json` neben der
`appsettings.json`. Vorlage: `actions.example.json` — umbenennen und anpassen.
Fehlt die Datei, gibt es eben keine Aktionen; der Agent startet trotzdem.

**Die eine Regel: deklariert wird hier, aufgerufen wird per Kennung.** Der
Client schickt nie eine Kommandozeile, sondern `POST /api/actions/backup/invoke`.
Was „backup" bedeutet, weiß ausschließlich dieser Rechner. Alles andere wäre
absichtlich gebaute Remote-Code-Execution.

| `type` | Was passiert | Pflichtfelder |
|---|---|---|
| `process` | Programm starten, Argumente einzeln | `file`, optional `args`, `workingDirectory` |
| `script` | `powershell -NoProfile -ExecutionPolicy Bypass -File <datei>` | `file` (muss auf `.ps1` enden) |
| `keys` | Tastenkombination senden | `chord` (Namen wie in `Native/VirtualKeys.cs`) |
| `url` | Adresse im Standardbrowser öffnen | `url` (nur `http`/`https`) |
| `sequence` | Andere Aktionen der Reihe nach | `steps` mit `action` **oder** `delayMs` (0–60000) |

`confirm: true` bittet den Client um eine Rückfrage. Der Agent führt trotzdem
aus, wenn der Aufruf kommt — der Merker schützt vor dem verrutschten Daumen,
nicht vor einem bösen Client. Davor schützt die Kopplung.

### Geprüft wird beim Start, nicht beim Auslösen

Ein unbekannter `type`, eine Datei, die es nicht gibt, `args` als Zeichenkette
statt als Array, eine Sequenz, die sich im Kreis aufruft — alles das beendet den
Agent beim Start mit einer Meldung im Klartext. Der Tippfehler soll auffallen,
solange jemand am Rechner sitzt, und nicht Wochen später, wenn der Knopf am
Handy nichts tut und niemand weiß, warum.

### Bearbeiten geht nur hier

Es gibt **keinen** Endpunkt, der `actions.json` über das Netz beschreibt. Ein
solcher hieße „jeder gültige Ausweis darf beliebigen Code auf diesem Rechner
hinterlegen" und machte die Regel oben wertlos. Neue Aktionen anlegen heißt: an
diesen Rechner gehen (oder ihn fernsteuern — dann sieht ein Mensch, was
passiert) und die Datei bearbeiten. Danach den Agent neu starten, denn gelesen
wird sie beim Start.

## H.264 statt JPEG (optional, aber deutlich besser)

Der Agent bringt beide Wege mit. Die App versucht zuerst H.264 über WebRTC und
fällt still auf JPEG zurück, wenn etwas fehlt. Für H.264 braucht es **ffmpeg**
auf dem Rechner:

```powershell
winget install Gyan.FFmpeg
```

Danach den Agent neu starten. Liegt ffmpeg nicht im `PATH`, den Pfad in
`appsettings.json` eintragen:

```jsonc
{
  "Agent": {
    "FfmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe"
  }
}
```

Der Agent probiert die Encoder in dieser Reihenfolge durch und nimmt den
ersten, der wirklich Bilder liefert: `h264_nvenc` (NVIDIA), `h264_qsv` (Intel),
`h264_amf` (AMD), `libx264` (CPU). Welcher es geworden ist, steht in der
Statistik-Anzeige der App.

## Selbst-Update (optional)

Ist der Hub eingetragen, prüft der Agent **15 Sekunden nach jedem Start**, ob
auf der NAS eine neuere Datei liegt, lädt sie, vergleicht die SHA-256-Summe,
tauscht sich selbst aus und startet neu — alles ohne Zutun. Danach wird nicht
mehr geprüft: ein laufender Agent soll sich nicht mitten in einer Sitzung
wegtauschen, und der Weg zu einer neuen Version ist ohnehin ein Neustart. Die
Datei `RemoteDesktopAgent.exe.update` merkt sich die zuletzt versuchte Version
und verhindert eine Neustartschleife, falls der Tausch scheitert.

Ohne diese beiden Zeilen bleibt das Update aus:

```jsonc
{
  "Agent": {
    "HubUrl": "http://nas:3080",
    "HubToken": "<hubToken aus devices.json>"
  }
}
```

Veröffentlicht wird, indem die neue `RemoteDesktopAgent.exe` auf der NAS unter
`/volume1/docker/remotedesktop/dist/` abgelegt wird — der Hub reicht sie über
`/api/agent/manifest` und `/api/agent/download` weiter, beides hinter dem
Hub-Token.

## Neu bauen (nur bei Codeänderungen)

Passiert auf der NAS, nicht auf Windows:

```bash
cd /workspace/RemoteDesktop/agent

# Das SDK liegt nicht im PATH, und dem Container fehlen die ICU-Bibliotheken —
# ohne die zweite Zeile stürzt schon die dotnet-CLI beim Start ab.
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

dotnet publish -c Release -r win-x64 --self-contained
cp bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/RemoteDesktopAgent.exe \
   /volume1/docker/remotedesktop/dist/
```

Ab dann holen sich beide Rechner die neue Fassung beim nächsten Start von
selbst. Wer nicht warten will: Agent auf dem Rechner beenden und neu starten.
