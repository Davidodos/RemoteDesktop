import { useEffect, useRef, type Dispatch, type SetStateAction } from 'react'
import { getPlatform } from '../platform/index.ts'
import type { Device } from './types.ts'
import type { Page } from '../views/Sidebar.tsx'

/** So lange gilt der erste Druck auf der obersten Ebene als Vorwarnung. */
export const EXIT_WINDOW_MS = 2000

interface Options {
  menuOpen: boolean
  setMenuOpen: Dispatch<SetStateAction<boolean>>
  pairing: boolean
  setPairing: Dispatch<SetStateAction<boolean>>
  selected: Device | undefined
  disconnect: () => void
  page: Page
  setPage: Dispatch<SetStateAction<Page>>
  onError: (message: string) => void
}

/**
 * Was ein Druck auf die Zurück-Taste tut — die nächsthöhere Ebene, in dieser
 * Reihenfolge: Menü zu, Kopplung abbrechen, Sitzung trennen, zur Geräteliste.
 * Auf der Geräteliste selbst beendet erst ein zweiter Druck binnen zwei
 * Sekunden die App; der erste sagt das.
 *
 * Die Entscheidung liegt in einem Ref, damit der Zuhörer einmal angemeldet
 * bleibt und trotzdem den jeweils aktuellen Zustand sieht.
 */
export function useBackButton(options: Options): void {
  const latest = useRef(options)
  const lastPress = useRef(0)

  useEffect(() => {
    latest.current = options
  })

  useEffect(() => {
    const navigation = getPlatform().navigation

    if (!navigation.available) {
      return
    }

    return navigation.onBack(() => {
      const now = Date.now()
      const step = decide(latest.current, now - lastPress.current <= EXIT_WINDOW_MS)

      lastPress.current = step === 'warn' ? now : 0
      apply(step, latest.current, navigation.exit)
    })
  }, [])
}

export type BackStep = 'closeMenu' | 'cancelPairing' | 'disconnect' | 'devices' | 'warn' | 'exit'

/** Reine Entscheidung, damit sie sich ohne Android prüfen lässt. */
export function decide(
  state: Pick<Options, 'menuOpen' | 'pairing' | 'selected' | 'page'>,
  secondPress: boolean,
): BackStep {
  if (state.menuOpen) {
    return 'closeMenu'
  }

  if (state.pairing) {
    return 'cancelPairing'
  }

  if (state.selected !== undefined) {
    return 'disconnect'
  }

  if (state.page !== 'devices') {
    return 'devices'
  }

  return secondPress ? 'exit' : 'warn'
}

function apply(step: BackStep, state: Options, exit: () => Promise<void>): void {
  switch (step) {
    case 'closeMenu':
      state.setMenuOpen(false)
      return
    case 'cancelPairing':
      state.setPairing(false)
      return
    case 'disconnect':
      state.disconnect()
      return
    case 'devices':
      state.setPage('devices')
      return
    case 'warn':
      state.onError('Noch einmal Zurück beendet die App.')
      return
    case 'exit':
      void exit().catch(() => undefined)
  }
}
