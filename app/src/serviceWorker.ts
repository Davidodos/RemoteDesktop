/**
 * Den Service Worker abmelden, den frühere Fassungen hinterlassen haben.
 *
 * **Der Befund dahinter:** die APK lief nach jedem Update **eine Startphase
 * hinterher**. Neue Fassung installieren, App-Details zeigen die neue
 * Versionsnummer, die Oberfläche ist die alte; dieselbe APK ein zweites Mal
 * ausführen, und plötzlich ist alles da. Am echten Gerät kostete das eine ganze
 * Fehlersuche, weil beides zugleich stimmte: „die Version ist neu" und „die
 * Funktionen fehlen".
 *
 * Der Grund war der Service Worker der PWA. Capacitor liefert die App unter
 * `https://localhost` aus — ein sicherer Kontext, also registrierte er sich auch
 * in der APK. Danach beantwortete er jeden Start aus seinem eigenen
 * Zwischenspeicher, und der stammte vom letzten Mal; der neue Worker
 * installierte sich im Hintergrund und übernahm erst beim nächsten Start.
 *
 * Erzeugt wird jetzt keiner mehr (siehe `vite.config.ts`). Diese Datei räumt
 * auf, was auf bereits installierten Geräten noch angemeldet ist — ohne sie
 * bliebe der alte Worker dort für immer stehen und lieferte für immer die
 * Fassung von vorgestern. Sie darf erst verschwinden, wenn keine Installation
 * älter als v1.3.6 mehr im Umlauf ist.
 */

/**
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
