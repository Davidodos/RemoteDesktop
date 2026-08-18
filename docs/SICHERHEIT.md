# Sicherheits-Durchsicht

Stand: 1. August 2026, nach Phase 14. Betrachtet wurden Agent, Waker und die
beiden Clients.

Die Ausgangslage bestimmt alles Weitere: **der Agent hat vollständige Kontrolle
über den PC** — Maus, Tastatur, Herunterfahren, Bildschirminhalt. Wer ihn
erreicht und das Token kennt, sitzt praktisch am Rechner.

## Schutzschichten

1. **Das Netz.** Nichts ist aus dem Internet erreichbar. Kein Port-Forwarding,
   keine öffentliche IP. Ein Angreifer muss zuerst im selben Netz sein — im
   Heimnetz also im WLAN, bei Tailscale im Tailnet, bei einem eigenen VPN
   darin. Seit V3 ist das eine Wahl (`setup/NetworkProfile.cs`) und keine
   Voraussetzung mehr; die Schutzwirkung ist im Heimnetz naturgemäß geringer
   als in einem VPN — wer im WLAN steht, ist schon drin.
2. **Kopplung pro Gerät.** Seit Phase 10 gibt es kein geteiltes Token mehr:
   jeder Client hat ein eigenes Schlüsselpaar, der Agent kennt nur den
   öffentlichen Teil (`clients.json`), und angemeldet wird per
   Challenge-Response. Das Sitzungstoken gilt zwölf Stunden und liegt
   ausschließlich im Arbeitsspeicher des Agents. Der Vergleich läuft in fester
   Zeit (`CryptographicOperations.FixedTimeEquals`).
3. **Rechte pro Client.** `screen`, `input`, `media`, `power`, `actions`,
   `wake` — als Whitelist. Ein Pfad, der nicht zugeordnet ist, wird abgelehnt
   statt durchgelassen.
4. **TLS.** Agent und Waker bedienen ausschließlich HTTPS; die WebSockets
   laufen darüber. Wo Tailscale läuft, kommt das Zertifikat von dort und ist
   öffentlich anerkannt. Sonst stellt sich der Agent seit V3 selbst eins aus
   (`agent/Services/SelfSignedCertificate.cs`): eine eigene Stelle mit zehn
   Jahren Laufzeit, die ein Serverzertifikat über 825 Tage unterschreibt und
   still erneuert. Ein Client nimmt sie erst an, nachdem ein Mensch ihren
   Fingerabdruck bestätigt hat — der kommt über die Kopplung, also über den
   Bildschirm des Rechners.
5. **Der Vertrauens-Port (8442).** Er wird nur geöffnet, wenn es eine eigene
   Stelle gibt, und trägt genau eine Datei: `/ca.crt`, das öffentliche
   Zertifikat. Alles andere dort ist 404. Unverschlüsselt, weil es anders nicht
   geht — die verschlüsselte Verbindung ist ja gerade die, die ohne dieses
   Zertifikat nicht zustande kommt. Ein Angreifer, der die Datei austauscht,
   scheitert am Fingerabdruck; einer, der sie mitliest, erfährt nichts, was
   nicht ohnehin jeder Verbindungsaufbau preisgibt.

## Befunde

### Behoben

| Schwere | Befund |
|---|---|
| Hoch | **Preflight lief in die Token-Sperre.** Der Browser schickt `OPTIONS` grundsätzlich ohne `Authorization`; die Middleware antwortete mit 401. Ergebnis: sämtliche REST-Aufrufe der App scheiterten lautlos, während die WebSockets liefen. Behoben in `TokenAuth`, zusammen mit der CORS-Freigabe. |
| Mittel | **Token im Query-String der WebSockets.** Unvermeidlich — Browser können bei WebSocket-Verbindungen keine Header setzen. Abgemildert: der Agent loggt keine Query-Strings, und die Verbindung ist TLS-verschlüsselt. |
| Mittel | **Selbst-Update als Einfallstor.** Bis Phase 13 kam die Datei vom Hub, geprüft wurde nur die SHA-256-Summe aus dem Manifest daneben. Seit Phase 14 kommt sie aus den GitHub-Releases, und das Manifest ist mit ECDSA P-256 unterschrieben; der öffentliche Schlüssel ist in den Agent kompiliert. Ein Hash aus derselben Quelle wie die Datei schützt gegen abgebrochene Downloads, nicht gegen ein übernommenes GitHub-Konto — die Signatur schon. |
| **Hoch** | **Der Hub bündelte alle Agent-Tokens.** Er lieferte über `GET /api/devices` die Tokens **aller** Rechner an jeden aus, der das eine Hub-Token kannte — ein einziges Geheimnis, hinter dem sämtliche Rechner lagen, und es lag im `localStorage` des Handys. Mit Phase 14 ist die Registry ersatzlos entfallen: der Waker führt keine Geräteliste, kennt keine MACs und keine Tokens. Die App bringt ihre gekoppelten Geräte selbst mit. |

