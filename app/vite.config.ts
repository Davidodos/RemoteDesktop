import react from '@vitejs/plugin-react'
// Aus vitest/config statt aus vite — nur diese Variante kennt den test-Block.
import { defineConfig } from 'vitest/config'

/*
  Kein `vite-plugin-pwa` mehr — und das ist eine Entscheidung, keine Auslassung.

  Die App wird auf zwei Wegen ausgeliefert, und beide tragen ihre Dateien selbst
  bei sich: die APK packt `app/dist` ein, das Windows-Fenster liest es von der
  Platte. Über HTTP serviert sie niemand mehr, seit der Waker das nicht mehr tut
  (Phase 14). Ein Service Worker hatte damit nichts zwischenzuspeichern, was
  nicht ohnehin schon lokal lag — er kostete aber jedes Update einen zusätzlichen
  Start: neue Fassung installiert, alte Oberfläche, und erst beim zweiten Start
  war sie da.

  Wer die App wieder im Browser vom Homescreen starten will, holt das Plugin
  zurück; dann gehört auch ein Manifest mit den Symbolen aus `public/` dazu.
*/
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
  },
  test: {
    // Der Eingabe-Kanal hängt an window.setTimeout und requestAnimationFrame —
    // ohne DOM-Umgebung ließe sich sein Verhalten nicht prüfen.
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
  },
})
