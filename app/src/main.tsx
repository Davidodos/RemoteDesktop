import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App.tsx'
import { isCapacitor, loadCapacitorPlatform } from './platform/capacitor.ts'
import { setPlatform } from './platform/index.ts'
import { webview2Platform } from './platform/webview2.ts'
import { removeServiceWorker } from './serviceWorker.ts'
import './styles.css'

// Die Plattform steht fest, bevor React startet: die Ansichten fragen schon
// beim ersten Rendern nach den Fähigkeiten, und der Speicher der APK muss zu
// diesem Zeitpunkt eingelesen sein. Läuft die App weder im Windows-Fenster noch
// als APK, bleibt es bei der Vorgabe `web.ts`.
async function choosePlatform(): Promise<void> {
  const host = window.remoteDesktopHost

  if (host !== undefined) {
    setPlatform(webview2Platform(host))

    return
  }

  if (isCapacitor()) {
    setPlatform(await loadCapacitorPlatform())
  }
}

const container = document.getElementById('root')

if (container === null) {
  throw new Error('Element #root fehlt in index.html.')
}

void choosePlatform().then(() => {
  createRoot(container).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )

  // Erst rendern, dann aufräumen: die App liegt damit vollständig im Speicher,
  // und der Zwischenspeicher wird nicht unter ihr weggezogen. Warum es hier
  // keinen Service Worker mehr gibt, steht in `serviceWorker.ts` — er hat jedes
  // Update um einen Start verzögert.
  void removeServiceWorker()
})
