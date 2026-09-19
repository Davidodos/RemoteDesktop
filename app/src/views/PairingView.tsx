import { useEffect, useState, type MutableRefObject } from 'react'
import { CertificateTrustStep } from './CertificateTrustStep.tsx'
import { PairingOffer, usePairingCode } from './PairingOffer.tsx'
import {
  certificateFingerprint,
  downloadAuthority,
  fetchAgentCertificate,
  matchesCheck,
  normalizeCheck,
  TRUST_PORT,
} from '../lib/certificateTrust.ts'
import { saveLocalDevice } from '../lib/deviceSources.ts'
import { collectPeers, grantPeer } from '../lib/bothWays.ts'
import { ownName } from '../lib/ownName.ts'
import { pairBothWays } from '../lib/pairing.ts'
import { DEFAULT_AGENT_PORT, parsePairingUri, type PairingTarget } from '../lib/pairingUri.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from '../lib/types.ts'

interface Props {
  /**
   * Eine Kopplung ist durch — am Handy wie am Rechner, gleich von welcher
   * Seite aus. Die Liste ist dann schon neu; die Seite zeigt ihre Meldung
   * selbst.
   */
  onPaired: (devices: Device[], paired?: Device) => void
  /** Zurück in die Geräteliste. */
  onClose: () => void
  /**
   * Wohin die Zurück-Taste von Android geht: ein Schritt zurück, nicht raus.
   * Die Seite trägt hier ihren eigenen Rückweg ein.
   */
  backRef: MutableRefObject<(() => void) | undefined>
}

/** Die Schritte der Kopplung, in der Reihenfolge, in der sie kommen. */
type Step =
  | 'choose'
  | 'offer'
  | 'offer-qr'
  | 'offer-manual'
  | 'enter'
  | 'enter-manual'

/** Die Meldung am Ende. */
interface Done {
  name: string
  /** Welche Seite dieses Gerät war — danach richtet sich „Weiteres Gerät …". */
  side: 'offer' | 'enter'
  warnung?: string
}

/**
 * Koppeln — Schritt für Schritt.
 *
 * <p>
 * **Zuerst die eine Frage:** zeigt dieses Gerät den Code, oder trägt es den
 * eines anderen ein? Beide Wege enden gleich — die Kopplung verbindet die
 * Geräte in beide Richtungen —, verlangen aber an diesem Gerät etwas anderes.
 * Danach je zwei Wege: QR-Code oder von Hand. Am Rechner gibt es keine Kamera;
 * „Anderes Gerät eintragen" führt dort direkt zur Handeingabe.
 * </p>
 *
 * <p>
 * **Am Ende steht auf beiden Geräten dieselbe Meldung.** Das Gerät, das den
 * Code zeigte, erfährt die Kopplung an seiner Clientliste (siehe
 * `usePairingCode`).
 * </p>
 */
