import type { CapacitorConfig } from '@capacitor/cli'

/**
 * Die Hülle liefert kein eigenes Web-Projekt aus, sondern das gebaute Bundle
 * aus `app/` — dieselbe Oberfläche, die auch die PWA und das Windows-Fenster
 * zeigen. `npm run sync` baut es und kopiert es hierher.
 */
const config: CapacitorConfig = {
  appId: 'app.remotedesktop.client',
  appName: 'RemoteDesktop',
  webDir: '../../app/dist',

  android: {
    // Ohne das lädt die WebView den Agent nicht: der spricht HTTPS mit einem
    // Zertifikat, das Android nicht kennen muss — geprüft wird stattdessen der
    // Fingerabdruck des Geräteschlüssels aus der Kopplung.
    allowMixedContent: false,
  },

  server: {
    // Die App läuft unter einer eigenen https-Herkunft statt unter file://.
    // Unter file:// hat Chromium eine sehr enge Herkunft — localStorage und die
    // Anfragen an den Agent scheiterten dort (siehe desktop/, Phase 11).
    androidScheme: 'https',
  },
}

export default config