### Bewusst so gelassen

**CORS erlaubt jede Herkunft.** Die App läuft als APK (`https://localhost`) und
als WebView2-Fenster (`https://app.remotedesktop.invalid`), spricht aber Agent
und Waker direkt an — jeder Aufruf ist Cross-Origin. Eine feste Liste wäre eine
ständige Fehlerquelle für wenig Gewinn: autorisiert wird ausschließlich über
das Sitzungstoken, Cookies gibt es nicht. Eine fremde Seite im Browser kann
damit nichts erreichen, was sie nicht ohnehin könnte.

**Kein Rate-Limit am Agent.** Ein Angreifer im Tailnet könnte Tokens raten.
Bei 32 zufälligen Zeichen ist das aussichtslos, und ein Limit würde bei
schneller Eingabe (Tastatur-Stream) im Weg stehen.

**ffmpeg wird als Prozess gestartet.** Der Pfad kommt aus der Konfiguration,
nie aus einer Anfrage. Monitor-Index und Bildrate werden vor dem Einsetzen auf
Zahlenbereiche geklemmt — es gibt keinen Weg, eigene Argumente einzuschleusen.

**Aktionen führen Programme aus.** Das ist ihr Zweck. Was ausgeführt werden
darf, steht ausschließlich in der `actions.json` auf dem Rechner selbst; über
das Netz gibt es keinen Schreibweg, und der Client schickt nie eine
Kommandozeile, sondern nur eine Kennung. Argumente gehen als Array hinaus, nie
über eine Shell (`UseShellExecute` steht überall auf `false`).

**Die Android-Flächen lösen Aktionen aus, ohne dass die App läuft.** Widget,
Quick-Settings-Kachel und App-Kürzel melden sich mit demselben Geräteschlüssel
an wie die App — sie umgehen die Kopplung nicht, sie benutzen sie. Drei
Entscheidungen halten das eng: der private Schlüssel wird **nicht kopiert**,
sondern dort gelesen, wo die App ihn hält; es wird **kein Sitzungstoken
gemerkt**, jeder Tipp meldet sich neu an; und Widget-Rundruf wie
`ShortcutRelay` sind **nicht nach außen freigegeben**, damit keine andere App
auf dem Handy in unserem Namen etwas auf dem PC startet. Aktionen mit
`confirm` erscheinen auf keiner Fläche — eine Rückfrage lässt sich dort nicht
stellen, und der Merker aus Phase 13 wäre sonst still ausgehebelt.

**Wecken ist absichtlich schwach abgesichert.** `POST /api/wol` verlangt das
Recht `wake` wie jeder andere Endpunkt, aber der Schaden wäre ohnehin gering:
ein Magic Packet kann nur einschalten und wirkt nur im eigenen Netz. Die
Begrenzung auf zehn Versuche pro Minute ist kein Schutz vor Gekoppelten,
sondern verhindert, dass Agent oder Waker als Paket-Verstärker taugen.

**Die Standort-Kennung ist ein Hash.** Gemeldet wird `sha256(gatewayMac)` und
nicht die MAC selbst. Sie geht über das Netz an jeden gekoppelten Client, und
ein Vergleichswert reicht für ihren Zweck.

