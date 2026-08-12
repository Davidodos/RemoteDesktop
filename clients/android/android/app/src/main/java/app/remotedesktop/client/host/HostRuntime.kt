package app.remotedesktop.client.host

import android.content.Context
import android.graphics.Point
import android.os.Build
import android.provider.Settings
import android.view.WindowManager
import java.io.File

/**
 * Alles, was dieses Handy zum Host macht, an einer Stelle zusammengesetzt.
 *
 * Getrennt vom Dienst, damit der Dienst nur noch startet und stoppt — und
 * getrennt vom Server, damit der ohne Android auskommt und unter Test steht.
 * Hier ist der einzige Ort, an dem beides aufeinandertrifft.
 */
class HostRuntime private constructor(
    private val context: Context,
    private val server: HostServer,
    private val codes: PairingCodes,
    private val pairing: PairingService,
    val material: HostCertificate.Material,
    val deviceName: String,
) {

    companion object {

        /** Alles, was zum Host gehört, liegt in einem Ordner. */
        private const val FOLDER = "host"

        @Volatile
        private var instance: HostRuntime? = null

        /**
         * Der Host dieses Prozesses. Es gibt genau einen — zwei Server auf
         * demselben Port wären ein Streit, den keiner gewinnt.
         */
        fun of(context: Context): HostRuntime = instance ?: synchronized(this) {
            instance ?: create(context.applicationContext).also { instance = it }
        }

        private fun create(context: Context): HostRuntime {
            val folder = File(context.filesDir, FOLDER).apply { mkdirs() }
            val name = deviceNameOf(context)

            // Die eigenen Adressen stehen im Zertifikat. Wechselt die Adresse —
            // und bei DHCP tut sie das —, wird beim nächsten Start ein neues
            // Serverzertifikat ausgestellt; die Stelle darüber bleibt.
            val material = HostCertificate.loadOrCreate(
                folder,
                name,
                (HostAddresses.all(context) + "localhost").distinct(),
            )

            val clients = ClientStore(File(folder, "clients.json"))
            val codes = PairingCodes()
            val sessions = SessionStore()
            val pairing = PairingService(clients, codes, ChallengeStore(), sessions)

            val server = HostServer(
                identity = HostIdentity.loadOrCreate(File(folder, "hostkey.txt")),
                pairing = pairing,
                codes = codes,
                material = material,
                deviceName = name,
                version = versionOf(context),
                sessions = sessions,
                screen = { ScreenCapture.scaled(screenOf(context)) },
                address = { HostAddresses.best(context) },
                screenSource = { ScreenCapture.open(context, screenOf(context)) },
                input = { command ->
                    RemoteInputService.current()?.execute(command) ?: HostServer.NO_INPUT
                },
            )

            return HostRuntime(context, server, codes, pairing, material, name)
        }

        /**
         * Wie das Handy heißt.
         *
         * Zuerst der Name, den der Nutzer in den Einstellungen vergeben hat —
         * „Davids Pixel" sagt mehr als „Pixel 8". Gibt es ihn nicht, bleibt das
         * Modell.
         */
        private fun deviceNameOf(context: Context): String {
            val chosen = runCatching {
                Settings.Global.getString(context.contentResolver, "device_name")
            }.getOrNull()

            return chosen?.takeIf { it.isNotBlank() } ?: Build.MODEL ?: "Android"
        }

        private fun versionOf(context: Context): String = runCatching {
            context.packageManager.getPackageInfo(context.packageName, 0).versionName
        }.getOrNull() ?: "0.0.0"

        /**
         * Der Bildschirm in echten Pixeln.
         *
         * Nicht in den skalierten: die App rechnet Klicks als Anteil der Breite
         * und Höhe um, und wer hier die falsche Größe meldet, klickt
         * systematisch daneben — derselbe Fehler, den `SetProcessDPIAware` auf
         * Windows verhindert.
         */
        private fun screenOf(context: Context): HostServer.Screen {
            val manager = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val bounds = manager.currentWindowMetrics.bounds

                return HostServer.Screen(bounds.width(), bounds.height())
            }

            @Suppress("DEPRECATION")
            val size = Point().also { manager.defaultDisplay.getRealSize(it) }

            return HostServer.Screen(size.x, size.y)
        }
    }

    val isRunning: Boolean get() = server.isRunning

    val port: Int get() = if (server.isRunning) server.boundPort else HostServer.DEFAULT_PORT

    /** Ob jemand die Bildschirmaufnahme bestätigt hat. */
    val isSharingScreen: Boolean get() = ScreenCapture.isPermitted

    /** Ob die Bedienungshilfe läuft — ohne sie kommt keine Eingabe an. */
    fun isAcceptingInput(context: Context): Boolean = RemoteInputService.isEnabled(context)

    /** Unter welchen Adressen dieses Handy gerade erreichbar ist. */
    fun addresses(): List<String> = HostAddresses.all(context)

    fun start() {
        if (!server.isRunning) {
            server.start()
        }
    }

    fun stop() {
        server.stop()

        // Ein offener Kopplungscode gehört zum laufenden Host. Bleibt er beim
        // Abschalten stehen, koppelt sich später jemand mit einem Code, den
        // niemand mehr auf dem Bildschirm sieht.
        codes.clear()
    }

    fun issueCode(): PairingCodes = codes

    /** Was die Gegenseite an Gegenkopplung hinterlassen hat — einmalig. */
    fun takePending(): BackPairing? = server.takePending()

    /**
     * Das eigene Angebot: Adresse, Port, ein frischer Code und der eigene
     * Fingerabdruck. Der geht damit über eine Verbindung, die bereits
     * beglaubigt ist — niemand muss ihn mehr ablesen und vergleichen.
     *
     * `null`, solange der Host nicht läuft: ein Angebot ohne Server wäre eine
     * Einladung in ein leeres Zimmer.
     */
    fun backOffer(): BackPairing? {
        if (!isRunning) {
            return null
        }

        val address = addresses().firstOrNull() ?: return null

        return BackPairing(address, port, codes.issue(), material.fingerprint, deviceName)
    }

    fun clients(): List<PairedClient> = pairing.listClients()

    fun revoke(id: String): Boolean = pairing.revoke(id)

    /** Der QR-Inhalt zum angezeigten Code, oder `null` ohne erreichbare Adresse. */
    fun pairingUri(code: String): String? {
        val address = addresses().firstOrNull() ?: return null

        return PairingUri.build(address, port, code, material.fingerprint)
    }
}
