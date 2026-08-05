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

## Verteilen

```bash
cd app && npm run build
cd ../desktop
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

rm -rf /volume1/docker/remotedesktop/dist/client
mkdir -p /volume1/docker/remotedesktop/dist/client
cp bin/Release/net8.0-windows/win-x64/publish/RemoteDesktop.exe \
   /volume1/docker/remotedesktop/dist/client/
cp -r bin/Release/net8.0-windows/win-x64/publish/app \
   /volume1/docker/remotedesktop/dist/client/
```

Auf dem Windows-Rechner den ganzen Ordner `client\` irgendwohin kopieren, wo
man schreiben darf. **Der Unterordner `app\` muss mit** — er ist die
Oberfläche, und `WebAppLocator` sucht sie neben der `.exe`.

Anders als der Agent braucht der Client **keinen** Scheduled Task. Er muss
nicht erhöht laufen und schickt niemandem Eingaben; eine Verknüpfung in
`shell:startup` genügt, wenn er beim Anmelden mitkommen soll.

Die `.xml`-Dateien aus dem Publish-Ordner sind die IntelliSense-Doku von
WebView2 und bleiben liegen.

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
| Geräte koppeln… | Kopplungscode und QR-Code anzeigen, gekoppelte Geräte auflisten und widerrufen |
| Beenden | Wirklich beenden |

## Der QR-Code

„Code anzeigen“ zeigt dieselben sechs Ziffern zweimal: als Text zum Abtippen und
als QR-Code, den die APK aus `clients/android/` scannt. Der Link darin
(`remotedesktop://pair?host=…&port=…&code=…`) kommt fertig vom Agent —
`agent/Auth/PairingUri.cs` erzeugt ihn, `app/src/lib/pairingUri.ts` liest ihn.
Dieses Fenster zeichnet ihn nur.

Das ist Absicht: nur der Agent kennt seinen eigenen Port verlässlich, und beide
Seiten des Formats liegen damit an Stellen, auf denen Tests sitzen. Antwortet
ein älterer Agent ohne Link, bleibt der Kasten leer und es geht wie bisher über
die Ziffern.

Ein Geheimnis wird der QR-Code dadurch nicht: er trägt denselben Code, der
ohnehin auf dem Bildschirm steht — fünf Minuten gültig, einmal verwendbar.

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
