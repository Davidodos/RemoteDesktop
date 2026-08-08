import { useState } from 'react'
import { fetchAgentCertificate, readable } from '../lib/certificateTrust.ts'
import type { Device } from '../lib/types.ts'
import { getPlatform } from '../platform/index.ts'

interface Props {
  device: Device
  /** Weiter zur Geräteliste — mit oder ohne bestätigtes Zertifikat. */
  onDone: () => void
}

/**
 * Der letzte Schritt der Kopplung, wenn der Rechner ohne Tailscale läuft.
 *
 * Er stellt sich sein Zertifikat dann selbst aus, und dieses Gerät muss der
 * ausstellenden Stelle einmal vertrauen — sonst scheitert jede Verbindung,
 * bevor überhaupt ein Ausweis geprüft wird.
 *
 * Der Fingerabdruck steht hier, weil er das Einzige ist, was diesen Schritt
 * sicher macht: er kam über den Kopplungscode, also über den Bildschirm des
 * Rechners, und nicht über das Netz. Das Zertifikat selbst wird unverschlüsselt
 * geholt (anders geht es nicht) und nur angenommen, wenn beides zusammenpasst.
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
        <strong>{device.name}</strong> weist sich mit einem <strong>selbst ausgestellten</strong>{' '}
        Zertifikat aus. Dein Handy muss der ausstellenden Stelle einmal vertrauen — danach nie
        wieder, auch wenn sich die Adresse ändert.
      </p>

      <p>
        Benutzt der Rechner Tailscale, ist das vermeidbar: dort im Fenster unter „Netz“ das
        Zertifikat von Tailscale holen und den Agent neu starten. Dann ist der Aussteller
        öffentlich bekannt und hier gibt es nichts mehr zu bestätigen.
      </p>

      <p className="fingerprint">
        <small>{readable(fingerprint)}</small>
      </p>

      {manual && (
        <p className="hint">
          Android lässt das seit Version 11 nicht mehr aus einer App heraus zu. Die Datei
          <strong> RemoteDesktop-CA.crt</strong> liegt jetzt in deinen Downloads, und die
          Einstellungen sind offen. Dort: <em>Sicherheit → Verschlüsselung und Anmeldedaten
          → Zertifikat installieren → CA-Zertifikat</em>, dann die Datei auswählen. Danach
          hier auf „Fertig“.
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
          In dieser Umgebung lässt sich das nicht aus der App heraus erledigen. Im Browser
          erscheint stattdessen beim ersten Aufruf eine Warnung, die du einmal bestätigst.
        </p>
      )}
    </section>
  )
}
