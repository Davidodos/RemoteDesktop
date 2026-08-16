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
    private val folder: File,
    private val identity: HostIdentity,
    private val sessions: SessionStore,
    private val codes: PairingCodes,
    private val pairing: PairingService,
    private val peers: PeerInbox,
    private val local: LocalClientKey,
    private val version: String,
    /** Die offenen Rückfragen „darf dieses Gerät jetzt verbinden?". */
    val connections: ConnectionRequests,
) {

    /**
     * Wie dieses Handy heißt — bei jedem Zugriff frisch gelesen. Wer sich in
     * den Einstellungen umbenennt, soll dafür weder den Host stoppen noch die
     * App neu starten müssen.
     */
    val deviceName: String get() = deviceNameOf(context)


    private val agentFingerprint: String = identity.fingerprint

    /**
     * Zertifikat und Server entstehen zusammen und werden zusammen ersetzt.
     *
     * <p>
     * **Der Befund dahinter:** beide entstanden genau einmal, beim ersten Zugriff
     * auf die Runtime. Damit stand im Zertifikat die Adressliste von damals,
     * während die Freigabeseite die von jetzt anzeigte. Ein Handy wechselt sein
     * Netz mehrmals am Tag — und wer die angezeigte Adresse abtippte, landete auf
     * einem Zertifikat, das genau diese Adresse nicht abdeckt. Die Verbindung
     * scheiterte im Handschlag, also lange bevor irgendjemand nach einem Ausweis
     * gefragt hätte: am Bildschirm stand „antwortet nicht", obwohl das Handy
     * lauschte und antwortete.
     * </p>
     *
     * <p>
     * Die Stelle selbst überlebt das Neuausstellen — nur das Serverzertifikat
     * wird ersetzt. Deshalb ist es folgenlos: kein gekoppeltes Gerät muss etwas
     * erneut bestätigen, und der Fingerabdruck, der im QR-Code steht, bleibt
     * derselbe.
     * </p>
     */
    private var current: Endpoint = endpoint()

    val material: HostCertificate.Material get() = current.material

    private val server: HostServer get() = current.server

    /** Was zusammengehört: ein Zertifikat und der Server, der es vorzeigt. */
    private data class Endpoint(
        val material: HostCertificate.Material,
        val server: HostServer,
    )

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

            val clients = ClientStore(File(folder, "clients.json"))
            val codes = PairingCodes()
            val sessions = SessionStore()

            // Die beiden Hälften der Gegenrichtung: der Eingang für fremde
            // Steckbriefe, der Ausweis für den eigenen.
            return HostRuntime(
                context = context,
                folder = folder,
                identity = HostIdentity.loadOrCreate(File(folder, "hostkey.txt")),
                sessions = sessions,
                codes = codes,
                pairing = PairingService(clients, codes, ChallengeStore(), sessions),
                peers = PeerInbox(File(folder, "peers.json")),
                local = LocalClientKey(File(folder, "clientkey.txt")),
                version = versionOf(context),
                // Jede Verbindung wird am Gerät einzeln bestätigt.
                connections = ConnectionRequests(),
            )
        }

        /**
         * Wie das Handy heißt.
         *
         * Zuerst der Name, der beim Erststart in dieser App vergeben wurde — er
         * steht in fremden Gerätelisten, und dort ist „Handy" brauchbarer als
         * „Pixel 8". Danach der Gerätename aus den Android-Einstellungen, zuletzt
         * das Modell.
         */
        fun deviceNameOf(context: Context): String {
            HostPreference.deviceName(context)?.let { return it }

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

    /**
     * Baut Zertifikat und Server für die Adressen, die dieses Gerät **jetzt**
     * hat.
     *
     * Ins Zertifikat kommt nur, worüber jemand hereinkommt: eine
     * Mobilfunkadresse stünde dort als Versprechen, das der Anbieter nicht
     * einlöst. `localhost` gehört dazu, weil die eigene Oberfläche über die
     * Rückkopplung fragt.
     */
    private fun endpoint(): Endpoint {
        val names = (HostAddresses.routable(context) + "localhost").distinct()

        // Stellt nur dann neu aus, wenn die Liste sich geändert hat oder das
        // alte Zertifikat abläuft — die Stelle darüber bleibt in jedem Fall.
        val material = HostCertificate.loadOrCreate(folder, deviceName, names)

        val server = HostServer(
            identity = identity,
            pairing = pairing,
            codes = codes,
            material = material,
            deviceName = { deviceName },
            version = version,
            sessions = sessions,
            peers = peers,
            local = local,
            screen = { ScreenCapture.scaled(screenOf(context)) },
            address = { HostAddresses.best(context) },
            screenSource = { ScreenCapture.open(context, screenOf(context)) },
            input = { command ->
                RemoteInputService.current()?.execute(command) ?: HostServer.NO_INPUT
            },
            confirm = connections::ask,
        )

        return Endpoint(material, server)
    }

    /**
     * Startet den Host — und stellt vorher sicher, dass sein Zertifikat die
     * Adressen abdeckt, unter denen er gleich angezeigt wird.
     *
     * Nachgesehen wird bei jedem Einschalten und nicht nur beim ersten: das
     * Einschalten ist der Augenblick, in dem jemand hinsieht, und zwischen zwei
     * Einschaltvorgängen liegt oft ein Netzwechsel. Deckt das vorhandene
     * Zertifikat die Liste schon ab, kostet der Aufruf nichts.
     */
    fun start() {
        if (server.isRunning) {
            return
        }

        // Der alte Server lauscht nicht mehr; ihn zu ersetzen kostet nichts als
        // den Blick auf die Adressliste. Die Stelle bleibt dabei dieselbe —
        // erneuert wird höchstens das Serverzertifikat.
        current = endpoint()

        server.start()
    }

    fun stop() {
        server.stop()

        // Ein offener Kopplungscode gehört zum laufenden Host. Bleibt er beim
        // Abschalten stehen, koppelt sich später jemand mit einem Code, den
        // niemand mehr auf dem Bildschirm sieht.
        codes.clear()
    }

    fun issueCode(): PairingCodes = codes

    /**
     * Der eigene Steckbrief — er geht mit, wenn diese App ein anderes Gerät
     * koppelt.
     *
     * **Er hängt ausdrücklich nicht daran, ob der Host gerade läuft.** Genau das
     * war der Fehler des Vorgängers: wer beim Koppeln die Freigabe noch nicht
     * eingeschaltet hatte, bekam die Gegenrichtung nie — auch später nicht, ohne
     * neu zu koppeln. Ein Steckbrief beschreibt, wie dieses Gerät erreichbar
     * *wäre*; ein Eintrag in einer Datei wirkt, sobald der Server startet.
     *
     * `null` nur ohne Adresse im Netz: dann beschreibt er nichts.
     */
    fun profile(): DeviceProfile? {
        // Nur eine Adresse, unter der jemand hereinkommt. Die Mobilfunkadresse
        // stand vorher mit in der Auswahl und gewann sie sogar, sobald das WLAN
        // gerade kein Internet hatte — die Gegenseite trug sie ein und lief von
        // da an in eine Zeitüberschreitung.
        val address = HostAddresses.routable(context).firstOrNull() ?: return null

        return DeviceProfile(
            host = address,
            port = port,
            name = deviceName,
            caFingerprint = material.fingerprint,
            agentFingerprint = agentFingerprint,
            clientKey = local.publicKey,
            platform = DeviceProfile.PLATFORM_ANDROID,
        )
    }

    /** Die Steckbriefe, die beim Koppeln hier abgegeben wurden. Ohne Nebenwirkung. */
    fun listPeers(): List<DeviceProfile> = peers.list()

    /** Vergisst, was die App eingetragen hat. */
    fun forgetPeers(ids: Collection<String>) = peers.forget(ids)

    /** Die Gegenrichtung eintragen: diese Oberfläche darf dieses Handy steuern. */
    fun grant(publicKey: String, label: String): Boolean = pairing.grant(publicKey, label)

    /**
     * Der Ausweis dieses Handys als Client — beide Hälften.
     *
     * Die App holt ihn sich hier ab, statt selbst einen zu erzeugen und ihn zu
     * hinterlegen: so kennt ihn der Host auch dann, wenn die Oberfläche noch
     * nie an der Reihe war. Der öffentliche Teil geht bei jeder Kopplung mit.
     */
    fun localClientKey(): Pair<String, String> = local.publicKey to local.privateKey

    fun clients(): List<PairedClient> = pairing.listClients()

    fun revoke(id: String): Boolean = pairing.revoke(id)

    /** Der QR-Inhalt zum angezeigten Code, oder `null` ohne erreichbare Adresse. */
    fun pairingUri(code: String): String? {
        // Dieselbe Wahl wie beim Steckbrief: was im QR-Code steht, muss ein Ziel
        // sein. Eine Adresse, die nur nach außen taugt, führt hier ins Leere —
        // und der Fehlschlag fällt erst auf, wenn schon jemand gescannt hat.
        val address = HostAddresses.routable(context).firstOrNull() ?: return null

        return PairingUri.build(address, port, code, material.fingerprint)
    }
}
