import { useState } from 'react'
import { fetchAgentCertificate } from '../lib/certificateTrust.ts'
import type { Device } from '../lib/types.ts'
import { getPlatform } from '../platform/index.ts'

interface Props {
  device: Device
  /** Weiter zur Geräteliste — mit oder ohne bestätigtes Zertifikat. */
  onDone: () => void
}

/**
 * Der letzte Schritt der Kopplung — nur dort, wo der erste Anlauf nicht schon
 * gereicht hat (siehe `PairingView`).
 *
 * Die Gegenseite stellt sich ihr Zertifikat selbst aus, und dieses Gerät muss
 * der ausstellenden Stelle einmal vertrauen. Sonst scheitert jede Verbindung,
 * bevor überhaupt ein Ausweis geprüft wird.
 *
 * Der Fingerabdruck aus der Kopplung wird **geprüft** und nicht angezeigt: er
 * kam über den QR-Code und nicht über das Netz, und damit ist er ein
 * Vergleichswert für die App, keine Aufgabe für einen Menschen. Angezeigt stand
 * er hier lange — verglichen hat ihn nie jemand.
 */
export function CertificateTrustStep({ device, onDone }: Props): React.JSX.Element {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)

  /** Android hat den Vorgang abgegeben — hier steht, wie es weitergeht. */
  const [manual, setManual] = useState(false)

  const platform = getPlatform()
  const fingerprint = device.caFingerprint ?? ''

  const confirm = async (): Promise<void> => {
    setBusy(true)
    setError(undefined)

    try {
      const certificate = await fetchAgentCertificate(device.host, fingerprint)

      // Ab Android 11 nimmt das System eine Zertifizierungsstelle nicht mehr
      // aus einer App entgegen. Dann liegt die Datei in den Downloads und die
      // Einstellungen sind offen — und hier steht, was dort zu tun ist. Weiter
      // geht es erst, wenn der Mensch das erledigt hat.
      if ((await platform.trust.install(certificate.base64, certificate.fingerprint)) ===
        'settings') {
        setManual(true)

        return
      }

      onDone()
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="pairing">
      <h2>Zertifikat bestätigen</h2>

      <p>
        <strong>{device.name}</strong> stellt sein Zertifikat selbst aus. Dieses Gerät muss
        der Stelle einmal vertrauen — danach nie wieder.
      </p>

      {manual && (
        <p className="hint">
          Android verlangt das seit Version 11 von Hand. <strong>RemoteDesktop-CA.crt</strong>{' '}
          liegt in den Downloads, die Einstellungen sind offen: <em>Sicherheit →
          Verschlüsselung und Anmeldedaten → Zertifikat installieren → CA-Zertifikat</em>.
          Danach hier auf „Fertig“.
        </p>
      )}

      {error !== undefined && <p className="error">{error}</p>}

      <div className="row">
        <button
          type="button"
          onClick={() => (manual ? onDone() : void confirm())}
          disabled={busy || !platform.trust.available}
        >
          {manual ? 'Fertig' : busy ? 'Einen Moment…' : 'Zertifikat bestätigen'}
        </button>

        {/*
          Überspringen bleibt möglich — die Kopplung selbst ist ja durch. Ohne
          bestätigtes Zertifikat kommt allerdings keine Verbindung zustande,
          deshalb steht es als zweite Wahl da und nicht gleichrangig.
        */}
        <button type="button" className="ghost" onClick={onDone} disabled={busy}>
          Später
        </button>
      </div>

      {!platform.trust.available && (
        <p className="hint">
          Hier geht das nicht aus der App heraus. Im Browser erscheint beim ersten Aufruf
          eine Warnung, die du einmal bestätigst.
        </p>
      )}
    </section>
  )
}
