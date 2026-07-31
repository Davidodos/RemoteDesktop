import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App.tsx'
import { setPlatform } from './platform/index.ts'
import { webview2Platform } from './platform/webview2.ts'
import './styles.css'

// Die Plattform steht fest, bevor React startet: die Ansichten fragen schon
// beim ersten Rendern nach den Fähigkeiten. Läuft die App nicht im
// Windows-Fenster, bleibt es bei der Vorgabe `web.ts`.
const host = window.remoteDesktopHost

if (host !== undefined) {
  setPlatform(webview2Platform(host))
}

const container = document.getElementById('root')

if (container === null) {
  throw new Error('Element #root fehlt in index.html.')
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