**Die APK aktualisiert sich ohne eigene Signaturprüfung.** Sie braucht keine:
Android lässt eine APK nur über eine installierte drüber, wenn sie mit
demselben Schlüssel unterschrieben ist, und zeigt außerhalb von Google Play
immer einen Bestätigungsdialog. Beides zusammen ist stärker als eine
selbstgebaute Prüfung.

**Ein gekoppeltes Gerät darf den Rechner aktualisieren** — seit 31m, über
`POST /api/update/app`. Der Agent lädt dabei den Installer aus dem Release und
führt ihn **mit vollen Rechten und ohne Rückfrage von Windows** aus. Das ist
kein Loch in der Rechteverwaltung: der Agent läuft als geplante Aufgabe mit
`HighestAvailable`, ein Prozess, den er startet, erbt diesen Token, und wer
`power` hat, darf den Rechner ohnehin herunterfahren. Es ist trotzdem der Punkt,
an dem eine heruntergeladene Datei am meisten kann.

Deshalb gilt hier dieselbe Bedingung wie beim Agent selbst und **keine
schwächere**: der Installer trägt sein eigenes Manifest im Release
(`installer.json` + `.sig`), unterschrieben mit demselben ECDSA-P-256-Schlüssel,
und ohne gültige Unterschrift *und* passende Prüfsumme wird nichts ausgeführt.
Wer das Recht `power` hat, kann ein Update auslösen — er kann nicht bestimmen,
*was* installiert wird.

### Offen

**Sperrbildschirm und UAC.** Der Agent läuft als gewöhnlicher Benutzerprozess
und kommt an den sicheren Desktop nicht heran (`DesktopBinder` versucht es und
scheitert dort erwartungsgemäß). Das ist gleichzeitig eine Sicherheitseigenschaft:
Wer den PC gesperrt vorfindet, kann ihn über die App nicht entsperren. Ein
Dienst in Sitzung 0 würde diese Grenze aufheben — bewusst noch nicht gebaut.

**`agentkey.txt` liegt im Klartext neben der `.exe`.** Darin steht der private
Schlüssel des Agents. Er gehört nach `C:\Program Files\RemoteDesktop\data\` mit
denselben ACLs wie `cert.key` — steht als offener Punkt in `docs/TASKS-V2.md`.

**Das alte geteilte Token gilt weiter.** `Agent:Token` ist seit Phase 10
freiwillig, wird aber noch angenommen, damit sich niemand vom eigenen Rechner
aussperrt. Wer die Kopplung überall durchgezogen hat, sollte die Zeile
entfernen — dann gibt es kein Geheimnis mehr, das alles darf.

**Der private Geräteschlüssel liegt in den Preferences der App.** Wer das
entsperrte Handy hat, hat den PC. Der Bildschirmsperre des Handys kommt damit
dieselbe Bedeutung zu wie dem Schlüssel selbst. Android böte mit
`EncryptedSharedPreferences` mehr; die Abwägung steht in `docs/TASKS-V2.md`,
Phase 12.

## Wenn ein Gerät verloren geht

1. Am Rechner selbst: `DELETE /api/clients/{id}` — der Widerruf wirft den
   Client zugleich aus laufenden Sitzungen, wirkt also sofort und nicht erst
   nach zwölf Stunden. Auf dem Waker dasselbe.
2. Steht in der `appsettings.json` noch `Agent:Token`, muss es ausgetauscht
   werden — es gilt für jeden, der es kennt.
3. Bei Verdacht auf einen fremden Zugriff im Tailnet zusätzlich im
   Tailscale-Adminpanel das betroffene Gerät entfernen. Das wirkt sofort und
   ist die schnellere Sperre.

## Wenn der Release-Schlüssel verloren geht

Wer ihn hat, kann jedem Agent eine beliebige `.exe` unterschieben. Neues Paar
mit `node scripts/release-key.mjs` erzeugen, den öffentlichen Teil in
`ReleaseKeys.PublicKey` eintragen — und dann **jeden Agent von Hand** auf die
neue Fassung bringen: ein Agent mit dem alten Schlüssel nimmt nichts an, was
mit dem neuen unterschrieben ist. Genau das ist der Preis dafür, dass die
Vertrauenskette nicht am GitHub-Konto hängt.
