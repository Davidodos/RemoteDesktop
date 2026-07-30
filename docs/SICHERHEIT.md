# Sicherheits-Durchsicht

Stand: 28. Juli 2026, nach Phase 6. Betrachtet wurden Agent, Hub und PWA.

Die Ausgangslage bestimmt alles Weitere: **der Agent hat vollständige Kontrolle
über den PC** — Maus, Tastatur, Herunterfahren, Bildschirminhalt. Wer ihn
erreicht und das Token kennt, sitzt praktisch am Rechner.

## Schutzschichten

1. **Tailscale.** Nichts ist aus dem Internet erreichbar. Kein Port-Forwarding,
   keine öffentliche IP. Ein Angreifer muss zuerst im Tailnet sein.
2. **Token.** Jeder Endpoint des Agents verlangt ein Bearer-Token von
   mindestens 32 Zeichen; der Vergleich läuft in fester Zeit
   (`CryptographicOperations.FixedTimeEquals`). Der Hub hat ein eigenes,
   davon unabhängiges Token.
3. **TLS.** Der Agent bedient ausschließlich HTTPS mit einem echten
   Tailscale-Zertifikat. Die WebSockets laufen darüber.

## Befunde

### Behoben

| Schwere | Befund |
|---|---|
| Hoch | **Preflight lief in die Token-Sperre.** Der Browser schickt `OPTIONS` grundsätzlich ohne `Authorization`; die Middleware antwortete mit 401. Ergebnis: sämtliche REST-Aufrufe der App scheiterten lautlos, während die WebSockets liefen. Behoben in `TokenAuth`, zusammen mit der CORS-Freigabe. |
| Mittel | **Token im Query-String der WebSockets.** Unvermeidlich — Browser können bei WebSocket-Verbindungen keine Header setzen. Abgemildert: der Agent loggt keine Query-Strings, und die Verbindung ist TLS-verschlüsselt. |
| Mittel | **Selbst-Update als Einfallstor.** Der Download läuft nur über HTTPS gegen den konfigurierten Hub, ist mit dem Hub-Token geschützt, und die Datei wird vor dem Tausch gegen die SHA-256-Summe aus dem Manifest geprüft. Passt sie nicht, wird sie gelöscht. |

### Bewusst so gelassen

**CORS erlaubt jede Herkunft.** Die App wird vom Hub ausgeliefert, spricht aber
den Agent direkt an — die Herkunft hängt also davon ab, wie der Hub gerade
aufgerufen wird (Tailnet-Name, LAN-IP, `localhost` beim Entwickeln). Eine
feste Liste wäre eine ständige Fehlerquelle für wenig Gewinn: Der Agent
autorisiert ausschließlich über das Bearer-Token und setzt keine Cookies. Eine
fremde Seite im Browser kann damit nichts erreichen, was sie nicht ohnehin
könnte — sie müsste das Token bereits kennen.

**Kein Rate-Limit am Agent.** Ein Angreifer im Tailnet könnte Tokens raten.
Bei 32 zufälligen Zeichen ist das aussichtslos, und ein Limit würde bei
schneller Eingabe (Tastatur-Stream) im Weg stehen.

**ffmpeg wird als Prozess gestartet.** Der Pfad kommt aus der Konfiguration,
nie aus einer Anfrage. Monitor-Index und Bildrate werden vor dem Einsetzen auf
Zahlenbereiche geklemmt — es gibt keinen Weg, eigene Argumente einzuschleusen.

### Offen

**Sperrbildschirm und UAC.** Der Agent läuft als gewöhnlicher Benutzerprozess
und kommt an den sicheren Desktop nicht heran (`DesktopBinder` versucht es und
scheitert dort erwartungsgemäß). Das ist gleichzeitig eine Sicherheitseigenschaft:
Wer den PC gesperrt vorfindet, kann ihn über die App nicht entsperren. Ein
Dienst in Sitzung 0 würde diese Grenze aufheben — bewusst noch nicht gebaut.

**Tokens liegen im Klartext.** In `devices.json` auf der NAS und in der
`appsettings.json` neben dem Agent. Beide Dateien sind über `.gitignore`
ausgeschlossen. Für den Heimgebrauch angemessen; ein Secret-Manager wäre hier
Aufwand ohne echten Gewinn.

**Das Hub-Token liegt im `localStorage` des Handys.** Wer das entsperrte Handy
hat, hat den PC. Der Bildschirmsperre des Handys kommt damit dieselbe Bedeutung
zu wie dem Token selbst.

## Wenn ein Token verloren geht

1. Neues Token erzeugen: `openssl rand -hex 32`.
2. In `appsettings.json` beim Agent eintragen, Agent neu starten.
3. In `devices.json` auf der NAS eintragen, Hub neu starten.
4. In der App abmelden und neu anmelden.

Bei Verdacht auf einen fremden Zugriff im Tailnet zusätzlich im
Tailscale-Adminpanel das betroffene Gerät entfernen — das wirkt sofort und ist
die schnellere Sperre.
