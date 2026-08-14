import { useState } from 'react'
import { CertificateTrustStep } from './CertificateTrustStep.tsx'
import {
  certificateFingerprint,
  downloadAuthority,
  fetchAgentCertificate,
  readable,
  TRUST_PORT,
} from '../lib/certificateTrust.ts'
import { saveLocalDevice } from '../lib/deviceSources.ts'
import { suggestAlias } from '../lib/deviceNames.ts'
import { grantPeer } from '../lib/bothWays.ts'
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
 * Ein Gerät koppeln — in zwei Schritten, und der erste ist meist ein Scan.
 *
 * <p>
 * **Der Befund dahinter:** vorher füllte der Scan drei Eingabefelder, die
 * danach ausgefüllt dastanden und trotzdem angeschaut werden wollten. Adresse,
 * Port und Code sind aber nichts, worüber jemand entscheidet — sie stehen im
 * QR-Code und sind entweder richtig oder unbrauchbar. Entschieden wird nur über
 * die beiden Namen, und genau die stehen jetzt auf der zweiten Seite.
 * </p>
 *
 * <p>
 * Das Abtippen bleibt für alles ohne Kamera — im Browser und im Windows-Fenster
 * gibt es keinen Scanner, und ein Knopf, der nur eine Fehlermeldung erzeugt,
 * wäre schlimmer als keiner.
 * </p>
 */
