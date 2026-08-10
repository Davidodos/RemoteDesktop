package app.remotedesktop.client.host

import com.getcapacitor.JSArray
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin

/**
 * Die Brücke, über die die App dieses Gerät steuerbar macht.
 *
 * Aufgerufen aus `app/src/platform/capacitor.ts`; dort heißt das `host.start()`
 * und `host.pairingCode()`. Im Browser und im Windows-Fenster gibt es das
 * nicht — dort meldet die Plattform `available: false`, und die Freigabeseite
 * sagt, warum.
 *
 * Der Kopplungscode wird hier und nicht über HTTP geholt: der Endpunkt dafür
 * ist absichtlich nur vom Gerät selbst erreichbar, und „vom Gerät selbst"
 * heißt genau das hier.
 */
@CapacitorPlugin(name = "Host")
class HostPlugin : Plugin() {

    @PluginMethod
    fun status(call: PluginCall) {
        call.resolve(describe())
    }

    @PluginMethod
    fun start(call: PluginCall) {
        HostService.start(context)

        // Der Dienst startet den Server; hier wird nur angestoßen. Die Antwort
        // beschreibt deshalb den Stand von gleich, nicht den von jetzt — die
        // Oberfläche fragt nach dem Umschalten ohnehin noch einmal nach.
        call.resolve(describe(running = true))
    }

    @PluginMethod
    fun stop(call: PluginCall) {
        HostService.stop(context)

        call.resolve(describe(running = false))
    }

    @PluginMethod
    fun pairingCode(call: PluginCall) {
        val runtime = HostRuntime.of(context)

        if (!runtime.isRunning) {
            call.reject("Der Host läuft nicht — erst freigeben, dann koppeln.")
            return
        }

        val code = runtime.issueCode().issue()

        call.resolve(
            JSObject()
                .put("code", code)
                .put("expiresInSeconds", PairingCodes.LIFETIME_MS / 1000)
                .put("pairingUri", runtime.pairingUri(code)),
        )
    }

    @PluginMethod
    fun clients(call: PluginCall) {
        val array = JSArray()

        HostRuntime.of(context).clients().forEach { client ->
            array.put(
                JSObject()
                    .put("id", client.id)
                    .put("label", client.label)
                    .put("scopes", JSArray(client.scopes.toTypedArray()))
                    .put("lastSeenAt", client.lastSeenAt),
            )
        }

        call.resolve(JSObject().put("clients", array))
    }

    @PluginMethod
    fun revoke(call: PluginCall) {
        val id = call.getString("id")

        if (id.isNullOrBlank()) {
            call.reject("Ohne Kennung lässt sich nichts widerrufen.")
            return
        }

        HostRuntime.of(context).revoke(id)

        call.resolve()
    }

    private fun describe(running: Boolean? = null): JSObject {
        val runtime = HostRuntime.of(context)

        return JSObject()
            .put("running", running ?: runtime.isRunning)
            .put("deviceName", runtime.deviceName)
            .put("port", runtime.port)
            .put("caFingerprint", runtime.material.fingerprint)
            .put("addresses", JSArray(runtime.addresses().toTypedArray()))
    }
}
