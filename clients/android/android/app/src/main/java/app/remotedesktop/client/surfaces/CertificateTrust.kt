package app.remotedesktop.client.surfaces

import android.security.KeyChain
import java.io.ByteArrayInputStream
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate

/**
 * Ein Zertifikat, das ein Agent sich selbst ausgestellt hat, dem System zum
 * Bestätigen vorlegen.
 *
 * **Warum das nicht heimlich geht:** Android öffnet dafür seinen eigenen
 * Dialog. Eine App kann eine Zertifizierungsstelle nicht im Vorbeigehen
 * installieren — sie kann sie nur vorschlagen. Das ist richtig so: wer eine
 * Stelle bestätigt, entscheidet für sein ganzes Gerät.
 *
 * Geprüft wird trotzdem hier noch einmal, obwohl die App den Fingerabdruck
 * bereits verglichen hat (`app/src/lib/certificateTrust.ts`). Zwei Gründe: die
 * Weboberfläche ist austauschbar, und eine Prüfung, die nur an einer Stelle
 * steht, ist eine, die beim nächsten Umbau verschwindet.
 */
object CertificateTrust {

    /** Was beim Prüfen herauskommen kann. */
    sealed interface Outcome {
        data class Ready(val certificate: X509Certificate, val fingerprint: String) : Outcome
        data class Rejected(val reason: String) : Outcome
    }

    /**
     * Liest das Zertifikat und sagt, ob es das erwartete ist.
     *
     * @param expected Der Fingerabdruck aus der Kopplung — kleingeschrieben,
     *   64 Hexzeichen. Ohne ihn gibt es kein „ready".
     */
    fun inspect(raw: ByteArray, expected: String): Outcome {
        if (raw.isEmpty()) {
            return Outcome.Rejected("Das Zertifikat ist leer.")
        }

        val wanted = expected.trim().lowercase()

        if (!wanted.matches(Regex("^[0-9a-f]{64}$"))) {
            return Outcome.Rejected("Ohne Fingerabdruck aus der Kopplung wird nichts bestätigt.")
        }

        val found = fingerprint(raw)

        if (found != wanted) {
            // Der eine Fall, der zählt: jemand im Netz schiebt sein eigenes
            // Zertifikat unter.
            return Outcome.Rejected(
                "Das Zertifikat gehört nicht zu diesem Rechner. Nicht bestätigen."
            )
        }

        val certificate = parse(raw)
            ?: return Outcome.Rejected("Das ist kein X.509-Zertifikat.")

        // Ein Serverzertifikat lässt sich gar nicht als Stelle hinterlegen —
        // Android nähme es nicht an. Der Hinweis hier ist trotzdem besser als
        // ein Systemdialog, der wortlos nichts tut.
        if (certificate.basicConstraints < 0) {
            return Outcome.Rejected(
                "Dieses Zertifikat ist keine Zertifizierungsstelle."
            )
        }

        return Outcome.Ready(certificate, found)
    }

    /** `sha256` über die Datei, kleingeschrieben und ohne Trennzeichen. */
    fun fingerprint(raw: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(raw)
            .joinToString("") { byte -> "%02x".format(byte) }

    private fun parse(raw: ByteArray): X509Certificate? = try {
        CertificateFactory.getInstance("X.509")
            .generateCertificate(ByteArrayInputStream(raw)) as? X509Certificate
    } catch (failure: CertificateException) {
        null
    }

    /**
     * Der Auftrag ans System. Was danach geschieht, gehört dem System: es fragt
     * selbst nach, zeigt seine eigene Warnung und darf abgelehnt werden.
     */
    fun installIntent(certificate: X509Certificate) =
        KeyChain.createInstallIntent().apply {
            putExtra(KeyChain.EXTRA_CERTIFICATE, certificate.encoded)
            putExtra(KeyChain.EXTRA_NAME, "RemoteDesktop")
        }
}
