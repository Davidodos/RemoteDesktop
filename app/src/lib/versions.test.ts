import { describe, expect, it } from 'vitest'
import {
  canUpdateRemotely,
  compareVersions,
  describeMatch,
  normalizeVersion,
} from './versions.ts'
import type { Device } from './types.ts'

const rechner: Device = {
  id: 'r1',
  name: 'PC',
  host: '192.168.178.31',
  port: 8443,
  platform: 'windows',
  canWake: true,
}

const handy: Device = { ...rechner, id: 'h1', name: 'Handy', platform: 'android' }

describe('normalizeVersion', () => {
  it('nimmt das v vom Git-Tag weg', () => {
    expect(normalizeVersion('v1.3.4')).toBe('1.3.4')
  })

  /**
   * Seit .NET 8 hängt der Build die Commit-Kennung an die Fassung im Dateikopf.
   * Nach SemVer ist alles hinter dem Pluszeichen ausdrücklich *kein*
   * Unterschied — ohne das meldete jeder Vergleich ein Update auf die Fassung,
   * die bereits lief.
   */
  it('schneidet die Commit-Kennung ab', () => {
    expect(normalizeVersion('1.3.4+435992d47c60')).toBe('1.3.4')
  })

  it('leer bleibt leer', () => {
    expect(normalizeVersion('  ')).toBeUndefined()
    expect(normalizeVersion(undefined)).toBeUndefined()
  })
})

describe('compareVersions', () => {
  it('gleich ist gleich, auch über Schreibweisen hinweg', () => {
    expect(compareVersions('1.3.4', 'v1.3.4+abc')).toBe('same')
  })

  /** Als Text sortiert sich `1.10.0` vor `1.9.0`. Als Fassung ist es neuer. */
  it('vergleicht Zahl für Zahl und nicht als Text', () => {
    expect(compareVersions('1.10.0', '1.9.0')).toBe('older')
    expect(compareVersions('1.9.0', '1.10.0')).toBe('newer')
  })

  it('fehlende Stellen zählen als null', () => {
    expect(compareVersions('1.3', '1.3.0')).toBe('same')
    expect(compareVersions('1.3.1', '1.3')).toBe('older')
  })

  it('ohne beide Fassungen wird nichts behauptet', () => {
    expect(compareVersions(undefined, '1.3.4')).toBe('unknown')
    expect(compareVersions('1.3.4', undefined)).toBe('unknown')
  })
})

describe('canUpdateRemotely', () => {
  it('ein Rechner mit älterer Fassung', () => {
    expect(canUpdateRemotely(rechner, 'older')).toBe(true)
  })

  /**
   * Kein Knopf ohne Anlass: der Rechner wäre danach eine Minute lang weg, ohne
   * dass sich etwas geändert hätte.
   */
  it('nicht bei gleichem oder neuerem Stand', () => {
    expect(canUpdateRemotely(rechner, 'same')).toBe(false)
    expect(canUpdateRemotely(rechner, 'newer')).toBe(false)
    expect(canUpdateRemotely(rechner, 'unknown')).toBe(false)
  })

  /**
   * Ein Handy geht nicht, und das ist keine Lücke: Android verlangt für jede
   * Installation einen Systemdialog, und den beantwortet nur, wer das Gerät in
   * der Hand hält.
   */
  it('nie ein Handy', () => {
    expect(canUpdateRemotely(handy, 'older')).toBe(false)
  })

  it('nie ein Waker — er hat nichts zu aktualisieren', () => {
    expect(canUpdateRemotely({ ...rechner, waker: true }, 'older')).toBe(false)
  })
})

describe('describeMatch', () => {
  /**
   * Der Zusatz steht nur da, wenn es etwas zu tun gibt. Ein Vergleich hinter
   * jeder Fassung verlangt, eine Zeile zu lesen, die meistens nichts meldet.
   */
  it('nennt bei gleichem Stand nur die Fassung', () => {
    expect(describeMatch('same', '1.3.4')).toBe('Version 1.3.4')
  })

  it('nur ein veraltetes Gerät bekommt den Zusatz', () => {
    expect(describeMatch('older', '1.3.0')).toBe('Version 1.3.0 - Update verfügbar')
  })

  /** Dort ist nichts zu tun — zu aktualisieren wäre dieses Gerät hier. */
  it('ein neueres Gerät nicht', () => {
    expect(describeMatch('newer', '1.4.0')).toBe('Version 1.4.0')
  })

  it('ohne Fassung steht dort nichts', () => {
    expect(describeMatch('unknown', undefined)).toBe('')
  })
})
