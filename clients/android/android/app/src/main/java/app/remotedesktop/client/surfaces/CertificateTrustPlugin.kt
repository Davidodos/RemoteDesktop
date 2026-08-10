package app.remotedesktop.client.surfaces

import android.content.Intent
import android.provider.Settings
import android.util.Base64
import java.net.HttpURLConnection
import java.net.URL
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin

/**
 * Die Brücke, über die die App ein selbst ausgestelltes Agent-Zertifikat dem
 * System vorlegt.
 *
 * Aufgerufen aus `app/src/platform/capacitor.ts`; dort heißt das
 * `trust.install()`. Im Browser und im Windows-Fenster gibt es das nicht — dort
 * meldet die Plattform `available: false`, und die Oberfläche bietet den Knopf
 * gar nicht erst an.
 */
@CapacitorPlugin(name = "CertificateTrust")
class CertificateTrustPlugin : Plugin() {

    /**
     * Holt die Zertifizierungsstelle der Gegenstelle.
     *
     * **Warum das nicht die Seite tut:** die App läuft unter `https://localhost`,
     * die Datei liegt unter `http://<adresse>:8442/ca.crt`. Chromium verwirft
     * das als aktiven Mixed Content, bevor eine Verbindung zustande kommt — und
     * die Ausnahme sieht aus wie ein Rechner, der nicht antwortet. Genau diese
     * Meldung stand am Gerät, während der Agent lief.
     *
     * Hier ist es eine gewöhnliche HTTP-Anfrage ohne diese Sperre. Geprüft wird
     * nichts: das tut die Seite, die auch weiß, womit zu vergleichen ist.
     */
    @PluginMethod
    fun fetch(call: PluginCall) {
        val host = call.getString("host").orEmpty().trim()
        val port = call.getInt("port") ?: 8442

        if (host.isEmpty()) {
            call.reject("Ohne Adresse gibt es nichts zu holen.")
            return
        }

        // Nicht auf dem Haupt-Thread: eine Netzanfrage dort wirft
        // NetworkOnMainThreadException, und zwar immer.
        Thread {
            try {
                val connection = (URL("http://\$host:\$port/ca.crt").openConnection()
                    as HttpURLConnection).apply {
                    connectTimeout = 5000
                    readTimeout = 5000
                }

                val bytes = try {
                    if (connection.responseCode !in 200..299) {
                        throw java.io.IOException("HTTP \${connection.responseCode}")
                    }

                    connection.inputStream.use { it.readBytes() }
                } finally {
                    connection.disconnect()
                }

                if (bytes.isEmpty()) {
                    call.reject("Die Gegenstelle hat eine leere Datei geliefert.")
                    return@Thread
                }

                call.resolve(
                    JSObject()
                        .put("base64", Base64.encodeToString(bytes, Base64.NO_WRAP))
                        .put("fingerprint", CertificateTrust.fingerprint(bytes)),
                )
            } catch (failure: Exception) {
                call.reject(
                    "Port \$port an \$host antwortet nicht. Läuft die Gegenstelle, " +
                        "und hängen beide Geräte im selben Netz?",
                )
            }
        }.start()
    }

    @PluginMethod
    fun install(call: PluginCall) {
        val fingerprint = call.getString("fingerprint").orEmpty()

        // Das Umwandeln bleibt hier und nicht in CertificateTrust: `Base64`
        // gehört zu Android und wäre im Testlauf nur eine Attrappe, die 0
        // zurückgibt. Was geprüft wird, soll ohne Android prüfbar bleiben.
        val certificate = try {
            Base64.decode(call.getString("certificate").orEmpty(), Base64.DEFAULT)
        } catch (failure: IllegalArgumentException) {
            call.reject("Das ist kein lesbares Zertifikat.")

            return
        }

        when (val outcome = CertificateTrust.inspect(certificate, fingerprint)) {
            is CertificateTrust.Outcome.Rejected -> call.reject(outcome.reason)

            is CertificateTrust.Outcome.Ready -> {
                // Ab hier gehört der Vorgang dem System: es zeigt seinen eigenen
                // Dialog samt Warnung, und der Nutzer darf ablehnen. Die App
                // erfährt nur, dass sie gefragt hat.
                //
                // Ab Android 11 gibt es diesen Dialog für Zertifizierungsstellen
                // nicht mehr. Dann wird die Datei abgelegt und die zuständige
                // Seite der Einstellungen geöffnet — und die App sagt, was dort
                // zu tun ist. Ein Knopf, der wortlos nichts tut, war der
                // schlechteste aller Zustände.
                val answer = JSObject()

                if (CertificateTrust.dialogWorks) {
                    activity.startActivity(CertificateTrust.installIntent(outcome.certificate))
                    answer.put("mode", "dialog")
                } else {
                    val file = CertificateTrust.saveForImport(activity, outcome.certificate)

                    activity.startActivity(
                        Intent(Settings.ACTION_SECURITY_SETTINGS)
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    )

                    answer.put("mode", "settings")
                    answer.put("file", file ?: "")
                }

                call.resolve(answer)
            }
        }
    }
}
