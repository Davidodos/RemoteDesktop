import { act, createElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PairingView } from './PairingView.tsx'
import { setPlatform } from '../platform/index.ts'
import { fallbackPlatform } from '../platform/fallback.ts'
import { noHost } from '../platform/host.ts'
import type { HostClient, HostService } from '../platform/index.ts'

/**
 * Die Kopplung als Schrittfolge: erst die Frage, wer den Code zeigt, dann der
 * Weg, am Ende dieselbe Meldung auf beiden Seiten.
 */

let container: HTMLDivElement
let root: Root

const CODE = {
  code: '123456',
  expiresInSeconds: 300,
  pairingUri: 'remotedesktop://pair?host=192.168.178.31&port=8443&code=123456',
  check: 'abcd1234',
}

/** Ein Host, dessen Clientliste der Test fortschreibt. */
function fakeHost(): HostService & { clientList: HostClient[] } {
  const host = {
    ...noHost,
    available: true,
    clientList: [] as HostClient[],
    pairingCode: vi.fn(() => Promise.resolve(CODE)),
    cancelPairing: vi.fn(() => Promise.resolve()),
    clients: (): Promise<HostClient[]> => Promise.resolve(host.clientList),
  }

  return host
}

function button(text: string): HTMLButtonElement {
  const found = [...container.querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === text,
  )

  if (found === undefined) {
    throw new Error(`Kein Knopf „${text}" — da sind: ${container.textContent ?? ''}`)
  }

  return found
}

async function click(text: string): Promise<void> {
  await act(async () => {
    button(text).click()
  })
  await settle()
}

async function settle(): Promise<void> {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0)
  })
}

function render(overrides: Partial<Parameters<typeof PairingView>[0]> = {}): {
  onClose: ReturnType<typeof vi.fn>
} {
  const onClose = vi.fn()

  act(() => {
    root.render(
      createElement(PairingView, {
        onPaired: () => undefined,
        onClose,
        backRef: { current: undefined },
        ...overrides,
      }),
    )
  })

  return { onClose }
}

beforeEach(() => {
  vi.useFakeTimers()
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
})

afterEach(() => {
  act(() => root.unmount())
  container.remove()
  setPlatform(fallbackPlatform)
  vi.useRealTimers()
})

describe('PairingView', () => {
  it('fragt zuerst, wer den Code zeigt', () => {
    setPlatform({ ...fallbackPlatform, host: fakeHost() })

    render()

    expect(container.textContent).toContain(
      'Ein Gerät zeigt den Code, das andere trägt ihn ein. Die Kopplung verbindet die Geräte in beide Richtungen.',
    )
    expect(button('Dieses Gerät koppeln')).toBeDefined()
    expect(button('Anderes Gerät eintragen')).toBeDefined()
  })

  it('führt ohne Kamera direkt zur Handeingabe', async () => {
    setPlatform({
      ...fallbackPlatform,
      host: fakeHost(),
      capabilities: { ...fallbackPlatform.capabilities, camera: false },
    })

    render()
    await click('Anderes Gerät eintragen')

    expect(container.querySelector('h1')?.textContent).toBe('Manuell eintragen')
    expect(container.querySelector('#pair-host')).not.toBeNull()
  })

  it('behält den Code beim Wechsel zwischen QR-Code und Handeingabe', async () => {
    const host = fakeHost()

    setPlatform({ ...fallbackPlatform, host })

    render()
    await click('Dieses Gerät koppeln')
    await click('QR-Code erzeugen')
    await click('←')
    await click('Manuell koppeln')

    expect(host.pairingCode).toHaveBeenCalledTimes(1)
    expect(container.textContent).toContain('123456')
    expect(container.textContent).toContain('192.168.178.31:8443')
    expect(container.textContent).toContain('abcd1234')
  })

  it('verwirft den Code beim Schließen und geht in die Geräteliste', async () => {
    const host = fakeHost()

    setPlatform({ ...fallbackPlatform, host })

    const { onClose } = render()
    await click('Dieses Gerät koppeln')
    await click('QR-Code erzeugen')
    await click('Schließen')

    expect(host.cancelPairing).toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('meldet die Kopplung auf der Seite, die den Code zeigt', async () => {
    const host = fakeHost()

    setPlatform({ ...fallbackPlatform, host })

    render()
    await click('Dieses Gerät koppeln')
    await click('Manuell koppeln')

    // Act — drüben wird der Code eingelöst.
    host.clientList = [{ id: 'handy', label: 'Handy', scopes: [], lastSeenAt: 0, createdAt: 5 }]

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2100)
    })
    await settle()

    expect(container.textContent).toContain('Handy erfolgreich gekoppelt')

    await click('Weiteres Gerät koppeln')

    expect(container.querySelector('h1')?.textContent).toBe('Gerät koppeln')
  })

  it('erkennt auch ein Gerät, das schon einmal gekoppelt war', async () => {
    const host = fakeHost()

    host.clientList = [{ id: 'handy', label: 'Handy', scopes: [], lastSeenAt: 0, createdAt: 1 }]
    setPlatform({ ...fallbackPlatform, host })

    render()
    await click('Dieses Gerät koppeln')
    await click('QR-Code erzeugen')

    host.clientList = [{ id: 'handy', label: 'Handy', scopes: [], lastSeenAt: 0, createdAt: 9 }]

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2100)
    })
    await settle()

    expect(container.textContent).toContain('Handy erfolgreich gekoppelt')
  })

  it('geht mit der Zurück-Taste einen Schritt zurück', async () => {
    setPlatform({ ...fallbackPlatform, host: fakeHost() })

    const backRef: { current: (() => void) | undefined } = { current: undefined }
    const { onClose } = render({ backRef })

    await click('Dieses Gerät koppeln')

    await act(async () => {
      backRef.current?.()
    })

    expect(container.querySelector('h1')?.textContent).toBe('Gerät koppeln')
    expect(onClose).not.toHaveBeenCalled()

    await act(async () => {
      backRef.current?.()
    })

    expect(onClose).toHaveBeenCalled()
  })
})
