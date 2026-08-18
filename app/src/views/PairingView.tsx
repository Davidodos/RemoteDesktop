import { useState } from 'react'
import { CertificateTrustStep } from './CertificateTrustStep.tsx'
import { PairingOffer } from './PairingOffer.tsx'
import {
  certificateFingerprint,
  downloadAuthority,
  fetchAgentCertificate,
  TRUST_PORT,
} from '../lib/certificateTrust.ts'
import { saveLocalDevice } from '../lib/deviceSources.ts'
import { grantPeer } from '../lib/bothWays.ts'
import { ownName } from '../lib/ownName.ts'
import { pairBothWays } from '../lib/pairing.ts'
import { DEFAULT_AGENT_PORT, parsePairingUri, type PairingTarget } from '../lib/pairingUri.ts'
import { getPlatform } from '../platform/index.ts'
import type { Device } from '../lib/types.ts'

interface Props {
  /**
   * @param warnung Ein Satz, wenn die Gegenrichtung nicht zustande kam. Die
   *   Kopplung selbst hat dann trotzdem geklappt — beides in einem Fehler zu
   *   melden hieße, eine gelungene Kopplung als Fehlschlag darzustellen.
   */
  onPaired: (devices: Device[], paired: Device, warnung?: string) => void
  onCancel: () => void
}

/**
 * Koppeln — eine Seite, beide Richtungen.
 *
 * <p>
 * **Oben: dieses Gerät koppeln** (QR-Code, darunter Code und Adresse).
 * **Darunter: ein anderes eintragen** — am Handy per Kamera oder von Hand, am
 * Rechner nur von Hand. Beides gehört auf dieselbe Seite, weil beim Koppeln
 * immer beide Seiten etwas tun.
 * </p>
 *
 * <p>
 * **Es gibt nichts mehr einzutippen außer Adresse und Code.** Bis zum
 * 16.08.2026 folgte darauf eine zweite Seite mit zwei Namensfeldern und
 * mitunter eine dritte mit einem Fingerabdruck zum Vergleichen. Der eigene Name
 * steht jetzt in den Einstellungen und geht von allein mit; die Gegenseite
 * erscheint unter dem Namen, den sie sich selbst gegeben hat.
 * </p>
 */
export function PairingView({ onPaired, onCancel }: Props): React.JSX.Element {
  const [error, setError] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)

  /**
   * Gekoppelt, aber noch nicht fertig: ein Gerät ohne Tailscale weist sich mit
   * einem selbst ausgestellten Zertifikat aus, und dem muss dieses hier erst
   * vertrauen. Ohne den Zwischenschritt stünde es in der Liste und ließe sich
   * nicht verbinden.
   */
  const [awaitingTrust, setAwaitingTrust] = useState<
    { device: Device; devices: Device[]; warnung?: string } | undefined
  >(undefined)

  const platform = getPlatform()

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
        onPaired(devices, device, warnung)

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

  if (awaitingTrust !== undefined) {
    return (
      <CertificateTrustStep
        device={awaitingTrust.device}
        onDone={() =>
          onPaired(awaitingTrust.devices, awaitingTrust.device, awaitingTrust.warnung)
        }
      />
    )
  }

  return (
    <div className="token-prompt pairing-page">
      <h1>Gerät koppeln</h1>

      {error !== undefined && <p className="error-text">{error}</p>}

      <section className="settings-group">
        <h2>Dieses Gerät koppeln</h2>
        <PairingOffer />
      </section>

      <section className="settings-group">
        <h2>Anderes Gerät eintragen</h2>

        {/* Ohne Kamera gibt es den Knopf nicht: einer, der nur eine
            Fehlermeldung erzeugt, wäre schlimmer als keiner. Im Fenster und im
            Browser bleibt das Formular darunter der Weg. */}
        {platform.capabilities.camera && (
          <button type="button" disabled={busy} onClick={() => void scan()}>
            {busy ? 'Koppeln…' : 'QR-Code scannen'}
          </button>
        )}

        <ManualForm busy={busy} onTarget={(target) => void pair(target)} />
      </section>

      <button type="button" className="secondary" disabled={busy} onClick={onCancel}>
        Zurück
      </button>
    </div>
  )
}

/**
 * Adresse und Code der Gegenseite — von Hand.
 *
 * Der einzige Weg ohne Kamera und damit der Normalfall am Rechner. Ein Knopf
 * davor hätte ihn versteckt.
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

  const ready = host.trim().length > 0 && code.trim().length === 6

  return (
    <form
      className="pairing-form"
      onSubmit={(event) => {
        event.preventDefault()

        if (ready && !busy) {
          onTarget({ host: host.trim(), port: DEFAULT_AGENT_PORT, code: code.trim() })
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
}

/**
 * Der ausstellenden Stelle der Gegenseite vertrauen.
 *
 * <p>
 * **Mit Fingerabdruck aus dem QR-Code wird verglichen, ohne ihn nicht.** Das
 * ist eine Abwägung und keine Nachlässigkeit: die Alternative wäre der
 * Bildschirm, auf dem der Wert zum Ablesen stand — und der wurde nie abgelesen.
 * Was bleibt, sichert der Kopplungscode: sechs Ziffern, fünf Minuten, genau
 * eine Kopplung. Wer in genau diesem Fenster im Netz dazwischensitzt, kommt
 * durch; wer es nicht tut, kommt nie wieder heran.
 * </p>
 */
async function trust(target: PairingTarget): Promise<Trusted> {
  const platform = getPlatform()
  const expected = certificateFingerprint(target.caFingerprint)

  if (!platform.trust.available) {
    return { ok: false }
  }

  try {
    // Nativ holen, wo die Umgebung das kann: die Seite läuft unter `https` und
    // darf die Datei unter `http://…:8442` gar nicht erst anfragen.
    const certificate =
      platform.trust.fetchAuthority === undefined
        ? expected === undefined
          ? await downloadAuthority(target.host)
          : await fetchAgentCertificate(target.host, expected)
        : verify(await platform.trust.fetchAuthority(target.host, TRUST_PORT), expected)

    await platform.trust.install(certificate.base64, certificate.fingerprint)

    return { ok: true }
  } catch (failure) {
    // **Der Grund geht mit.** Verschluckt endete er hier, und der Ablauf lief
    // weiter in die verschlüsselte Verbindung — die ohne bestätigte Stelle
    // scheitern *muss*. Am Bildschirm stand danach „antwortet nicht", während
    // die Gegenstelle nachweislich antwortete.
    return { ok: false, failure: failure instanceof Error ? failure.message : String(failure) }
  }
}

/**
 * Was nativ geholt wurde, gegen den Fingerabdruck aus dem QR-Code halten.
 *
 * Ohne Vergleichswert gibt es nichts zu prüfen — dann gilt, was der Code
 * absichert. Mit ihm wird geprüft, und zwar hier ein zweites Mal, obwohl die
 * Umgebung es ebenfalls könnte: eine Prüfung, die nur an einer Stelle steht,
 * verschwindet beim nächsten Umbau.
 */
function verify(
  found: { base64: string; fingerprint: string },
  expected: string | undefined,
): { base64: string; fingerprint: string } {
  if (
    expected !== undefined &&
    found.fingerprint.trim().toLowerCase() !== expected.trim().toLowerCase()
  ) {
    throw new Error(
      'Das Zertifikat gehört nicht zu diesem Gerät. Im Netz sitzt jemand ' +
        'dazwischen, oder es ist das falsche Gerät.',
    )
  }

  return found
}
