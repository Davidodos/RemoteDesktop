package app.remotedesktop.client.surfaces

import android.util.Base64
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
                activity.startActivity(CertificateTrust.installIntent(outcome.certificate))

                call.resolve()
            }
        }
    }
}
