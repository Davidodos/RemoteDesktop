package app.remotedesktop.client.host

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.provider.Settings
import androidx.activity.result.ActivityResult
import com.getcapacitor.JSArray
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.ActivityCallback
import com.getcapacitor.annotation.CapacitorPlugin
import org.json.JSONObject

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

    /**
     * Die Rückfrage „darf dieses Gerät jetzt verbinden?" an die Oberfläche
     * hängen.
     *
     * Ein Ereignis und kein Rückgabewert: die Frage entsteht im Thread einer
     * eingehenden Verbindung, und die Antwort kommt Sekunden später von einem
     * Menschen. Meldet sich hier niemand an, gilt jede Anfrage als abgelehnt —
     * siehe [ConnectionRequests].
     */
    override fun load() {
        val requests = HostRuntime.of(context).connections

        requests.listener = { id, label ->
            notifyListeners(
                "connectionRequest",
                JSObject().put("id", id).put("label", label),
                true,
            )
        }

        requests.onSettled = { id ->
            notifyListeners("connectionSettled", JSObject().put("id", id), true)
        }
    }

    /** Die Antwort vom Bildschirm. Ohne sie läuft die Frage in ihr Zeitlimit. */
    @PluginMethod
    fun answerConnection(call: PluginCall) {
        val id = call.getString("id")

        if (id.isNullOrBlank()) {
            call.reject("Ohne Kennung gehört diese Antwort zu keiner Frage.")
            return
        }

        HostRuntime.of(context).connections.answer(id, call.getBoolean("allow", false) == true)

        call.resolve()
    }

    @PluginMethod
    fun status(call: PluginCall) {
        call.resolve(describe())
    }

    /**
     * Die Einstellung „dieses Gerät darf ferngesteuert werden" einschalten.
     *
     * Sie wird gemerkt und gilt ab jetzt für jeden Start der App: der Host läuft,
     * solange die App offen ist, und endet mit ihr. Vorher war er auf
     * Dauerbetrieb ausgelegt — einmal eingeschaltet, lief er weiter, auch wenn
     * die App längst weggewischt war.
     */
    @PluginMethod
    fun start(call: PluginCall) {
        HostPreference.set(context, true)
        HostService.start(context)

        // Der Dienst startet den Server; hier wird nur angestoßen. Die Antwort
        // beschreibt deshalb den Stand von gleich, nicht den von jetzt — die
        // Oberfläche fragt nach dem Umschalten ohnehin noch einmal nach.
        call.resolve(describe(running = true))
    }

    @PluginMethod
    fun stop(call: PluginCall) {
        HostPreference.set(context, false)
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

    /**
     * Fragt die Bildschirmaufnahme an.
     *
     * Der Systemdialog kommt von Android und lässt sich nicht umgehen. Er
     * kommt auch nicht einmalig: nach einem Neustart des Geräts ist die
     * Erlaubnis weg. Das steht auf der Freigabeseite, damit niemand sein Handy
     * in dem Glauben weglegt, es bleibe einsehbar.
     */
    @PluginMethod
    fun enableScreen(call: PluginCall) {
        val manager = context.getSystemService(Context.MEDIA_PROJECTION_SERVICE)
            as MediaProjectionManager

        startActivityForResult(call, manager.createScreenCaptureIntent(), "screenResult")
    }

    @PluginMethod
    fun disableScreen(call: PluginCall) {
        ScreenCapture.forget()

        // Der Dienst meldet sich neu an, damit der Typ „nimmt den Bildschirm
        // auf" wieder verschwindet — sonst behauptet die Benachrichtigung
        // etwas, das nicht mehr stimmt.
        HostService.start(context)

        call.resolve(describe())
    }

    @ActivityCallback
    private fun screenResult(call: PluginCall?, result: ActivityResult) {
        if (call == null) {
            return
        }

        val data = result.data

        if (result.resultCode != Activity.RESULT_OK || data == null) {
            call.reject("Die Bildschirmaufnahme wurde nicht bestätigt.")
            return
        }

        ScreenCapture.remember(result.resultCode, data)

        // Erst der Dienst mit dem passenden Typ, dann darf jemand aufnehmen.
        // Andersherum zieht Android seit Fassung 14 die Erlaubnis wortlos
        // zurück, und die App stünde vor einem schwarzen Bild ohne Erklärung.
        HostService.start(context)

        call.resolve(describe())
    }

    /**
     * Führt in die Systemeinstellungen, wo die Bedienungshilfe eingeschaltet
     * wird.
     *
     * Mehr kann eine App hier nicht tun: einschalten muss es ein Mensch. Der
     * Weg dorthin führt über eine Liste, in der „RemoteDesktop-Fernsteuerung"
     * steht — deshalb sagt die Freigabeseite den Namen dazu.
     */
    @PluginMethod
    fun openInputSettings(call: PluginCall) {
        val intent = Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

        context.startActivity(intent)

        call.resolve()
    }

    /**
     * Der eigene Steckbrief — er geht mit, wenn diese App ein anderes Gerät
     * koppelt. Er hängt nicht daran, ob der Host gerade läuft: was er
     * beschreibt, gilt, sobald er startet.
     */
    @PluginMethod
    fun profile(call: PluginCall) {
        val profile = HostRuntime.of(context).profile()

        call.resolve(
            if (profile == null) {
                JSObject().put("profile", JSONObject.NULL)
            } else {
                JSObject().put("profile", JSObject.fromJSONObject(profile.toJson()))
            },
        )
    }

    /**
     * Die Steckbriefe, die beim Koppeln hier abgegeben wurden.
     *
     * Lesen leert den Eingang **nicht**: geht danach etwas schief, wäre der
     * Steckbrief sonst endgültig weg. Vergessen wird auf Zuruf, siehe
     * [forgetPeers].
     */
    @PluginMethod
    fun peers(call: PluginCall) {
        val array = JSArray()

        HostRuntime.of(context).listPeers().forEach {
            array.put(it.toJson().put("id", PeerInbox.key(it)))
        }

        call.resolve(JSObject().put("peers", array))
    }

    /** Was in der Liste steht, braucht hier nicht mehr zu liegen. */
    @PluginMethod
    fun forgetPeers(call: PluginCall) {
        val ids = call.getArray("ids")
        val wanted = ArrayList<String>()

        if (ids != null) {
            for (index in 0 until ids.length()) {
                ids.optString(index)?.takeIf { it.isNotBlank() }?.let(wanted::add)
            }
        }

        HostRuntime.of(context).forgetPeers(wanted)

        call.resolve()
    }

    /**
     * Die Gegenrichtung eintragen: die Oberfläche der Gegenseite darf dieses
     * Handy steuern. Ohne Code — der Schlüssel kam über eine Verbindung, an
     * deren Anfang genau ein Code stand.
     */
    @PluginMethod
    fun grant(call: PluginCall) {
        val key = call.getString("publicKey")

        if (key.isNullOrBlank() ||
            !HostRuntime.of(context).grant(key, call.getString("label").orEmpty())
        ) {
            call.reject("Der öffentliche Schlüssel ist kein ECDSA-P-256-Schlüssel.")
            return
        }

        call.resolve()
    }

    /**
     * Den Ausweis dieser App hinterlegen. Ohne ihn bliebe jede Kopplung
     * einseitig: die Gegenseite bekäme in der Antwort nichts, was sie in ihre
     * eigene Liste eintragen könnte.
     */
    @PluginMethod
    fun registerLocalClient(call: PluginCall) {
        if (!HostRuntime.of(context).rememberLocalClient(call.getString("publicKey"))) {
            call.reject("Der öffentliche Schlüssel ist kein ECDSA-P-256-Schlüssel.")
            return
        }

        call.resolve()
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
            .put("sharingScreen", runtime.isSharingScreen)
            .put("acceptingInput", runtime.isAcceptingInput(context))
            .put("addresses", JSArray(runtime.addresses().toTypedArray()))
    }
}
