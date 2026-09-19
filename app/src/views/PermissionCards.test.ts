import { act, createElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { PermissionCards } from './PermissionCards.tsx'
import { setPlatform } from '../platform/index.ts'
import { fallbackPlatform } from '../platform/fallback.ts'
import { noHost } from '../platform/host.ts'
import type { HostStatus } from '../platform/index.ts'

let container: HTMLDivElement
let root: Root

const BASE: HostStatus = { running: true, deviceName: 'Handy', port: 8443, addresses: [] }

function host(): typeof noHost & {
  openInputSettings: ReturnType<typeof vi.fn>
  disableInput: ReturnType<typeof vi.fn>
} {
  return {
    ...noHost,
    available: true,
    openInputSettings: vi.fn(() => Promise.resolve()),
    disableInput: vi.fn(() => Promise.resolve(BASE)),
  }
}

function render(status: HostStatus): void {
  act(() => {
    root.render(
      createElement(PermissionCards, {
        status,
        onStatus: () => undefined,
        onRefresh: () => undefined,
        onError: () => undefined,
        onGuide: () => undefined,
      }),
    )
  })
}

function buttons(): string[] {
  return [...container.querySelectorAll('button')].map((b) => b.textContent?.trim() ?? '')
}

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
})

afterEach(() => {
  act(() => root.unmount())
  container.remove()
  setPlatform(fallbackPlatform)
})

describe('PermissionCards', () => {
  it('sagt bei ausgeschalteten Rechten nur, was fehlt', () => {
    setPlatform({ ...fallbackPlatform, host: host() })

    render({ ...BASE, screenAllowed: false, acceptingInput: false })

    expect(container.textContent).toContain('Nicht freigegeben')
    expect(container.textContent).toContain('Nicht freigegeben. Benötigt Bedienungshilfen')
    expect(buttons()).toEqual([
      'Aktivieren',
      'Aktivieren',
      'Anleitung: Bedienungshilfen einschalten',
    ])
  })

  it('zeigt bei eingeschalteten Rechten „Freigegeben" und „Deaktivieren"', () => {
    setPlatform({ ...fallbackPlatform, host: host() })

    render({ ...BASE, screenAllowed: true, acceptingInput: true })

    expect(container.querySelectorAll('.settings-hint')[1]?.textContent).toBe('Freigegeben')
    expect(buttons().slice(0, 2)).toEqual(['Deaktivieren', 'Deaktivieren'])
  })

  it('schaltet Eingaben über die Bedienungshilfe ein und hier wieder aus', async () => {
    const fake = host()

    setPlatform({ ...fallbackPlatform, host: fake })

    render({ ...BASE, acceptingInput: false })
    await act(async () => {
      container.querySelectorAll('button')[1]?.click()
    })
    expect(fake.openInputSettings).toHaveBeenCalled()

    render({ ...BASE, acceptingInput: true })
    await act(async () => {
      container.querySelectorAll('button')[1]?.click()
    })
    expect(fake.disableInput).toHaveBeenCalled()
  })
})
