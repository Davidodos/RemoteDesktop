/**
 * Der Service Worker — im Browser ja, in der APK nein.
 *
 * **Der Befund dahinter:** die APK lief nach jedem Update **eine Startphase
 * hinterher**. Neue Fassung installieren, App-Details zeigen die neue
 * Versionsnummer, die Oberfläche ist die alte; dieselbe APK ein zweites Mal
 * ausführen, und plötzlich ist alles da. Am echten Gerät kostete das eine
 * ganze Fehlersuche, weil beides zugleich stimmte: „die Version ist neu" und
 * „die Funktionen fehlen".
 *
 * Der Grund ist der Service Worker. Capacitor liefert die App unter
 * `https://localhost` aus — ein sicherer Kontext, also registriert sich der
 * Worker der PWA auch dort. Er beantwortet jede Anfrage aus seinem eigenen
 * Zwischenspeicher, und der stammt vom letzten Start. Beim ersten Start nach
 * einem Update kommt deshalb die alte Oberfläche; der neue Worker installiert
 * sich dabei im Hintergrund und übernimmt erst beim nächsten Mal.
 *
 * **In einer APK ist er ohnehin sinnlos.** Er ist dafür da, eine Web-Seite
 * ohne Netz benutzbar zu machen — die APK trägt ihre Dateien selbst bei sich
 * und ist ohne Netz sowieso vollständig. Er spart hier nichts und kostet genau
 * diesen Fehler. Dasselbe gilt für das Windows-Fenster.
 *
 * Für die PWA im Browser bleibt er, was er ist: der Grund, warum sie sich vom
 * Homescreen starten lässt.
 */

/** Wo die erzeugte Datei liegt — `vite-plugin-pwa` legt sie in die Wurzel. */
const SCRIPT_URL = '/sw.js'

/**
 * Im Browser: registrieren. Ohne Unterstützung passiert nichts — dann ist es
 * eben eine gewöhnliche Seite.
 */
export function registerServiceWorker(): void {
  if (!('serviceWorker' in navigator)) {
    return
  }

  // Ein Fehlschlag ist kein Grund, die App nicht zu starten: sie funktioniert
  // auch ohne ihn, nur eben nicht offline.
  void navigator.serviceWorker.register(SCRIPT_URL).catch(() => undefined)
}

/**
 * In APK und Windows-Fenster: abmelden und den Zwischenspeicher wegräumen.
 *
 * Nicht bloß „nicht registrieren": wer die App schon hat, hat auch schon einen
 * angemeldeten Worker, und der bliebe sonst für immer stehen und lieferte für
 * immer die Fassung von vorgestern. Der eine Start, an dem diese Zeilen zum
 * ersten Mal laufen, ist der letzte mit dem alten Verhalten.
 *
 * Aufgeräumt wird erst, nachdem alles geladen ist — die App besteht aus einem
 * Bündel, und das liegt zu diesem Zeitpunkt längst im Speicher.
 */
export async function removeServiceWorker(): Promise<void> {
  try {
    if ('serviceWorker' in navigator) {
      const registrations = await navigator.serviceWorker.getRegistrations()

      await Promise.all(registrations.map((registration) => registration.unregister()))
    }

    if ('caches' in globalThis) {
      const names = await caches.keys()

      await Promise.all(names.map((name) => caches.delete(name)))
    }
  } catch {
    // Ein Zwischenspeicher, der sich nicht leeren lässt, ist unschön und kein
    // Grund für eine Meldung. Beim nächsten Start noch einmal.
  }
}