export function PairingView({ onPaired, onClose, backRef }: Props): React.JSX.Element {
  const platform = getPlatform()
  const camera = platform.capabilities.camera

  const [step, setStep] = useState<Step>('choose')
  const [error, setError] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<Done | undefined>(undefined)

  /**
   * Gekoppelt, aber noch nicht fertig: ein Gerät ohne Tailscale weist sich mit
   * einem selbst ausgestellten Zertifikat aus, und dem muss dieses hier erst
   * vertrauen. Ohne den Zwischenschritt stünde es in der Liste und ließe sich
   * nicht verbinden.
   */
  const [awaitingTrust, setAwaitingTrust] = useState<
    { device: Device; devices: Device[]; warnung?: string } | undefined
  >(undefined)

  const offer = usePairingCode((name) => {
    // Der Steckbrief der Gegenseite liegt jetzt hier. Erst abholen, dann den
    // Code verwerfen — eine Gegenstelle, die nur für ihn lief, geht damit aus,
    // und danach wäre er nicht mehr zu haben.
    void collectPeers()
      .then((devices) => {
        if (devices !== undefined) {
          onPaired(devices)
        }
      }, () => undefined)
      .finally(offer.cancel)

    setDone({ name, side: 'offer' })
  })

  const finish = (devices: Device[], device: Device, warnung?: string): void => {
    onPaired(devices, device)
    setDone({ name: device.name, side: 'enter', ...(warnung === undefined ? {} : { warnung }) })
  }

  const pair = async (target: PairingTarget): Promise<void> => {
    setBusy(true)
    setError(undefined)

    try {
      const { device, devices, trusted, warnung } = await pairWith(target)

      // Nichts zu bestätigen — oder schon bestätigt, bevor gekoppelt wurde.
      //
      // Geprüft wird der Fingerabdruck und nicht bloß, ob das Feld gesetzt ist:
      // ein `null` vom Agent bedeutet „nichts zu bestätigen", sah aber aus wie
      // ein Wert. Siehe `certificateFingerprint`.
      if (certificateFingerprint(device.caFingerprint) === undefined || trusted) {
        finish(devices, device, warnung)

        return
      }

      setAwaitingTrust({ device, devices, ...(warnung === undefined ? {} : { warnung }) })
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  const scan = async (): Promise<void> => {
    setError(undefined)

    let target: PairingTarget

    try {
      target = parsePairingUri(await platform.qr.scan())
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))

      return
    }

    await pair(target)
  }

  const go = (next: Step): void => {
    setError(undefined)
    setStep(next)
  }

  /** Den Code anzeigen — derselbe, falls noch einer gilt. */
  const show = (next: 'offer-qr' | 'offer-manual'): void => {
    go(next)

    if (offer.code === undefined && !offer.busy) {
      offer.renew()
    }
  }

  /** Zurück an den Anfang — ein offener Code gilt dann nicht mehr. */
  const restart = (): void => {
    offer.cancel()
    setDone(undefined)
    go('choose')
  }

  /** Raus aus der Kopplung, zurück in die Geräteliste. */
  const close = (): void => {
    offer.cancel()
    onClose()
  }

  const back = (): void => {
    if (done !== undefined) {
      close()
      return
    }

    // Die Kopplung steht schon; zurück gibt es hier nicht mehr, nur weiter.
    if (awaitingTrust !== undefined) {
      return
    }

    switch (step) {
      case 'choose':
        close()
        return
      case 'offer':
      case 'enter':
        restart()
        return
      case 'offer-qr':
      case 'offer-manual':
        go('offer')
        return
      case 'enter-manual':
        if (camera) {
          go('enter')
        } else {
          restart()
        }
    }
  }

  useEffect(() => {
    backRef.current = back
  })

  useEffect(
    () => () => {
      backRef.current = undefined
    },
    [backRef],
  )

  if (awaitingTrust !== undefined) {
    return (
      <CertificateTrustStep
        device={awaitingTrust.device}
        onDone={() => {
          const { devices, device, warnung } = awaitingTrust

          setAwaitingTrust(undefined)
          finish(devices, device, warnung)
        }}
      />
    )
  }

  return (
    <div className="token-prompt pairing-page">
      <button type="button" className="link-button back-arrow" onClick={back} aria-label="Zurück">
        ←
      </button>

      <h1>{TITLES[step]}</h1>

      {error !== undefined && <p className="error-text">{error}</p>}

      {step === 'choose' && (
        <>
          <p>
            Ein Gerät zeigt den Code, das andere trägt ihn ein. Die Kopplung verbindet die Geräte in
            beide Richtungen.
          </p>
          <div className="choice-buttons">
            <button type="button" onClick={() => go('offer')}>
              Dieses Gerät koppeln
            </button>
            <button type="button" onClick={() => go(camera ? 'enter' : 'enter-manual')}>
              Anderes Gerät eintragen
            </button>
          </div>
        </>
      )}

      {step === 'offer' && (
        <div className="choice-buttons">
          <button type="button" onClick={() => show('offer-qr')}>
            QR-Code erzeugen
          </button>
          <button type="button" onClick={() => show('offer-manual')}>
            Manuell koppeln
          </button>
        </div>
      )}

      {(step === 'offer-qr' || step === 'offer-manual') && (
        <PairingOffer
          pairing={offer}
          mode={step === 'offer-qr' ? 'qr' : 'manual'}
          onClose={close}
        />
      )}

      {step === 'enter' && (
        <div className="choice-buttons">
          <button type="button" disabled={busy} onClick={() => void scan()}>
            {busy ? 'Koppeln…' : 'QR-Code scannen'}
          </button>
          <button type="button" disabled={busy} onClick={() => go('enter-manual')}>
            Manuell eintragen
          </button>
        </div>
      )}

      {step === 'enter-manual' && (
        <ManualForm busy={busy} onTarget={(target) => void pair(target)} />
      )}

      {done !== undefined && (
        <div className="request-overlay">
          <div className="overlay-card">
            <h2>{done.name} erfolgreich gekoppelt</h2>
            {done.warnung !== undefined && <p>{done.warnung}</p>}
            <button type="button" onClick={close}>
              Fertig
            </button>
            <button type="button" className="secondary" onClick={restart}>
              {done.side === 'offer' ? 'Weiteres Gerät koppeln' : 'Weiteres Gerät eintragen'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

const TITLES: Record<Step, string> = {
  choose: 'Gerät koppeln',
  offer: 'Dieses Gerät koppeln',
  'offer-qr': 'QR-Code',
  'offer-manual': 'Manuell koppeln',
  enter: 'Anderes Gerät eintragen',
  'enter-manual': 'Manuell eintragen',
}

/**
 * Adresse und Code der Gegenseite — von Hand.
 *
 * Der einzige Weg ohne Kamera und damit der Normalfall am Rechner — dort
 * führt „Anderes Gerät eintragen" deshalb direkt hierher.
 */
function ManualForm({
  busy,
  onTarget,
}: {
  busy: boolean
  onTarget: (target: PairingTarget) => void
}): React.JSX.Element {
  const [host, setHost] = useState('')
  const [code, setCode] = useState('')
  const [check, setCheck] = useState('')

  const ready =
    host.trim().length > 0 &&
    code.trim().length === 6 &&
    (check.trim().length === 0 || normalizeCheck(check) !== undefined)

  return (
    <form
      className="pairing-form"
      onSubmit={(event) => {
        event.preventDefault()

        if (ready && !busy) {
          const normalized = normalizeCheck(check)

          onTarget({
            host: host.trim(),
            port: DEFAULT_AGENT_PORT,
            code: code.trim(),
            ...(normalized === undefined ? {} : { check: normalized }),
          })
        }
      }}
    >
      <label className="field-label" htmlFor="pair-host">
        Adresse
      </label>
      <input
        id="pair-host"
        value={host}
        onChange={(event) => setHost(event.target.value)}
        placeholder="z. B. 192.168.178.31"
        autoCapitalize="off"
        autoCorrect="off"
        spellCheck={false}
      />

      <label className="field-label" htmlFor="pair-code">
        Code
      </label>
      <input
        id="pair-code"
        value={code}
        onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
        placeholder="6 Ziffern"
        inputMode="numeric"
        autoComplete="off"
      />

      <label className="field-label" htmlFor="pair-check">
        Prüfzeichen
      </label>
      <input
        id="pair-check"
        value={check}
        onChange={(event) => setCheck(event.target.value.replace(/[^0-9a-fA-F]/g, '').slice(0, 8))}
        placeholder="steht dort neben dem Code — leer, wenn keins da ist"
        autoCapitalize="off"
        autoCorrect="off"
        spellCheck={false}
        autoComplete="off"
      />

      <button type="submit" disabled={!ready || busy}>
        {busy ? 'Koppeln…' : 'Koppeln'}
      </button>
    </form>
  )
}

/** Was nach einer Kopplung feststeht. */
interface Paired {
  device: Device
  devices: Device[]
  /** Ob der ausstellenden Stelle schon vertraut wurde. */
  trusted: boolean
  /** Warum die Gegenrichtung nicht zustande kam — meist gibt es keinen. */
  warnung?: string
}

/**
 * Die Kopplung selbst — erst vertrauen, dann koppeln.
 *
 * <p>
 * **Die Reihenfolge ist der Punkt:** die Kopplung geht über `https`. Weist sich
 * die Gegenseite mit einem selbst ausgestellten Zertifikat aus, scheitert schon
 * der erste Aufruf, und die App meldete „antwortet nicht", obwohl geantwortet
 * wurde.
 * </p>
 */
async function pairWith(target: PairingTarget): Promise<Paired> {
  const platform = getPlatform()

  /**
   * Warum die ausstellende Stelle nicht geholt werden konnte. Für sich genommen
   * kein Fehler — bei einem Zertifikat von Tailscale gibt es nichts zu holen.
   * Scheitert danach die Kopplung, ist er meist der Grund.
   */
  let hindernis: string | undefined

  try {
    const trusted = await trust(target)

    if (!trusted.ok && trusted.fatal === true) {
      // Nicht weiter: die Gegenseite weist sich selbst aus, und der
      // Vergleichswert fehlt oder passt nicht. Über `https` weiterzumachen
      // ergäbe nur eine zweite, unverständlichere Meldung.
      throw new Error(trusted.failure ?? 'Die Stelle der Gegenseite ließ sich nicht prüfen.')
    }

    if (!trusted.ok) {
      hindernis = trusted.failure
    }

    // Der eigene Steckbrief geht mit. Ist dieses Gerät kein mögliches Ziel,
    // gibt es keinen — dann bleibt es bei der einen Richtung, und das ist kein
    // Fehler. Ob der eigene Host gerade läuft, spielt keine Rolle: der
    // Steckbrief beschreibt, wie dieses Gerät erreichbar wäre.
    const self = await platform.node.profile().catch(() => undefined)

    // **Der eingestellte Name gilt, nicht der, den das System vergibt.** Er
    // steht sowohl in der `clients.json` der Gegenseite als auch in ihrer
    // Geräteliste — vorher konnten die beiden auseinanderlaufen.
    const name = await ownName()
    const steckbrief = self === undefined ? undefined : { ...self, name }

    const { device: paired, peer } = await pairBothWays({
      host: target.host,
      port: target.port,
      code: target.code,
      label: name,
      ...(steckbrief === undefined ? {} : { self: steckbrief }),
    })

    // Und die andere Hälfte, ohne einen zweiten Aufruf über das Netz: die
    // Gegenseite darf dieses Gerät steuern.
    const warnung = await grantPeer(peer)

    return {
      device: paired,
      devices: saveLocalDevice(paired),
      trusted: trusted.ok,
      ...(warnung === undefined ? {} : { warnung }),
    }
  } catch (failure) {
    const message = failure instanceof Error ? failure.message : String(failure)

    throw new Error(
      hindernis === undefined
        ? message
        : `${message} Das Zertifikat der Gegenseite ließ sich vorher nicht holen: ` +
          `${hindernis}`,
    )
  }
}

/** Ob der ausstellenden Stelle der Gegenseite vertraut wurde. */
interface Trusted {
  ok: boolean
  /** Warum nicht. Steht in der Meldung, falls danach die Kopplung scheitert. */
  failure?: string
  /**
   * Ob es sich lohnt, trotzdem zu koppeln. Nicht, wenn die Gegenseite eine
   * Stelle vorzeigt, die sich nicht prüfen ließ — dann wäre der nächste
   * Schritt genau der, den die Prüfung verhindern soll.
   */
  fatal?: boolean
}

/**
 * Der ausstellenden Stelle der Gegenseite vertrauen.
 *
 * <p>
 * **Verglichen wird immer** — mit dem Fingerabdruck aus dem QR-Code oder mit
 * dem abgetippten Prüfzeichen. Bis zum 18.09.2026 wurde ohne QR-Code jede
 * Stelle angenommen, die in den fünf Minuten des Codes auf Port 8442
 * antwortete; wer in dieser Zeit im WLAN dazwischensaß, hatte danach eine
 * Stelle auf dem Handy. Jetzt scheitert der Weg ohne Vergleichswert, sobald
 * die Gegenseite eine Stelle vorzeigt.
 * </p>
 */
async function trust(target: PairingTarget): Promise<Trusted> {
  const platform = getPlatform()
  const expected = certificateFingerprint(target.caFingerprint)

  if (!platform.trust.available) {
    return { ok: false }
  }

  let certificate: { base64: string; fingerprint: string }

  try {
    // Nativ holen, wo die Umgebung das kann: die Seite läuft unter `https` und
    // darf die Datei unter `http://…:8442` gar nicht erst anfragen.
    certificate =
      platform.trust.fetchAuthority === undefined
        ? expected === undefined
          ? await downloadAuthority(target.host)
          : await fetchAgentCertificate(target.host, expected)
        : await platform.trust.fetchAuthority(target.host, TRUST_PORT)
  } catch (failure) {
    // **Der Grund geht mit.** Verschluckt endete er hier, und der Ablauf lief
    // weiter in die verschlüsselte Verbindung — die ohne bestätigte Stelle
    // scheitern *muss*. Am Bildschirm stand danach „antwortet nicht", während
    // die Gegenstelle nachweislich antwortete. Ein Gerät mit Zertifikat von
    // Tailscale antwortet hier gar nicht — dann ist das kein Fehler.
    return { ok: false, failure: failure instanceof Error ? failure.message : String(failure) }
  }

  try {
    verify(certificate, expected, target.check)
    await platform.trust.install(certificate.base64, certificate.fingerprint)

    return { ok: true }
  } catch (failure) {
    return {
      ok: false,
      failure: failure instanceof Error ? failure.message : String(failure),
      fatal: true,
    }
  }
}

/**
 * Das geholte Zertifikat gegen den Vergleichswert halten: den Fingerabdruck
 * aus dem QR-Code, sonst das abgetippte Prüfzeichen. Ohne beides gibt es
 * nichts zu vergleichen — und dann wird nichts angenommen.
 *
 * Geprüft wird hier auch dann, wenn die Umgebung es nativ ebenfalls könnte:
 * eine Prüfung, die nur an einer Stelle steht, verschwindet beim nächsten Umbau.
 */
function verify(
  found: { base64: string; fingerprint: string },
  expected: string | undefined,
  check: string | undefined,
): void {
  if (expected !== undefined) {
    if (found.fingerprint.trim().toLowerCase() !== expected.trim().toLowerCase()) {
      throw new Error(
        'Das Zertifikat gehört nicht zu diesem Gerät. Im Netz sitzt jemand ' +
          'dazwischen, oder es ist das falsche Gerät.',
      )
    }

    return
  }

  if (check === undefined) {
    throw new Error(
      'Dieses Gerät weist sich selbst aus. Trage das Prüfzeichen ein, das dort ' +
        'neben dem Kopplungscode steht.',
    )
  }

  if (!matchesCheck(found.fingerprint, check)) {
    throw new Error(
      'Das Prüfzeichen passt nicht zu diesem Gerät. Im Netz sitzt jemand ' +
        'dazwischen, oder es ist das falsche Gerät.',
    )
  }
}
