import { act, createElement, useEffect, useState } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useNotice } from './notice.ts'

/**
 * `useNotice` im Zusammenspiel mit Effekten — so, wie `App.tsx` es benutzt.
 *
 * Der Befund dahinter: `report` und `clear` waren bei jedem Rendern neue
 * Funktionen. Ein Effekt mit `clear` als Abhängigkeit lief deshalb nach jedem
 * Rendern und löschte jede Meldung einen Durchlauf nach ihrem Erscheinen; ein
 * Effekt mit `report` als Abhängigkeit (die Bildschirmansicht) baute bei jedem
 * Rendern seinen Socket neu auf.
 */

let container: HTMLDivElement
let root: Root
let reportEffectRuns = 0

function Probe({ selected }: { selected: string }): React.ReactElement {
  const { message, report, clear } = useNotice()
  const [, tick] = useState(0)

  useEffect(() => {
    clear()
  }, [selected, clear])

  useEffect(() => {
    reportEffectRuns += 1
  }, [report])

  return createElement(
    'div',
    null,
    createElement('span', { id: 'msg' }, message ?? '(leer)'),
    createElement('button', { id: 'report', onClick: () => report('Fehler X') }),
    createElement('button', { id: 'rerender', onClick: () => tick((n) => n + 1) }),
  )
}

describe('useNotice', () => {
  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.useFakeTimers()
    reportEffectRuns = 0
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
    vi.useRealTimers()
  })

  it('lässt eine gemeldete Meldung stehen, auch wenn ein Effekt an clear hängt', () => {
    act(() => root.render(createElement(Probe, { selected: 'a' })))

    act(() => {
      container.querySelector<HTMLButtonElement>('#report')!.click()
    })

    expect(container.querySelector('#msg')!.textContent).toBe('Fehler X')

    act(() => {
      vi.advanceTimersByTime(12_000)
    })

    expect(container.querySelector('#msg')!.textContent).toBe('(leer)')
  })

  it('behält report über Renderdurchläufe hinweg', () => {
    act(() => root.render(createElement(Probe, { selected: 'a' })))

    const before = reportEffectRuns

    act(() => {
      container.querySelector<HTMLButtonElement>('#rerender')!.click()
    })

    expect(reportEffectRuns).toBe(before)
  })

  it('löscht beim Wechsel des Geräts, aber nicht beim bloßen Rendern', () => {
    act(() => root.render(createElement(Probe, { selected: 'a' })))

    act(() => {
      container.querySelector<HTMLButtonElement>('#report')!.click()
    })

    act(() => {
      container.querySelector<HTMLButtonElement>('#rerender')!.click()
    })

    expect(container.querySelector('#msg')!.textContent).toBe('Fehler X')

    act(() => root.render(createElement(Probe, { selected: 'b' })))

    expect(container.querySelector('#msg')!.textContent).toBe('(leer)')
  })
})
