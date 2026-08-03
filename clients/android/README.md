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

```bash
cd clients/android
npm install
npm run sync    # baut app/dist und kopiert es nach android/
npm run apk     # ergibt android/app/build/outputs/apk/debug/app-debug.apk
```

`npm run open` öffnet dasselbe Projekt in Android Studio.

Das geht auch im Entwicklungscontainer — das Android-SDK ist reines Java und
braucht weder Windows noch ein angeschlossenes Gerät. Nötig sind einmalig:

```bash
# JDK 21 (AGP 8.13 verlangt mindestens 17)
mkdir -p ~/.jdk && cd ~/.jdk
curl -sL -o jdk.tar.gz "https://api.adoptium.net/v3/binary/latest/21/ga/linux/x64/jdk/hotspot/normal/eclipse"
tar xzf jdk.tar.gz && rm jdk.tar.gz

# Android-Command-line-Tools
mkdir -p ~/android-sdk/cmdline-tools && cd ~/android-sdk/cmdline-tools
curl -sL -o clt.zip "https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip"
python3 -c "import zipfile; zipfile.ZipFile('clt.zip').extractall('.')"
mv cmdline-tools latest && rm clt.zip && chmod +x latest/bin/*

export JAVA_HOME=~/.jdk/jdk-21.0.12+8
export PATH="$JAVA_HOME/bin:$PATH"
export ANDROID_HOME=~/android-sdk

yes | sdkmanager --sdk_root=$ANDROID_HOME --licenses
sdkmanager --sdk_root=$ANDROID_HOME "platform-tools" "platforms;android-36" "build-tools;36.0.0"
echo "sdk.dir=$HOME/android-sdk" > android/local.properties
```

Der erste Lauf dauert rund sechs Minuten, weil Gradle sich selbst und die
Abhängigkeiten holt; danach sind es Sekunden. `local.properties` steht in
`.gitignore` — der SDK-Pfad ist rechnerspezifisch.

Was der Container **nicht** kann: die APK ausführen. Für alles, was ein Gerät
braucht (Gesten, Kamera, Verhalten beim Wegwischen), bleibt es beim Handy.

**`npm run sync` nach jeder Änderung an `app/`.** Die Oberfläche ist kein Teil
des Gradle-Builds; ohne den Lauf steckt in der APK die vorige Fassung.

Der Kotlin-Anteil unter `surfaces/` hat einen eigenen Testlauf, und der läuft
hier ebenfalls:

```bash
cd android && ./gradlew testDebugUnitTest
```

Geprüft wird darin, was ohne Gerät prüfbar ist und wehtut, wenn es falsch ist:
dass die Unterschrift des Handys im Format ankommt, das der Agent erwartet, und
dass der Steckbrief aus `app/src/lib/surfaceBoard.ts` drüben so gelesen wird,
wie er geschrieben wurde. Dafür liegt die echte `org.json` als Testabhängigkeit
dabei — die aus `android.jar` ist im JVM-Testlauf eine leere Hülle.

## Was hier von Hand steht

Alles unter `android/` außer diesen Dateien stammt aus der Vorlage von
`npx cap add android` und wird von `cap sync` gepflegt:

| Datei | Warum |
|---|---|
| `SessionService.java` | Der Vordergrunddienst samt Benachrichtigung |
| `SessionServicePlugin.java` | Die Brücke, über die die App ihn startet und stoppt |
| `AppUpdatePlugin.java`, `ApkInstaller.java` | Fassung ablesen und eine neue APK installieren (Phase 14) |
| `surfaces/` | Widget, Quick-Settings-Kachel und App-Kürzel — in Kotlin (Phase 15) |
| `MainActivity.java` | Meldet die Plugins an — App-eigene Plugins findet Capacitor nicht von allein |
| `AndroidManifest.xml` | Dienst samt Typ `dataSync`, die drei Flächen, Rechte für Benachrichtigung, Kamera und Installation |
| `res/values/strings.xml`, `res/layout/widget_*`, `res/xml/widget_actions_info.xml` | Texte der Benachrichtigung, Aussehen des Widgets |
| `variables.gradle`, `app/build.gradle` | `minSdk` 26, Kotlin-Plugin, die echte `org.json` für den Testlauf |

Die JS-Seite spricht die Plugins über ihren Namen an (`registerPlugin`), nicht
über deren TypeScript-Aufsätze. Dadurch braucht `app/` nur `@capacitor/core` und
zieht sich für die PWA nicht den Web-Ersatz des QR-Scanners samt `html5-qrcode`
ins Bündel. Die Plugin-Pakete selbst stehen hier in der `package.json` — von
dort liest `cap sync` sie und hängt die Gradle-Module ein.

## Grenzen

- **`minSdk` ist 26** (Android 8, 2017) und nicht die 24 der Capacitor-Vorlage.
  Der QR-Scanner bringt `io.ionic.libs:ionbarcode-android` mit, das 26 verlangt;
  darunter bricht der Manifest-Merger den Build ab.
- **Die APK ist rund 34 MB.** Den Löwenanteil stellen MLKit-Barcode und CameraX
  aus dem Scanner-Plugin. Beim Sideload ist das gleichgültig; sollte es je
  stören, wäre der ABI-Split der erste Griff.
- **iOS ist bewusst außen vor** (`docs/PLAN-V2.md`, Abschnitt 1): 99 $/Jahr, kein
  Sideload, alle sieben Tage neu signieren.
- **Der Vordergrunddienst ist vom Typ `dataSync`.** `connectedDevice` verlangt
  seit Android 14 zusätzlich eine Erlaubnis aus einer festen Liste (Bluetooth,
  NFC, `CHANGE_NETWORK_STATE`, USB …); ohne sie wirft `startForeground` und
  nimmt die App mit. Übertragen wird hier fortlaufend Bild und Eingabe über das
  Netz — dafür ist `dataSync` gedacht. Ab Android 15 gilt dafür eine Grenze von
  sechs Stunden pro Tag; für Fernsteuerungs-Sitzungen reicht das.
- **Kein Play Store.** Ein Vordergrunddienst wäre dort begründungspflichtig;
  beim Sideload ist er es nicht.
- **Signiert wird nicht.** Es gibt nur `assembleDebug`; ein Release-Keystore
  gehört zu Phase 14 (Updates über GitHub).
