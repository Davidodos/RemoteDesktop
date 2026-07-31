import { describe, expect, it } from 'vitest'
import { AgentClient } from './agentClient.ts'
import type { Channel, ChannelHandlers, ControlRequest, Transport } from '../transport/index.ts'
import type { AgentActionSummary, Device } from './types.ts'

/**
 * Die App-Seite der Aktionen aus Phase 13.
 *
 * Geprüft wird genau eine Zusage, und die ist die wichtigste des ganzen
 * Entwurfs: der Client schickt eine **Kennung** und niemals eine Kommandozeile.
 * Was eine Aktion bedeutet, steht ausschließlich in der `actions.json` auf dem
 * Zielrechner.
 */

const DEVICE: Device = {
  id: 'pc',
  name: 'Arbeitsrechner',
  host: 'arbeitsrechner',
  port: 8443,
} as Device

/** Zeichnet auf, was hinausginge, statt es zu senden. */
function aufzeichnendenTransport(antwort: unknown): {
  transport: Transport
  anfragen: ControlRequest[]
} {
  const anfragen: ControlRequest[] = []

  const transport: Transport = {
    control: <T,>(request: ControlRequest): Promise<T> => {
      anfragen.push(request)
      return Promise.resolve(antwort as T)
    },
    resourceUrl: (path) => path,
    inputChannel: (_: ChannelHandlers): Channel => {
      throw new Error('wird hier nicht gebraucht')
    },
    screenStream: (_monitor: number, _handlers: ChannelHandlers): Channel => {
      throw new Error('wird hier nicht gebraucht')
    },
  } as Transport

  return { transport, anfragen }
}

const AKTIONEN: AgentActionSummary[] = [
  { id: 'obs-aufnahme', label: 'OBS aufnehmen', icon: 'record', type: 'process', confirm: false },
  { id: 'backup', label: 'Backup', type: 'script', confirm: true },
]

describe('Aktionen abrufen', () => {
  it('liest die Liste des Zielrechners', async () => {
    // Arrange
    const { transport } = aufzeichnendenTransport({ actions: AKTIONEN })

    // Act
    const gefunden = await new AgentClient(DEVICE, transport).getActions()

    // Assert
    expect(gefunden).toEqual(AKTIONEN)
  })

  it('fragt lesend am richtigen Pfad', async () => {
    // Arrange
    const { transport, anfragen } = aufzeichnendenTransport({ actions: [] })

    // Act
    await new AgentClient(DEVICE, transport).getActions()

    // Assert — GET, kein Rumpf: die Liste zu holen verändert nichts.
    expect(anfragen).toEqual([{ path: '/api/actions', method: 'GET', body: undefined }])
  })

  it('reicht den Merker für die Rückfrage durch', async () => {
    // Arrange — der Merker kommt vom Rechner und nicht aus dem Handy; nur
    // deshalb gilt er für jeden Client gleich.
    const { transport } = aufzeichnendenTransport({ actions: AKTIONEN })

    // Act
    const gefunden = await new AgentClient(DEVICE, transport).getActions()

    // Assert
    expect(gefunden[1]?.confirm).toBe(true)
    expect(gefunden[0]?.confirm).toBe(false)
  })
})

describe('Aktionen auslösen', () => {
  it('schickt nur die Kennung, nie eine Kommandozeile', async () => {
    // Arrange
    const { transport, anfragen } = aufzeichnendenTransport({ status: 'ok' })

    // Act
    await new AgentClient(DEVICE, transport).invokeAction('obs-aufnahme')

    // Assert — kein Rumpf, keine Argumente, kein Pfad zu einem Programm. Das
    // ist der ganze Entwurf in einer Zeile.
    expect(anfragen).toEqual([
      { path: '/api/actions/obs-aufnahme/invoke', method: 'POST', body: undefined },
    ])
  })

  it('kodiert die Kennung für die Adresse', async () => {
    // Der Agent lässt nur Kleinbuchstaben, Ziffern und Bindestriche zu. Sollte
    // je etwas anderes durchkommen, darf es die Adresse nicht verbiegen.
    const { transport, anfragen } = aufzeichnendenTransport({ status: 'ok' })

    await new AgentClient(DEVICE, transport).invokeAction('a/b')

    expect(anfragen[0]?.path).toBe('/api/actions/a%2Fb/invoke')
  })
})
