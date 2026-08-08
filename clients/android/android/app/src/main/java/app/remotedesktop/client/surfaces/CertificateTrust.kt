package app.remotedesktop.client.surfaces

import android.content.ContentValues
import android.content.Context
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.security.KeyChain
import java.io.ByteArrayInputStream
import java.io.File
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
     *
     * **Nur bis Android 10.** Ab Android 11 nimmt der Zertifikatsinstallierer
     * über diesen Weg keine Zertifizierungsstellen mehr an: der Dialog kommt
     * gar nicht erst, die App bekommt keinen Fehler, und für den Menschen davor
     * passiert schlicht nichts. Genau das stand am echten Gerät — „auf
     * ‚Zertifikat bestätigen‘ drücken tut nichts". Deshalb der zweite Weg
     * unten.
     */
    fun installIntent(certificate: X509Certificate) =
        KeyChain.createInstallIntent().apply {
            putExtra(KeyChain.EXTRA_CERTIFICATE, certificate.encoded)
            putExtra(KeyChain.EXTRA_NAME, "RemoteDesktop")
        }

    /** Ob der Dialog von Android überhaupt noch etwas annimmt. */
    val dialogWorks: Boolean
        get() = Build.VERSION.SDK_INT < Build.VERSION_CODES.R

    /** Wie die Datei heißt, die in den Downloads landet. */
    const val FileName = "RemoteDesktop-CA.crt"

    /**
     * Legt das Zertifikat als Datei ab, damit der Mensch es in den
     * Einstellungen auswählen kann.
     *
     * <p>
     * Ab Android 11 führt der Weg ausschließlich über *Einstellungen →
     * Sicherheit → Verschlüsselung und Anmeldedaten → Zertifikat installieren →
     * CA-Zertifikat*, und dort wird eine Datei ausgewählt. Also muss eine
     * dastehen — in den Downloads, weil das der eine Ordner ist, den jeder
     * Dateiwähler zeigt.
     * </p>
     *
     * @return Wo sie liegt, für die Anzeige — oder `null`, wenn das Ablegen
     *   nicht geklappt hat.
     */
    fun saveForImport(context: Context, certificate: X509Certificate): String? = try {
        val bytes = certificate.encoded

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, FileName)
                put(MediaStore.Downloads.MIME_TYPE, "application/x-x509-ca-cert")
                put(MediaStore.Downloads.IS_PENDING, 1)
            }

            val resolver = context.contentResolver
            val target = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)

            if (target == null) {
                null
            } else {
                resolver.openOutputStream(target)?.use { stream -> stream.write(bytes) }

                values.clear()
                values.put(MediaStore.Downloads.IS_PENDING, 0)
                resolver.update(target, values, null, null)

                FileName
            }
        } else {
            val folder =
                Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)

            File(folder, FileName).also { file -> file.writeBytes(bytes) }.name
        }
    } catch (failure: Exception) {
        // Ohne Datei bleibt der Hinweis auf die Einstellungen — der hilft schon
        // mehr als ein Knopf, der nichts tut.
        null
    }
}
