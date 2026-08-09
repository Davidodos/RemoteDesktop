import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
// Aus vitest/config statt aus vite — nur diese Variante kennt den test-Block.
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',

      // Nicht von allein in die index.html schreiben: die APK und das
      // Windows-Fenster dürfen den Worker gar nicht erst anmelden — bei ihnen
      // liegt die App in der Anwendung selbst, und der Worker verzögerte jedes
      // Update um einen Start. Registriert wird deshalb in `main.tsx`, wo die
      // Plattform bekannt ist.
      injectRegister: null,
      manifest: {
        name: 'RemoteDesktop',
        short_name: 'Remote',
        lang: 'de',
        description: 'PC und Laptop vom Handy steuern',
        theme_color: '#101418',
        background_color: '#101418',
        // Kein Fullscreen: die Android-Navigationsleiste soll bleiben.
        display: 'standalone',
        orientation: 'any',
        start_url: '/',
        icons: [
          { src: 'icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: 'icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: 'icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // Nur die App-Hülle cachen. API-Antworten wären hier schädlich:
        // ein gecachter Online-Status oder eine veraltete Monitor-Liste
        // führen direkt zu Fehlbedienung.
        globPatterns: ['**/*.{js,css,html,png,svg,woff2}'],
        navigateFallbackDenylist: [/^\/api/, /^\/health/],
      },
    }),
  ],
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
