import { describe, expect, test } from 'vitest'
import { buildSurfaceBoard } from './surfaceBoard.ts'
import type { AgentActionSummary, Device } from './types.ts'

const ZUHAUSE = 'kennung-zuhause'
const ELTERN = 'kennung-eltern'

function geraet(id: string, extra: Partial<Device> = {}): Device {
  return {
    id,
    name: id.toUpperCase(),
    host: `${id}.example.ts.net`,
    port: 8443,
    clientId: 'handy-1',
    canWake: false,
    ...extra,
  }
}

function aktion(id: string, extra: Partial<AgentActionSummary> = {}): AgentActionSummary {
  return { id, label: id.toUpperCase(), type: 'process', confirm: false, ...extra }
}

const PC = geraet('pc', { mac: 'aa:bb:cc:dd:ee:ff', siteId: ZUHAUSE })

describe('buildSurfaceBoard', () => {
  test('nimmt Kennung und Aufschrift der Aktionen mit', () => {
    const board = buildSurfaceBoard(PC, [aktion('spotify'), aktion('vscode')], [PC])

    expect(board?.actions).toEqual([
      { id: 'spotify', label: 'SPOTIFY' },
      { id: 'vscode', label: 'VSCODE' },
    ])
  })

  test('Aktionen mit Rückfrage kommen nicht auf das Widget', () => {
    // Ein Widget kann nicht nachfragen — es hat keine Oberfläche, in der eine
    // Rückfrage stünde. Der `confirm`-Merker aus Phase 13 wäre damit still
    // ausgehebelt, und zwar bei genau den Aktionen, die ihn tragen.
    const board = buildSurfaceBoard(
      PC,
      [aktion('neustart', { confirm: true }), aktion('spotify')],
      [PC],
    )

    expect(board?.actions.map((action) => action.id)).toEqual(['spotify'])
  })

  test('Pfade und Argumente stehen nirgends drin', () => {
    // Dieselbe Zusage wie bei `GET /api/actions`: wer einen Knopf baut, braucht
    // nicht zu wissen, welche Software wo auf dem Rechner liegt.
    const board = buildSurfaceBoard(PC, [aktion('spotify')], [PC])

    expect(Object.keys(board!.actions[0]!)).toEqual(['id', 'label'])
  })

  test('ohne Kopplung gibt es keine Flächen', () => {
    // Der native Teil weist sich ausschließlich mit dem Geräteschlüssel aus.
    // Ein geteiltes Token gehört nicht in ein Widget — es gilt für alles.
    const alt = geraet('alt', { clientId: undefined, token: 'geheim' })

    expect(buildSurfaceBoard(alt, [aktion('spotify')], [alt])).toBeUndefined()
  })

  test('das Token wandert auch dann nicht mit, wenn beides dasteht', () => {
    const board = buildSurfaceBoard(geraet('pc', { token: 'geheim' }), [], [])

    expect(JSON.stringify(board)).not.toContain('geheim')
  })

  test('der Bote zum Wecken kommt mit, samt MAC des Ziels', () => {
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(buildSurfaceBoard(PC, [], [PC, nas])?.wake).toEqual({
      mac: 'aa:bb:cc:dd:ee:ff',
      via: { host: 'nas.example.ts.net', port: 8443, clientId: 'handy-1' },
    })
  })

  test('ein Bote aus einem fremden Netz kommt nicht mit', () => {
    const pi = geraet('pi', { canWake: true, waker: true, siteId: ELTERN })

    expect(buildSurfaceBoard(PC, [], [PC, pi])?.wake).toBeUndefined()
  })

  test('ohne Boten bleibt der Weckteil leer statt halb gefüllt', () => {
    expect(buildSurfaceBoard(PC, [], [PC])?.wake).toBeUndefined()
  })

  test('der Bote wird gewählt, ohne zu fragen, wer gerade antwortet', () => {
    // Der Steckbrief entsteht, solange der Rechner läuft, und wird womöglich
    // Tage später benutzt. Wer dann erreichbar ist, prüft die Fläche selbst —
    // hier wäre die Frage schlicht zu früh gestellt.
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(buildSurfaceBoard(PC, [], [PC, nas])?.wake?.via.host).toBe('nas.example.ts.net')
  })

  test('Rechner ohne bekannte MAC bekommen keinen Weckknopf', () => {
    const laptop = geraet('laptop', { siteId: ZUHAUSE })
    const nas = geraet('nas', { canWake: true, waker: true, siteId: ZUHAUSE })

    expect(buildSurfaceBoard(laptop, [], [laptop, nas])?.wake).toBeUndefined()
  })

  test('Name und Adresse des Rechners stehen drin', () => {
    const board = buildSurfaceBoard(PC, [], [PC])

    expect(board).toMatchObject({
      deviceId: 'pc',
      deviceName: 'PC',
      node: { host: 'pc.example.ts.net', port: 8443, clientId: 'handy-1' },
    })
  })
})
