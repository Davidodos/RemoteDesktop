import { useState } from 'react'
import { CertificateTrustStep } from './CertificateTrustStep.tsx'
import { PairingOffer } from './PairingOffer.tsx'
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
 * Neues Gerät koppeln — eine Seite mit beiden Richtungen.
 *
 * <p>
 * **Oben: dieses Gerät anbieten** (Code, QR-Code, eigene Adresse). **Darunter:
 * ein anderes eintragen** (seine Adresse, sein Code). Beides gehört auf
 * dieselbe Seite, weil beim Koppeln immer beide Seiten etwas tun — wer nur die
 * eine Hälfte findet, sucht die andere in den Einstellungen.
 * </p>
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

  return (
    <div className="token-prompt pairing-page">
      <h1>Neues Gerät koppeln</h1>

      {error !== undefined && <p className="error-text">{error}</p>}

      <section className="settings-group">
        <h2>Dieses Gerät anbieten</h2>
        <PairingOffer />
      </section>

      <section className="settings-group">
        <h2>Ein anderes Gerät eintragen</h2>
        <p className="settings-hint">
          Adresse und Code stehen dort unter „Geräte → Neues Gerät koppeln“.
          Danach werden nur noch die beiden Namen vergeben.
        </p>

        {/* Ohne Kamera gibt es den Knopf nicht: einer, der nur eine
            Fehlermeldung erzeugt, wäre schlimmer als keiner. */}
        {platform.capabilities.camera && (
          <button type="button" onClick={() => void scan()}>
            QR-Code scannen
          </button>
        )}

        <ManualForm onTarget={setTarget} />
      </section>

      <button type="button" className="secondary" onClick={onCancel}>
        Zurück zu den Geräten
      </button>
    </div>
  )
}

