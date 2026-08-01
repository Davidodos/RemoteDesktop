import { CLIENT_PROTOCOL, type AgentInfo } from './types.ts'

/**
 * Sagt, welche Seite zu alt ist — oder nichts, wenn beide zusammenpassen.
 *
 * Seit Agent und App getrennt über GitHub aktualisiert werden, treffen
 * zwangsläufig verschiedene Stände aufeinander. Ohne diese Auskunft merkt das
 * niemand, bis eine Nachricht ankommt, die die Gegenseite nicht kennt — und
 * das sieht dann nach einem kaputten Rechner aus statt nach einem fälligen
 * Update.
 *
 * Ein Agent ohne die Angabe ist älter als Phase 14. Das ist kein Fehler und
 * keine Meldung wert: alles, was es damals gab, funktioniert weiter.
 */
export function protocolMismatch(info: AgentInfo, deviceName: string): string | undefined {
  if (info.protocol === undefined || info.protocol === CLIENT_PROTOCOL) {
    return undefined
  }

  if (info.protocol < CLIENT_PROTOCOL) {
    return (
      `Der Agent auf ${deviceName} ist älter als diese App. ` +
      'Auf der Seite „Ein/Aus" lässt er sich aktualisieren.'
    )
  }

  return (
    `Der Agent auf ${deviceName} ist neuer als diese App. ` +
    'Bitte die App aktualisieren — einzelne Funktionen können bis dahin fehlen.'
  )
}