export function PairingView({ onPaired, onCancel }: Props): React.JSX.Element {
  const [target, setTarget] = useState<PairingTarget | undefined>(undefined)
  const [manual, setManual] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  /**
   * Gekoppelt, aber noch nicht fertig: ein Rechner ohne Tailscale weist sich
   * mit einem selbst ausgestellten Zertifikat aus, und dem muss dieses Gerät
   * erst vertrauen. Ohne diesen Zwischenschritt stünde er in der Liste und
   * ließe sich nicht verbinden — ohne dass irgendwo steht, warum.
   */
  const [awaitingTrust, setAwaitingTrust] = useState<
    { device: Device; devices: Device[]; warnung?: string } | undefined
  >(undefined)

  const platform = getPlatform()

  const scan = async (): Promise<void> => {
    setError(undefined)

    try {
      setTarget(parsePairingUri(await platform.qr.scan()))
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    }
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

  if (target !== undefined) {
    return (
      <NameStep
        target={target}
        onBack={() => setTarget(undefined)}
        onPaired={(devices, device, trusted, warnung) => {
          // Nichts zu bestätigen — oder eben schon bestätigt, bevor gekoppelt
          // wurde. Ein zweites Mal danach zu fragen wäre nur Weg.
          //
          // Geprüft wird der Fingerabdruck und nicht bloß, ob das Feld gesetzt
          // ist: ein `null` vom Agent bedeutet „nichts zu bestätigen", sah aber
          // aus wie ein Wert. Siehe `certificateFingerprint`.
          if (certificateFingerprint(device.caFingerprint) === undefined || trusted) {
            onPaired(devices, device, warnung)

            return
          }

          setAwaitingTrust({ device, devices, ...(warnung === undefined ? {} : { warnung }) })
        }}
      />
    )
  }

  if (manual) {
    return (
      <ManualStep
        onBack={() => setManual(false)}
        onTarget={(entered) => {
          setError(undefined)
          setTarget(entered)
        }}
      />
    )
  }

  return (
    <div className="token-prompt">
      <h1>Gerät koppeln</h1>
      <p>
        Am Rechner unter „Geräte“ auf „Code anzeigen“ klicken. Der Code gilt fünf Minuten
        und lässt sich nur einmal verwenden.
      </p>

      {error !== undefined && <p className="error-text">{error}</p>}

      {platform.capabilities.camera && (
        <button type="button" onClick={() => void scan()}>
          QR-Code scannen
        </button>
      )}

      <button type="button" className="secondary" onClick={() => setManual(true)}>
        Von Hand eingeben
      </button>

      <button type="button" className="secondary" onClick={onCancel}>
        Abbrechen
      </button>
    </div>
  )
}

/**
 * Die einzige Seite, auf der es etwas zu entscheiden gibt: wie dieses Gerät am
 * Rechner heißt, und wie der Rechner hier heißt.
 */
function NameStep({
  target,
  onPaired,
  onBack,
}: {
  target: PairingTarget
  onPaired: (
    devices: Device[],
    paired: Device,
    trusted: boolean,
    warnung?: string,
  ) => void
  onBack: () => void
}): React.JSX.Element {
  const [label, setLabel] = useState(defaultLabel())

  /**
   * Die Stelle, die geholt wurde und noch niemand bestätigt hat. Solange sie
   * hier steht, wird nicht gekoppelt.
   */
  const [offered, setOffered] = useState<
    { base64: string; fingerprint: string } | undefined
  >(undefined)

  /** Ob die gezeigte Stelle bestätigt wurde — dann wird nicht erneut gefragt. */
  const [confirmed, setConfirmed] = useState(false)
  const [alias, setAlias] = useState(suggestAlias(target.host))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  const platform = getPlatform()

  /**
   * Erst vertrauen, dann koppeln.
   *
   * <p>
   * **Der Befund dahinter:** die Kopplung geht über `https`. Weist sich der
   * Rechner mit einem selbst ausgestellten Zertifikat aus, scheitert schon
   * dieser erste Aufruf — und die App meldete „der Rechner antwortet nicht",
   * obwohl er antwortete. Bestätigt wurde die Stelle erst *nach* der Kopplung,
   * also nie. Der Fingerabdruck aus dem QR-Code dreht die Reihenfolge um.
   * </p>
   */
  const trust = async (): Promise<boolean> => {
    const fingerprint = certificateFingerprint(target.caFingerprint)

    if (fingerprint === undefined || !platform.trust.available) {
      return false
    }

    // Nativ holen, wo die Umgebung das kann: die Seite läuft unter `https` und
    // darf die Datei unter `http://…:8442` gar nicht erst anfragen.
    const certificate =
      platform.trust.fetchAuthority === undefined
        ? await fetchAgentCertificate(target.host, fingerprint)
        : verify(await platform.trust.fetchAuthority(target.host, TRUST_PORT), fingerprint)

    await platform.trust.install(certificate.base64, certificate.fingerprint)

    return true
  }

  const submit = async (): Promise<void> => {
    setBusy(true)
    setError(undefined)

    try {
      // Ohne QR-Code kam kein Fingerabdruck mit. Dann wird die Stelle geholt
      // und **gezeigt**: verglichen wird mit dem, was auf dem Bildschirm der
      // Gegenstelle steht. Derselbe Anker wie beim Scannen, nur mit dem Auge
      // statt der Kamera.
      if (certificateFingerprint(target.caFingerprint) === undefined && !confirmed) {
        const found = await discover(platform, target.host)

        if (found !== undefined) {
          setOffered(found)
          setBusy(false)

          return
        }
      }

      const trusted = await trust()

      // Der eigene Steckbrief geht mit. Ist dieses Gerät kein mögliches Ziel,
      // gibt es keinen — dann bleibt es bei der einen Richtung, und das ist
      // kein Fehler. Ob der eigene Host gerade läuft, spielt keine Rolle: der
      // Steckbrief beschreibt, wie dieses Gerät erreichbar wäre.
      const self = await platform.node.profile().catch(() => undefined)

      const { device: paired, peer } = await pairBothWays({
        host: target.host,
        port: target.port,
        code: target.code,
        label: label.trim(),
        ...(self === undefined ? {} : { self }),
      })

      // Und die andere Hälfte, ohne einen zweiten Aufruf über das Netz: die
      // Gegenseite darf dieses Gerät steuern.
      const warnung = await grantPeer(peer)

      // Der eigene Name geht nirgendwo hin — er steht neben den Zugangsdaten
      // dieses Geräts und sonst nirgends.
      const device =
        alias.trim().length > 0 ? { ...paired, alias: alias.trim() } : paired

      onPaired(saveLocalDevice(device), device, trusted, warnung)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  const ready = label.trim().length > 0

  if (offered !== undefined) {
    return (
      <div className="token-prompt">
        <h1>Ist das die richtige Stelle?</h1>
        <p>
          <code>{target.host}</code> weist sich mit einem{' '}
          <strong>selbst ausgestellten</strong> Zertifikat aus. Ohne QR-Code kam
          kein Vergleichswert mit — also vergleiche ihn selbst: auf dem anderen
          Gerät steht derselbe Fingerabdruck unter „Dieses Gerät freigeben“.
        </p>

        <p className="fingerprint">
          <small>{readable(offered.fingerprint)}</small>
        </p>

        <p>
          Stimmen die beiden nicht überein, brich ab: dann sitzt jemand im Netz
          dazwischen, oder es ist das falsche Gerät.
        </p>

        {error !== undefined && <p className="error-text">{error}</p>}

        <button
          type="button"
          disabled={busy}
          onClick={() => {
            setBusy(true)
            setError(undefined)

            void platform.trust
              .install(offered.base64, offered.fingerprint)
              .then(
                () => {
                  setConfirmed(true)
                  setOffered(undefined)
                  setBusy(false)
                },
                (failure: unknown) => {
                  setError(failure instanceof Error ? failure.message : String(failure))
                  setBusy(false)
                },
              )
          }}
        >
          Stimmt überein — weiter
        </button>

        <button type="button" className="secondary" onClick={onBack}>
          Abbrechen
        </button>
      </div>
    )
  }

  return (
    <form
      className="token-prompt"
      onSubmit={(event) => {
        event.preventDefault()

        if (ready && !busy) {
          void submit()
        }
      }}
    >
      <h1>Fast fertig</h1>
      <p>
        Der Rechner unter <code>{target.host}</code> ist erkannt. Fehlen nur noch die
        Namen.
      </p>

      {certificateFingerprint(target.caFingerprint) !== undefined && (
        <p>
          Dieser Rechner weist sich mit einem <strong>selbst ausgestellten</strong>{' '}
          Zertifikat aus. Beim Koppeln muss dein Handy der ausstellenden Stelle einmal
          vertrauen; der Fingerabdruck stand im QR-Code und wird geprüft. Wenn der
          Rechner Tailscale benutzt, ist das vermeidbar: dort unter „Netz“ das
          Zertifikat von Tailscale holen und den Agent neu starten — dann entfällt
          dieser Schritt ganz.
        </p>
      )}

      {error !== undefined && <p className="error-text">{error}</p>}

      <label className="field-label" htmlFor="pair-label">
        Name dieses Geräts — so steht es am Rechner in der Liste
      </label>
      <input
        id="pair-label"
        value={label}
        onChange={(event) => setLabel(event.target.value)}
        placeholder="z. B. Handy"
      />

      <label className="field-label" htmlFor="pair-alias">
        Name für den Rechner — gilt nur hier und ist jederzeit änderbar
      </label>
      <input
        id="pair-alias"
        value={alias}
        onChange={(event) => setAlias(event.target.value)}
        placeholder="z. B. Arbeitsrechner"
      />

      <button type="submit" disabled={!ready || busy}>
        {busy ? 'Koppeln…' : 'Koppeln'}
      </button>

      <button type="button" className="secondary" onClick={onBack} disabled={busy}>
        Zurück
      </button>
    </form>
  )
}

/** Adresse, Port und Code von Hand — für alles ohne Kamera. */
function ManualStep({
  onTarget,
  onBack,
}: {
  onTarget: (target: PairingTarget) => void
  onBack: () => void
}): React.JSX.Element {
  const [host, setHost] = useState('')
  const [code, setCode] = useState('')

  const ready = host.trim().length > 0 && code.trim().length === 6

  return (
    <form
      className="token-prompt"
      onSubmit={(event) => {
        event.preventDefault()

        if (ready) {
          onTarget({ host: host.trim(), port: DEFAULT_AGENT_PORT, code: code.trim() })
        }
      }}
    >
      <h1>Von Hand eingeben</h1>
      <p>Beides steht am Rechner unter „Geräte“, neben dem QR-Code.</p>

      <input
        value={host}
        onChange={(event) => setHost(event.target.value)}
        placeholder="Adresse des Rechners"
        autoCapitalize="off"
        autoCorrect="off"
        spellCheck={false}
      />

      <input
        value={code}
        onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
        placeholder="6-stelliger Code"
        inputMode="numeric"
        autoComplete="off"
      />

      <button type="submit" disabled={!ready}>
        Weiter
      </button>

      <button type="button" className="secondary" onClick={onBack}>
        Zurück
      </button>
    </form>
  )
}

/**
 * Unter diesem Namen taucht das Gerät in der Liste am Rechner auf. Ein
 * brauchbarer Vorschlag ist wichtiger, als er aussieht: wer widerrufen will,
 * muss Monate später erkennen, welcher Eintrag welches Gerät ist.
 */
function defaultLabel(): string {
  return getPlatform().name === 'web' ? 'Browser' : 'Handy'
}

/**
 * Die Stelle der Gegenseite holen, ohne Vergleichswert.
 *
 * `undefined` heißt: es gibt keine — die Gegenstelle hat ein Zertifikat, dem
 * ohnehin jeder glaubt (Tailscale), oder der Port ist zu. Dann gibt es nichts
 * zu bestätigen, und die Kopplung läuft ohne diesen Schritt weiter.
 */
async function discover(
  platform: ReturnType<typeof getPlatform>,
  host: string,
): Promise<{ base64: string; fingerprint: string } | undefined> {
  if (!platform.trust.available) {
    return undefined
  }

  try {
    return platform.trust.fetchAuthority === undefined
      ? await downloadAuthority(host)
      : await platform.trust.fetchAuthority(host, TRUST_PORT)
  } catch {
    return undefined
  }
}

/**
 * Was nativ geholt wurde, gegen den Fingerabdruck aus der Kopplung halten.
 *
 * Die Prüfung steht hier ein zweites Mal, obwohl die Umgebung sie ebenfalls
 * machen könnte: sie ist der einzige Grund, warum das Zertifikat unverschlüsselt
 * kommen darf, und eine Prüfung, die nur an einer Stelle steht, ist eine, die
 * beim nächsten Umbau verschwindet.
 */
function verify(
  found: { base64: string; fingerprint: string },
  expected: string,
): { base64: string; fingerprint: string } {
  if (found.fingerprint.trim().toLowerCase() !== expected.trim().toLowerCase()) {
    throw new Error(
      'Das Zertifikat gehört nicht zu diesem Gerät. Nicht bestätigen — ' +
        'im Netz sitzt jemand dazwischen, oder es ist das falsche Gerät.',
    )
  }

  return found
}
