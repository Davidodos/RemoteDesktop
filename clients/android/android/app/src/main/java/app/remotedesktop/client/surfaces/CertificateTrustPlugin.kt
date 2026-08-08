package app.remotedesktop.client.surfaces

import android.content.Intent
import android.provider.Settings
import android.util.Base64
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
