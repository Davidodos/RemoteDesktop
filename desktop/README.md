# Windows-Client

Ein Tray-Programm mit einem WebView2-Fenster, in dem dieselbe React-App läuft
wie auf dem Handy. Damit lässt sich der eine Rechner vom anderen aus bedienen,
ohne dass es eine zweite Oberfläche zu pflegen gäbe.

Der **Agent** (`agent/`) ist davon unberührt: er bleibt ein eigener Dienst. Der
Client spricht ihn nur über die Loopback-Adresse an, und auch das nur für den
Kopplungscode und den Widerruf.

## Bauen

Die Oberfläche ist kein Teil des .NET-Builds — sie wird vorher gebaut und
danach mitkopiert:

```bash
cd app && npm run build
cd ../desktop && dotnet build
```

Ohne den ersten Schritt startet das Programm zwar, sagt aber beim Öffnen des
Fensters, dass die Oberfläche fehlt.

## Was das Fenster mehr kann als die PWA

| | Handy (PWA) | Windows-Fenster |
|---|---|---|
| Maus | Trackpad-Fläche mit nachgeführtem Zeiger | **Pointer Lock** — echte relative Bewegung |
| Tastatur | Bildschirmtastatur | **echte Tastatur**, `keydown` mit richtigen `code`-Werten |
| Zwischenablage | scheitert an der Berechtigung | in beide Richtungen |
| Hintergrund | Tab wird gedrosselt | Sitzung läuft weiter |

Die Fähigkeiten stehen in `app/src/platform/webview2.ts`; die Oberfläche fragt
dort nach, bevor sie einen Knopf anbietet.

## Bedienung

Das Programm sitzt im Tray, das Fenster geht nur auf Wunsch auf.

| Menüpunkt | Was passiert |
|---|---|
| Fenster öffnen | Die Oberfläche; das Kreuz versteckt sie wieder, statt zu beenden |
| Geräte koppeln… | Kopplungscode anzeigen, gekoppelte Geräte auflisten und widerrufen |
| Beenden | Wirklich beenden |

## Selbstverbindung

Ein Rechner, der sich selbst als Ziel wählt, zeigt sein eigenes Fenster im
eigenen Fenster und rekursiv weiter; die Eingaben laufen dabei im Kreis. Der
Client vergleicht deshalb den Hostnamen aus `/api/info` mit
`Environment.MachineName` und verweigert (`app/src/lib/selfConnection.ts`).

## Bekannte Ecken

- **WebView2-Runtime** muss vorhanden sein. Auf Windows 11 ist sie es, auf
  Windows 10 meist über Edge. Fehlt sie, kommt beim Start eine Meldung mit
  Download-Adresse statt eines stillen Absturzes.
- **SmartScreen** warnt bei jeder unsignierten .exe. Ein Code-Signing-Zertifikat
  kostet dreistellig pro Jahr; für zwei eigene Rechner ist die Warnung
  hinnehmbar — sie ist kein Fehler.
- **Der Sperrbildschirm des Zielrechners** bleibt auch von hier aus
  unerreichbar. Das liegt am Ziel, nicht am Client (siehe `docs/SICHERHEIT.md`).
- Der Agent-Port lässt sich über `REMOTEDESKTOP_AGENT_PORT` verstellen; ohne das
  sind es 8443.
