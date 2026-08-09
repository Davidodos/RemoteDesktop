import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App.tsx'
import { isCapacitor, loadCapacitorPlatform } from './platform/capacitor.ts'
import { setPlatform } from './platform/index.ts'
import { webview2Platform } from './platform/webview2.ts'
import { registerServiceWorker, removeServiceWorker } from './serviceWorker.ts'
import './styles.css'

// Die Plattform steht fest, bevor React startet: die Ansichten fragen schon
// beim ersten Rendern nach den Fähigkeiten, und der Speicher der APK muss zu
// diesem Zeitpunkt eingelesen sein. Läuft die App weder im Windows-Fenster noch
// als APK, bleibt es bei der Vorgabe `web.ts`.
async function choosePlatform(): Promise<'web' | 'packaged'> {
  const host = window.remoteDesktopHost

  if (host !== undefined) {
    setPlatform(webview2Platform(host))

    return 'packaged'
  }

  if (isCapacitor()) {
    setPlatform(await loadCapacitorPlatform())

    return 'packaged'
  }

  return 'web'
}

const container = document.getElementById('root')

if (container === null) {
  throw new Error('Element #root fehlt in index.html.')
}

void choosePlatform().then((where) => {
  createRoot(container).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )

  // Erst rendern, dann aufräumen: die App liegt damit vollständig im
  // Speicher, und der Zwischenspeicher wird nicht unter ihr weggezogen.
  // Warum der Worker in einer APK nichts zu suchen hat, steht in
  // `serviceWorker.ts` — er hat dort jedes Update um einen Start verzögert.
  if (where === 'packaged') {
    void removeServiceWorker()

    return
  }

  registerServiceWorker()
})