/**
 * Die einzige Seite, auf der es etwas zu entscheiden gibt: wie dieses Gerät
 * drüben heißt, und wie die Gegenseite hier heißt.
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

    /**
     * Warum die Stelle nicht geholt werden konnte. Ein Fehlschlag dort ist für
     * sich genommen keiner — bei einem Zertifikat von Tailscale gibt es nichts
     * zu holen. Scheitert danach aber die Kopplung, ist er meist der Grund, und
     * dann gehört er in die Meldung.
     */
    let hindernis: string | undefined

    try {
      // Ohne QR-Code kam kein Fingerabdruck mit. Dann wird die Stelle geholt
      // und **gezeigt**: verglichen wird mit dem, was auf dem Bildschirm der
      // Gegenstelle steht. Derselbe Anker wie beim Scannen, nur mit dem Auge
      // statt der Kamera.
      if (certificateFingerprint(target.caFingerprint) === undefined && !confirmed) {
        const found = await discover(platform, target.host)

        if (found.certificate !== undefined) {
          setOffered(found.certificate)
          setBusy(false)

          return
        }

        hindernis = found.failure
      }

      const trusted = await trust()

      // Der eigene Steckbrief geht mit. Ist dieses Gerät kein mögliches Ziel,
      // gibt es keinen — dann bleibt es bei der einen Richtung, und das ist
      // kein Fehler. Ob der eigene Host gerade läuft, spielt keine Rolle: der
      // Steckbrief beschreibt, wie dieses Gerät erreichbar wäre.
      const self = await platform.node.profile().catch(() => undefined)

      // **Der eingetippte Name gilt, nicht der, den dieses Gerät von sich
      // angibt.** Genau danach wird auf dieser Seite gefragt: „wie soll dieses
      // Gerät am anderen heißen?". Er ging bisher nur in die `clients.json` der
      // Gegenseite — in ihre Geräteliste kam der Selbstname aus dem Steckbrief,
      // bei einem Handy also das, was unter „Gerätename" in den
      // Android-Einstellungen steht. Wer „Handy" eintippte, fand drüben „David"
      // wieder und konnte nicht wissen, woher das kam.
      const steckbrief =
        self === undefined ? undefined : { ...self, name: label.trim() }

      const { device: paired, peer } = await pairBothWays({
        host: target.host,
        port: target.port,
        code: target.code,
        label: label.trim(),
        ...(steckbrief === undefined ? {} : { self: steckbrief }),
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
      const message = failure instanceof Error ? failure.message : String(failure)

      setError(
        hindernis === undefined
          ? message
          : `${message}\n\nDas Zertifikat dieses Geräts ließ sich vorher nicht ` +
            `holen: ${hindernis}. Ohne bestätigte Stelle kann die Verbindung ` +
            'nicht zustande kommen — das ist vermutlich der eigentliche Grund.',
      )
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
          Gerät steht derselbe Fingerabdruck unter „Geräte → Neues Gerät
          koppeln“, gleich unter der Adresse.
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
        Das Gerät unter <code>{target.host}</code> ist erkannt. Fehlen nur noch die
        Namen.
      </p>

      {certificateFingerprint(target.caFingerprint) !== undefined && (
        <p>
          Dieses Gerät weist sich mit einem <strong>selbst ausgestellten</strong>{' '}
          Zertifikat aus. Beim Koppeln muss dieses hier der ausstellenden Stelle einmal
          vertrauen; der Fingerabdruck stand im QR-Code und wird geprüft. Bei einem
          Rechner mit Tailscale ist das vermeidbar: dort unter „Einstellungen → Netz“
          das Zertifikat von Tailscale holen und den Agent neu starten — dann entfällt
          dieser Schritt ganz.
        </p>
      )}

      {error !== undefined && <p className="error-text">{error}</p>}

      <label className="field-label" htmlFor="pair-label">
        Name dieses Geräts — so steht es drüben in der Liste
      </label>
      <input
        id="pair-label"
        value={label}
        onChange={(event) => setLabel(event.target.value)}
        placeholder="z. B. Handy"
      />

      <label className="field-label" htmlFor="pair-alias">
        Name für das andere Gerät — gilt nur hier und ist jederzeit änderbar
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

/**
 * Adresse und Code der Gegenseite — von Hand.
 *
 * Kein eigener Schritt mehr, sondern ein Feld auf derselben Seite: es ist der
 * einzige Weg ohne Kamera, und ein Knopf davor hätte ihn versteckt, wo er der
 * Normalfall ist.
 */
function ManualForm({
  onTarget,
}: {
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

        if (ready) {
          onTarget({ host: host.trim(), port: DEFAULT_AGENT_PORT, code: code.trim() })
        }
      }}
    >
      <label className="field-label" htmlFor="pair-host">
        Adresse des anderen Geräts
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
        Sein Kopplungscode
      </label>
      <input
        id="pair-code"
        value={code}
        onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
        placeholder="6 Ziffern"
        inputMode="numeric"
        autoComplete="off"
      />

      <button type="submit" disabled={!ready}>
        Weiter
      </button>
    </form>
  )
}

/**
 * Unter diesem Namen taucht das Gerät in der Liste der Gegenseite auf. Ein
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
): Promise<Discovered> {
  if (!platform.trust.available) {
    return {}
  }

  try {
    const certificate =
      platform.trust.fetchAuthority === undefined
        ? await downloadAuthority(host)
        : await platform.trust.fetchAuthority(host, TRUST_PORT)

    return { certificate }
  } catch (failure) {
    // **Der Grund geht mit.** Vorher endete er hier, und der Ablauf lief weiter
    // in die verschlüsselte Verbindung — die ohne bestätigte Stelle scheitern
    // *muss*. Am Bildschirm stand danach „antwortet nicht", während die
    // Gegenstelle nachweislich antwortete: erreichbar auf beiden Ports, das
    // Zertifikat abholbar. Ein verschluckter Fehler an dieser Stelle kostet
    // jeden, der ihn sucht, den Blick auf die eine Auskunft, die weiterhilft.
    return { failure: failure instanceof Error ? failure.message : String(failure) }
  }
}

/**
 * Was beim Holen der Stelle herauskam.
 *
 * Beides fehlt, wo es nichts zu holen gibt — eine Umgebung ohne Vertrauensweg.
 * Das ist kein Fehler und bleibt still.
 */
interface Discovered {
  certificate?: { base64: string; fingerprint: string }
  /** Warum es nicht ging. Steht später in der Meldung, falls die Kopplung scheitert. */
  failure?: string
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
