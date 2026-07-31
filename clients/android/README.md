# Android-Client

Capacitor-Hülle um die React-App aus `app/`. Sie liefert dieselbe Oberfläche als
APK aus, die im Browser als PWA und unter Windows im WebView2-Fenster läuft —
der Code liegt nur einmal da, die Unterschiede stehen in
`app/src/platform/capacitor.ts`.

## Warum überhaupt eine APK

Drei Dinge, die einer PWA verwehrt sind:

- **Vordergrunddienst.** Ein Browser-Tab im Hintergrund wird gedrosselt; der
  Eingabe-Socket fällt zu und der Videostrom pausiert. `SessionService` hält die
  Sitzung offen, solange eine Verbindung besteht.
- **Kamera.** Gekoppelt wird über den QR-Code am Rechner statt über sechs
  abgetippte Ziffern.
- **Eigener Speicher.** Die Preferences verschwinden nicht mit den Browserdaten.

Was sie *nicht* bringt: eine schnellere Bildübertragung. Die WebView bleibt eine
WebView, und der Videoweg ist derselbe (WebRTC, H.264 über MediaCodec).

## Bauen

Voraussetzung ist ein Android-SDK samt JDK 21 — im Entwicklungscontainer gibt es
beides nicht, gebaut wird auf einem Rechner mit Android Studio.

```bash
cd clients/android
npm install
npm run sync    # baut app/dist und kopiert es nach android/
npm run apk     # ergibt android/app/build/outputs/apk/debug/app-debug.apk
```

`npm run open` öffnet dasselbe Projekt in Android Studio.

**`npm run sync` nach jeder Änderung an `app/`.** Die Oberfläche ist kein Teil
des Gradle-Builds; ohne den Lauf steckt in der APK die vorige Fassung.

## Was hier von Hand steht

Alles unter `android/` außer diesen Dateien stammt aus der Vorlage von
`npx cap add android` und wird von `cap sync` gepflegt:

| Datei | Warum |
|---|---|
| `SessionService.java` | Der Vordergrunddienst samt Benachrichtigung |
| `SessionServicePlugin.java` | Die Brücke, über die die App ihn startet und stoppt |
| `MainActivity.java` | Meldet das Plugin an — App-eigene Plugins findet Capacitor nicht von allein |
| `AndroidManifest.xml` | Dienst, sein Typ `connectedDevice`, Rechte für Benachrichtigung und Kamera |
| `res/values/strings.xml` | Texte der Benachrichtigung |

Die JS-Seite spricht die Plugins über ihren Namen an (`registerPlugin`), nicht
über deren TypeScript-Aufsätze. Dadurch braucht `app/` nur `@capacitor/core` und
zieht sich für die PWA nicht den Web-Ersatz des QR-Scanners samt `html5-qrcode`
ins Bündel. Die Plugin-Pakete selbst stehen hier in der `package.json` — von
dort liest `cap sync` sie und hängt die Gradle-Module ein.

## Grenzen

- **iOS ist bewusst außen vor** (`docs/PLAN-V2.md`, Abschnitt 1): 99 $/Jahr, kein
  Sideload, alle sieben Tage neu signieren.
- **Kein Play Store.** Der Typ `connectedDevice` eines Vordergrunddienstes wäre
  dort begründungspflichtig; beim Sideload ist er es nicht.
- **Signiert wird nicht.** Es gibt nur `assembleDebug`; ein Release-Keystore
  gehört zu Phase 14 (Updates über GitHub).
