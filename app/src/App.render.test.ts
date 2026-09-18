import { act, createElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { App } from './App.tsx'
import { setPlatform } from './platform/index.ts'
import { fallbackPlatform } from './platform/fallback.ts'
import type { IdentityState, Platform } from './platform/index.ts'

/**
 * Die App muss vom Erststart in die Geräteliste kommen, ohne dass React sie
 * fallen lässt.
 *
 * Der Befund dahinter (18.09.2026): ein Hook stand unter den frühen
 * Rückgaben für Erststart und Kopplung. Sobald der Erststart durch war, zählte
 * React einen Hook mehr als im Lauf davor und warf — am Handy blieb der
 * Bildschirm nach der Einrichtung leer, im Fenster der Geräte-Tab.
 */

let container: HTMLDivElement
let root: Root

/** Eine Kennung, deren Erststart im Test abgeschlossen werden kann. */
function identityThatFinishes(): Platform['identity'] & { done: boolean } {
  const identity = {
    done: false,
    read: (): Promise<IdentityState> =>
      Promise.resolve({ name: 'Pixel', chosen: true, firstRunDone: identity.done }),
    rename: (): Promise<void> => Promise.resolve(),
    finishFirstRun: (): Promise<void> => {
      identity.done = true

      return Promise.resolve()
    },
  }

  return identity
}

async function settle(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(() => {
  window.localStorage.clear()
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
})

afterEach(() => {
  act(() => root.unmount())
  container.remove()
  setPlatform(fallbackPlatform)
})

describe('App', () => {
  it('kommt vom Erststart in die Geräteliste, ohne zu stürzen', async () => {
    // Arrange — Erststart offen, ohne Host: dann endet er mit dem Namen.
    const identity = identityThatFinishes()

    setPlatform({ ...fallbackPlatform, identity })

    await act(async () => {
      root.render(createElement(App))
    })
    await settle()

    // Der Erststart ist zu sehen.
    const form = container.querySelector('form')
    expect(form).not.toBeNull()

    // Act — den Namen bestätigen; das beendet den Erststart.
    await act(async () => {
      form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    })
    await settle()
    await settle()

    // Assert — die App steht, mit Kopfzeile und Geräteliste, nicht leer.
    expect(identity.done).toBe(true)
    expect(container.querySelector('.app-header')).not.toBeNull()
    expect(container.textContent).toContain('Gerät')
  })

  it('steht am Rechner sofort mit der Geräteliste da', async () => {
    setPlatform(fallbackPlatform)

    await act(async () => {
      root.render(createElement(App))
    })
    await settle()

    expect(container.querySelector('.app-header')).not.toBeNull()
  })
})
