package app.remotedesktop.client.host

import java.util.Locale
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLServerSocketFactory
import org.json.JSONArray
import org.json.JSONObject

/**
 * Die Endpunkte dieses Handys — dieselben, die der Windows-Agent anbietet.
 *
 * Das ist der ganze Trick von V4: die App merkt nicht, mit wem sie spricht.
 * Sie fragt `/api/info`, koppelt über `/api/pair`, meldet sich über
 * `/api/session` an und bekommt überall dieselben Felder zurück. Was dieses
 * Gerät nicht kann, steht in `capabilities` schlicht nicht drin.
 *
 * Ein zweiter, unverschlüsselter Port trägt genau eine Datei: die eigene CA.
 * Ohne ihn gäbe es ein Henne-Ei-Problem — ein Client kann sie nicht über eine
 * Verbindung holen, der er noch nicht traut.
 */
class HostServer(
    private val identity: HostIdentity,
    private val pairing: PairingService,
    private val codes: PairingCodes,
    private val material: HostCertificate.Material,
    /**
     * Wie dieses Gerät heißt — als Frage und nicht als Wert: der Name ist
     * änderbar, und ein Server, der den von seinem Start behielte, meldete
     * nach einer Umbenennung den alten.
     */
    private val deviceName: () -> String,
    private val version: String,
    private val port: Int = DEFAULT_PORT,
    private val trustPort: Int = DEFAULT_TRUST_PORT,
    private val sessions: SessionStore,
    /**
     * Der Posteingang für Steckbriefe und der Ausweis der eigenen App — die
     * beiden Hälften der Gegenrichtung. Siehe [DeviceProfile].
     */
    private val peers: PeerInbox,
    private val local: LocalClientKey,
    private val screen: () -> Screen,
    private val address: () -> String?,
    /**
     * Woher die Bilder kommen. Ein Lambda und kein Feld, weil die Aufnahme erst
     * beim Verbinden geöffnet wird — und weil der Server damit ohne Android
     * unter Test steht.
     */
    private val screenSource: () -> FrameSource? = { null },
    /**
     * Ob dieses Handy sein Bild überhaupt herausgibt.
     *
     * <p>
     * **Eine Einstellung, keine Aufnahme.** Sie sagt der Gegenseite, dass es
     * möglich ist — mehr nicht. Die Aufnahme selbst verlangt Androids
     * Systemdialog, und der gehört an den Punkt, an dem wirklich jemand zusehen
     * will, und nicht an den, an dem jemand die Einstellung öffnet.
     * </p>
     *
     * <p>
     * Als Frage und nicht als Wert: sie ist während des Laufs änderbar, und ein
     * Server, der den Stand seines Starts behielte, meldete danach den alten.
     * </p>
     */
    private val screenAllowed: () -> Boolean = { true },
    /**
     * Wohin die Eingaben gehen. Gibt eine Meldung zurück, wenn es nicht geht —
     * etwa weil die Bedienungshilfe aus ist. Ein Lambda, damit der Server ohne
     * Android unter Test steht.
     */
    private val input: (InputCommand) -> String? = { NO_INPUT },
    internal val live: LiveConnections = LiveConnections(),
    /**
     * Wer die Verbindung bestätigt. Gibt `false` zurück, wenn niemand
     * zugestimmt hat — Ablehnung ist die Vorgabe. Ein Lambda, damit der Server
     * ohne Android unter Test steht; im Test steht hier `true`, weil sonst jeder
     * Anmeldeweg an einer Frage hängen bliebe, die niemand beantwortet.
     */
    private val confirm: (String) -> Boolean = { true },
    /**
     * Ob die Bedienungshilfe in den Systemeinstellungen eingeschaltet ist.
     * Etwas anderes als [inputReady]: eingeschaltet heißt „sie kommt", gebunden
     * heißt „sie ist da".
     */
    private val inputEnabled: () -> Boolean = { true },
    /** Ob sie gebunden ist und Befehle annimmt. Siehe [awaitInput]. */
    private val inputReady: () -> Boolean = { true },
    /**
     * Beendet die Bildschirmaufnahme samt Zustimmung.
     *
     * <p>
     * **Gerufen, wenn der letzte Zuschauer geht.** Eine Zustimmung, die über
     * das Ende der Verbindung hinaus gilt, klingt bequem und ist es auch — bis
     * sie nicht mehr gilt: Android nimmt eine Projektion nach einer Weile ohne
     * Zuschauer von sich aus zurück, und zwar lautlos. Was dann blieb, war eine
     * Quelle, die es zu geben behauptete und nichts lieferte, und ein Gerät, das
     * beim nächsten Verbinden nicht mehr fragte, weil es sich für berechtigt
     * hielt. Ein Ende, das man selbst herbeiführt, ist verlässlicher als eins,
     * von dem man nichts erfährt.
     * </p>
     */
    private val releaseScreen: () -> Unit = {},
) {

    companion object {
        const val DEFAULT_PORT = 8443
        const val DEFAULT_TRUST_PORT = 8442

        /**
         * Die Sprache, die dieser Host spricht. Gegenstück zu
         * `AgentVersion.Protocol` und `CLIENT_PROTOCOL` in der App — alle drei
         * werden zusammen erhöht, und nur dann, wenn eine Änderung die alte
         * Seite nicht mehr versteht.
         */
        const val PROTOCOL = 1

        /**
         * Was der Client hört, solange niemand die Bedienungshilfe
         * eingeschaltet hat. Ein Gerät, das Berührungen wortlos verschluckt,
         * sieht aus der Ferne aus wie ein hängendes.
         */
        const val NO_INPUT =
            "Dieses Gerät nimmt noch keine Eingaben an. Am Handy unter " +
                "„Dieses Gerät freigeben\" die Fernsteuerung einschalten."

        /** Wenn am Gerät niemand zugestimmt hat. */
        const val NOT_CONFIRMED =
            "Am anderen Gerät hat niemand zugestimmt. Jede Verbindung wird dort " +
                "einzeln bestätigt — die App muss offen sein."

        /** Wenn der vorgelegte Ausweis zu keiner Sitzung gehört. */
        const val NOT_SIGNED_IN = "Nicht angemeldet."

        /**
         * Eingeschaltet, aber Android bindet sie nicht. Etwas anderes als
         * [NO_INPUT] — dort ist etwas zu tun, hier ist etwas kaputt.
         */
        const val INPUT_STALLED =
            "Die Fernsteuerung ist eingeschaltet, Android hat sie aber nicht " +
                "gebunden. In den Bedienungshilfen einmal aus- und wieder einschalten."

        /**
         * Was der Client hört, wenn wirklich niemand die Aufnahme bestätigt
         * hat.
         */
        const val NO_SCREEN =
            "Dieses Gerät gibt seinen Bildschirm noch nicht frei. Am Handy unter " +
                "„Dieses Gerät freigeben\" die Bildschirmaufnahme einschalten."

        /**
         * So lange wird auf die Aufnahme gewartet, bevor der Satz oben
         * hinausgeht.
         *
         * <p>
         * **Der Befund dahinter (18.08.2026):** die Aufnahme entsteht nicht in
         * dem Augenblick, in dem jemand „Zulassen" tippt. Erst kommt Androids
         * eigener Dialog, dann meldet sich der Vordergrunddienst mit dem Typ
         * „nimmt den Bildschirm auf" neu an — und erst danach gibt
         * `getMediaProjection` etwas heraus. Der Client stand längst an der Tür
         * und bekam „gibt seinen Bildschirm noch nicht frei" zu hören, während
         * die Freigabe eine Sekunde später stand. Sichtbar war das als eine
         * Fehlermeldung über ein Gerät, das man daneben tadellos steuern konnte.
         * </p>
         */
        private const val SOURCE_WAIT_MS = 6000L
        private const val SOURCE_POLL_MS = 250L
    }

    /** Der Bildschirm dieses Handys, in echten Pixeln. */
    data class Screen(val width: Int, val height: Int)

    private val secure = HttpServer(port, sslFactory(), ::handle)
    private val plain = HttpServer(trustPort, null, ::handleTrust)

    val isRunning: Boolean get() = secure.isRunning

    /** Die Ports, auf denen wirklich gelauscht wird — siehe `HttpServer.boundPort`. */
    val boundPort: Int get() = secure.boundPort
    val boundTrustPort: Int get() = plain.boundPort

    fun start() {
        secure.start()
        plain.start()
    }

    fun stop() {
        secure.stop()
        plain.stop()
        live.closeAll()
    }

    // ---- Zugangsprüfung ---------------------------------------------------

    /**
     * Blockt alles, was nicht ausdrücklich freigegeben ist — als Sperre für den
     * gesamten Baum und nicht pro Endpunkt. Ein vergessener Eintrag an einem
     * neuen Endpunkt wäre sonst ein offenes Tor.
     */
    internal fun handle(request: HttpServer.Request): HttpServer.Response {
        // Vorabfragen schickt der Browser grundsätzlich ohne Ausweis. Mit 401
        // abgewiesen, käme der eigentliche Aufruf nie zustande.
        if (request.method == "OPTIONS") {
            return HttpServer.Response(204)
        }

        if (HostScopes.LOCAL_ONLY.any { HostScopes.matches(request.path, it) }) {
            return if (request.local) {
                route(request)
            } else {
                HttpServer.Response.error(403, "Dieser Aufruf ist nur am Gerät selbst möglich.")
            }
        }

        if (HostScopes.WITHOUT_CREDENTIAL.any { HostScopes.matches(request.path, it) }) {
            return route(request)
        }

        val credential = credentialOf(request)
            ?: return HttpServer.Response.error(401, "Nicht angemeldet.")

        val required = HostScopes.resolve(request.path)
            ?: return HttpServer.Response.error(
                403,
                "Unbekannter Endpunkt — vermutlich kann dieses Gerät weniger als der Rechner.",
            )

        val session = sessions.find(credential)
            ?: return HttpServer.Response.error(401, "Nicht angemeldet.")

        if (!session.allows(required.scope)) {
            return HttpServer.Response.error(
                403,
                "Dieses Gerät hat kein Recht auf '${required.scope}'.",
            )
        }

        return route(request)
    }

    /**
     * Browser können weder bei WebSockets noch bei `<img>` eigene Kopfzeilen
     * setzen. Deshalb ist das Token dort in der Adresse erlaubt — die
     * Verbindung ist verschlüsselt, und der Host schreibt keine Adressen mit.
     */
    private fun credentialOf(request: HttpServer.Request): String? {
        val header = request.header("authorization").orEmpty()

        if (header.startsWith("Bearer ", ignoreCase = true)) {
            return header.substring(7).trim().ifEmpty { null }
        }

        return request.query["token"]?.ifEmpty { null }
    }

    // ---- Endpunkte --------------------------------------------------------

    private fun route(request: HttpServer.Request): HttpServer.Response = when {
        request.path == "/health" && request.method == "GET" ->
            HttpServer.Response.json(200, JSONObject().put("status", "ok").toString())

        request.path == "/api/info" && request.method == "GET" -> info()

        request.path == "/api/pair/code" && request.method == "POST" -> issueCode()

        request.path == "/api/pair" && request.method == "POST" -> pair(request)

        request.path == "/api/session/challenge" && request.method == "POST" -> challenge(request)

        request.path == "/api/session" && request.method == "POST" -> openSession(request)

        request.path == "/api/clients" && request.method == "GET" -> listClients()

        request.path.startsWith("/api/clients/") && request.method == "DELETE" ->
            revoke(request.path.removePrefix("/api/clients/"))

        request.path == "/api/unpair" && request.method == "DELETE" -> unpair(request)

        request.path == "/ws/screen" -> screenSocket(request)

        request.path == "/ws/input" -> inputSocket(request)

        else -> HttpServer.Response.error(404, "Diesen Endpunkt gibt es hier nicht.")
    }

    /**
     * Sich bei diesem Handy selbst austragen — der eine Weg, auf dem ein
     * Entfernen **beide** Seiten trifft.
     *
     * `/api/clients/{id}` geht dafür nicht: der ist nur am Gerät selbst
     * erreichbar, und das soll er bleiben. Hier trägt sich niemand einen
     * anderen aus, sondern nur sich selbst — wer die Kennung nennt, ist der
     * Sitzungstoken, und der gehört genau einem Gerät.
     *
     * Der Pfad liegt ausdrücklich nicht unter `/api/pair/…`: alles darunter ist
     * ohne Ausweis erreichbar, weil der Kopplungsaufruf die Berechtigung erst
     * erzeugt.
     */
    private fun unpair(request: HttpServer.Request): HttpServer.Response {
        // Die Prüfung ist gelaufen, sonst wäre dieser Aufruf nicht hier. Der
        // Sitzungstoken wird nur noch einmal nachgeschlagen, weil die
        // Weiterleitung keinen Platz für die Sitzung hat.
        val id = sessions.find(credentialOf(request).orEmpty())?.clientId
            ?: return HttpServer.Response.error(
                401,
                "Dieser Zugang gehört keinem gekoppelten Gerät.",
            )

        // Schon weg? Für den Anrufer ist das dasselbe Ergebnis, und ein
        // Fehlschlag hier hielte ihn davon ab, bei sich aufzuräumen.
        if (pairing.revoke(id)) {
            live.close(id)
        }

        return HttpServer.Response.json(200, JSONObject().put("removed", id).toString())
    }

    /**
     * Wartet, bis die Aufnahme steht — höchstens {@link SOURCE_WAIT_MS} lang.
     *
     * Nicht gewartet wird, wenn dieses Gerät sein Bild gar nicht hergibt: dann
     * gibt es nichts, worauf man warten könnte, und der Satz darf sofort
     * hinaus. Siehe {@link SOURCE_WAIT_MS} für den Grund, warum es sonst dauert.
     */
    private fun awaitSource(): FrameSource? {
        if (!screenAllowed()) {
            return null
        }

        val deadline = System.currentTimeMillis() + SOURCE_WAIT_MS

        while (true) {
            screenSource()?.let { return it }

            if (System.currentTimeMillis() >= deadline) {
                return null
            }

            try {
                Thread.sleep(SOURCE_POLL_MS)
            } catch (interrupted: InterruptedException) {
                Thread.currentThread().interrupt()
                return null
            }
        }
    }

    /**
     * Räumt auf, wenn eine Verbindung endet.
     *
     * <p>
     * **Zwei Dinge, und beide hängen am selben Augenblick.** Geht der letzte
     * Zuschauer, endet die Bildschirmaufnahme samt Zustimmung — Android nimmt
     * eine ungenutzte Projektion ohnehin lautlos zurück, und ein Ende, das man
     * selbst herbeiführt, ist verlässlicher. Geht die letzte Verbindung dieses
     * Geräts überhaupt, wird auch die Zustimmung des Menschen vergessen: sie
     * galt dieser Verbindung und nicht dem Sitzungstoken, das zwölf Stunden
     * lebt.
     * </p>
     *
     * <p>
     * Beim Ablösen — ein neuer Socket verdrängt den alten — läuft dieses
     * `finally` erst *nach* der Registrierung des Nachfolgers. Dann ist der
     * Zähler nicht null, und es passiert richtigerweise nichts.
     * </p>
     */
    private fun partOver(request: HttpServer.Request) {
        if (live.countOf(LiveConnections.Kind.SCREEN) == 0) {
            releaseScreen()
        }

        if (live.countFor(clientOf(request)) == 0) {
            sessionOf(request)?.forget()
        }
    }

    /**
     * Führt einen Befehl aus — und gibt der Bedienungshilfe eine zweite Chance.
     *
     * <p>
     * **Der Befund dahinter (19.08.2026):** „Dieses Gerät nimmt noch keine
     * Eingaben an" stand auch dann da, wenn die Fernsteuerung eingeschaltet war
     * und die Steuerung nachweislich lief — besonders nach einem erneuten
     * Verbinden. Android bindet den Dienst zwischendurch neu, und in dieser
     * Lücke antwortet `RemoteInputService.current()` mit nichts. Warten vor dem
     * ersten Befehl (siehe [awaitInput]) deckt das nicht ab: die Lücke kann auch
     * später auftreten.
     * </p>
     *
     * <p>
     * Der Satz behauptet etwas Prüfbares — dass jemand die Fernsteuerung
     * einschalten möge. Ist sie eingeschaltet, ist er falsch, gleich aus welchem
     * technischen Grund. Also wird gewartet und ein zweites Mal versucht, und
     * erst danach steht dort etwas, das dann auch stimmt.
     * </p>
     */
    private fun attempt(command: InputCommand): String? {
        val failure = input(command)

        if (failure != NO_INPUT || !inputEnabled()) {
            return failure
        }

        awaitInput()

        // Immer noch nichts, obwohl eingeschaltet: dann ist es keine Lücke,
        // sondern ein Dienst, den Android nicht bindet. Der Satz sagt das.
        return input(command)?.let { if (it == NO_INPUT) INPUT_STALLED else it }
    }

    /**
     * Wartet, bis die Bedienungshilfe gebunden ist — höchstens
     * {@link SOURCE_WAIT_MS} lang.
     *
     * Nicht gewartet wird, wenn sie gar nicht eingeschaltet ist: dann gibt es
     * nichts, worauf man warten könnte, und der Satz darf beim ersten Befehl
     * hinaus.
     */
    private fun awaitInput() {
        if (!inputEnabled()) {
            return
        }

        val deadline = System.currentTimeMillis() + SOURCE_WAIT_MS

        while (!inputReady() && System.currentTimeMillis() < deadline) {
            try {
                Thread.sleep(SOURCE_POLL_MS)
            } catch (interrupted: InterruptedException) {
                Thread.currentThread().interrupt()
                return
            }
        }
    }

    /**
     * Der Bild-Stream.
     *
     * Die Aufnahme wird hier geöffnet und nicht vorgehalten: sie kostet einen
     * virtuellen Bildschirm und Strom, und beides soll nur laufen, solange
     * jemand zusieht.
     */
    private fun screenSocket(request: HttpServer.Request): HttpServer.Response =
        HttpServer.Response(101) { socket ->
            // Erst die Zustimmung, dann die Aufnahme. Andersherum stünde am
            // Handy ein Systemdialog, bevor jemand überhaupt zugestimmt hat,
            // dass dieses Gerät zusehen darf.
            requireConfirmation(request)?.let { failure ->
                socket.sendText(
                    JSONObject().put("t", "error").put("message", failure).toString(),
                )

                socket.close()
                return@Response
            }

            val source = awaitSource()

            if (source == null) {
                // Kein Fehler im Sinne von kaputt: es hat niemand die Aufnahme
                // bestätigt. Die App zeigt den Satz an, statt ein schwarzes Bild
                // stehen zu lassen.
                socket.sendText(
                    JSONObject()
                        .put("t", "error")
                        .put("message", NO_SCREEN)
                        .toString(),
                )

                socket.close()
                return@Response
            }

            val display = screen()
            val stream = ScreenStream(source, display.width, display.height)

            val release = live.register(clientOf(request), LiveConnections.Kind.SCREEN) {
                socket.close()
            }

            // Zwei Schleifen: das Bild geht in einem eigenen Thread hinaus,
            // während dieser hier auf Steuerbefehle hört. Sie in einer zu
            // führen hieße, dass ein „Pause" erst nach dem nächsten Bild
            // ankommt — und bei einem hängenden Encoder gar nicht.
            val sender = Thread({ stream.run(socket) }, "remotedesktop-screen").apply {
                isDaemon = true
                start()
            }

            try {
                socket.listen(onText = stream::apply)
            } finally {
                socket.close()
                sender.join(2000)
                release()
                partOver(request)
            }
        }

    /**
     * Der Eingabe-Socket.
     *
     * Getrennt vom Bild, wie beim Agent: ein volles Bild im Sendepuffer darf
     * keinen Klick aufhalten. Hier ist das noch wichtiger als dort — ein Bild
     * dieses Handys ist ein ganzes JPEG und kein Ausschnitt.
     */
    private fun inputSocket(request: HttpServer.Request): HttpServer.Response =
        HttpServer.Response(101) { socket ->
            requireConfirmation(request)?.let { failure ->
                socket.sendText(
                    JSONObject().put("t", "error").put("message", failure).toString(),
                )

                socket.close()
                return@Response
            }

            // Auf die Bedienungshilfe warten, bevor der erste Befehl kommt.
            //
            // **Der Befund dahinter (18.08.2026):** „Dieses Gerät nimmt noch
            // keine Eingaben an" stand da, obwohl die Steuerung tadellos lief.
            // Android bindet die Bedienungshilfe erst, wenn es soweit ist —
            // eingeschaltet ist sie längst, aber `RemoteInputService.current()`
            // gibt für ein bis zwei Sekunden noch nichts heraus. Genau in dieses
            // Fenster fiel der erste Befehl der frischen Verbindung, die Meldung
            // ging hinaus und blieb in der Statuszeile stehen — während alles
            // Folgende ankam.
            awaitInput()

            val release = live.register(clientOf(request), LiveConnections.Kind.INPUT) {
                socket.close()
            }

            // Je Verbindung höchstens eine Meldung derselben Art. Ohne das
            // stünde bei jedem Antippen dieselbe Zeile in der Statuszeile, und
            // die eine, auf die es ankommt, ginge darin unter.
            val reported = HashSet<String>()

            try {
                socket.listen(onText = { message ->
                    val command = InputCommands.parse(message) ?: return@listen
                    val failure = attempt(command)

                    if (failure != null && reported.add(failure)) {
                        socket.sendText(
                            JSONObject().put("t", "error").put("message", failure).toString(),
                        )
                    }
                })
            } finally {
                socket.close()
                release()
                partOver(request)
            }
        }

    /**
     * Wem diese Verbindung gehört. Gebraucht für den Widerruf: eine
     * Dauerverbindung wird nach dem Aufbau nie wieder geprüft und überlebte
     * sonst ihre eigene Berechtigung.
     */
    private fun clientOf(request: HttpServer.Request): String? =
        credentialOf(request)?.let { sessions.find(it)?.clientId }

    private fun sessionOf(request: HttpServer.Request): HostSession? =
        credentialOf(request)?.let(sessions::find)

    /**
     * Die Zustimmung des Menschen am Gerät, einmal je Sitzung.
     *
     * @return `null`, wenn zugestimmt wurde; sonst der Satz für die Gegenseite.
     */
    private fun requireConfirmation(request: HttpServer.Request): String? {
        val session = sessionOf(request) ?: return NOT_SIGNED_IN

        val label = pairing.listClients()
            .find { it.id == session.clientId }
            ?.label
            ?: "Ein gekoppeltes Gerät"

        return if (session.confirmOnce { confirm(label) }) null else NOT_CONFIRMED
    }

    /**
     * Was dieses Gerät ist und kann.
     *
     * Der „Monitor" ist der Bildschirm des Handys — einer, immer der erste.
     * Die App baut daraus dieselben Tabs wie beim PC und blendet sie bei einem
     * einzigen Eintrag von allein aus.
     */
    private fun info(): HttpServer.Response {
        val display = screen()

        val monitor = JSONObject()
            .put("index", 0)
            .put("width", display.width)
            .put("height", display.height)
            .put("x", 0)
            .put("y", 0)
            .put("primary", true)
            .put("name", "Display")

        val json = JSONObject()
            .put("hostname", deviceName())
            .put("version", version)
            .put("protocol", PROTOCOL)
            // Was dieses Gerät kann, sagt es selbst — und „Bild" gehört nur
            // dazu, wenn es freigegeben ist. Sonst stünde bei der Gegenseite
            // eine Bildschirmseite bereit, die nie ein Bild bekommt.
            .put("capabilities", JSONArray(HostScopes.capabilities(screenAllowed())))
            // Was dieses Gerät ist. Es entscheidet nur über das Symbol in der
            // Liste — was es kann, steht darüber.
            .put("platform", DeviceProfile.PLATFORM_ANDROID)
            // Ein Handy weckt niemanden und lässt sich nicht wecken: es hört im
            // Schlaf auf kein Magic Packet. Beides steht hier trotzdem, damit
            // die App nicht raten muss.
            .put("canWake", false)
            .put("caFingerprint", material.fingerprint)
            .put("trustPort", boundTrustPort)
            .put("monitors", JSONArray().put(monitor))
            .put(
                "virtualDesktop",
                JSONObject()
                    .put("X", 0).put("Y", 0)
                    .put("Width", display.width).put("Height", display.height),
            )

        return HttpServer.Response.json(200, json.toString())
    }

    private fun issueCode(): HttpServer.Response {
        val code = codes.issue()
        val host = address()

        val json = JSONObject()
            .put("code", code)
            .put("expiresInSeconds", PairingCodes.LIFETIME_MS / 1000)
            .put(
                "pairingUri",
                if (host.isNullOrBlank()) {
                    JSONObject.NULL
                } else {
                    // Der tatsächliche Port, nicht der gewünschte. Sie sind
                    // fast immer gleich — aber wenn 8443 belegt war, steht im
                    // QR-Code sonst eine Adresse, die ins Leere führt.
                    PairingUri.build(host, boundPort, code, material.fingerprint)
                },
            )

        return HttpServer.Response.json(200, json.toString())
    }

    private fun pair(request: HttpServer.Request): HttpServer.Response {
        val body = json(request) ?: return badJson()

        val scopes = body.optJSONArray("scopes")?.let { array ->
            (0 until array.length()).map { array.getString(it) }
        }

        val result = pairing.pair(
            body.optString("code"),
            body.optString("label"),
            body.optString("publicKey"),
            scopes,
        )

        val client = result.client

        if (result.outcome != PairOutcome.OK || client == null) {
            return HttpServer.Response.error(400, describe(result.outcome))
        }

        // Der Steckbrief des Anrufers. Angenommen wird er erst **nach**
        // bestandener Kopplung — vorher wäre es ein Weg, jedem Gerät ein Gerät
        // in die Liste zu schreiben, indem man Codes rät.
        //
        // Nur der Steckbrief wandert in den Eingang. Den Schlüssel der
        // Gegenseite hat dieser Host schon: es ist derselbe, mit dem sie sich
        // gerade gekoppelt hat.
        DeviceProfile.sanitize(body.optJSONObject("self"))?.let(peers::add)

        val json = JSONObject()
            .put("clientId", client.id)
            .put("scopes", JSONArray(client.scopes))
            .put("hostname", deviceName())
            .put("agentPublicKey", identity.publicKey)
            .put("agentFingerprint", identity.fingerprint)
            // Was dieses Gerät ist — für das Symbol in der Geräteliste der
            // Gegenseite, auch wenn dieses Handy gerade aus ist.
            .put("platform", DeviceProfile.PLATFORM_ANDROID)
            .put("caFingerprint", material.fingerprint)
            // Dasselbe zurück: der Ausweis der App dieses Handys. Damit trägt
            // die Gegenseite die andere Richtung bei sich ein, ohne noch einmal
            // ins Netz zu gehen.
            .put(
                "peer",
                JSONObject()
                    .put("name", deviceName())
                    .put("clientKey", local.publicKey),
            )

        return HttpServer.Response.json(200, json.toString())
    }

    private fun challenge(request: HttpServer.Request): HttpServer.Response {
        val body = json(request) ?: return badJson()
        val nonce = pairing.challenge(body.optString("clientId"))

        // Auch ein unbekannter Client bekommt 401 und nicht 404: dass eine
        // Kennung existiert, ist selbst schon eine Auskunft.
        return if (nonce == null) {
            HttpServer.Response.error(401, "Nicht gekoppelt.")
        } else {
            HttpServer.Response.json(
                200,
                JSONObject()
                    .put("nonce", nonce)
                    .put("expiresInSeconds", ChallengeStore.LIFETIME_MS / 1000)
                    .toString(),
            )
        }
    }

    private fun openSession(request: HttpServer.Request): HttpServer.Response {
        val body = json(request) ?: return badJson()

        val result = pairing.openSession(
            body.optString("clientId"),
            body.optString("nonce"),
            body.optString("signature"),
        )

        // Alle Fehlschläge sehen gleich aus. Wer probiert, soll nicht erfahren,
        // ob die Kennung stimmte und nur die Unterschrift nicht passte.
        if (result.outcome != SessionOutcome.OK || result.token == null) {
            return HttpServer.Response.error(401, "Anmeldung fehlgeschlagen.")
        }

        // **Hier wird nicht mehr gefragt.** Eine Anmeldung sieht nichts und
        // steuert nichts — sie ist auch der Weg, auf dem die Gegenseite die
        // Fassung dieses Geräts abliest. Die Rückfrage stand damit bei jedem
        // Start der App drüben auf dem Bildschirm, ohne dass jemand etwas
        // vorhatte. Gefragt wird beim ersten Bild- oder Eingabe-Socket dieser
        // Sitzung, siehe [HostSession.confirmOnce].
        val json = JSONObject()
            .put("token", result.token)
            .put("scopes", JSONArray(result.client?.scopes.orEmpty()))
            .put("expiresInSeconds", SessionStore.LIFETIME_MS / 1000)

        return HttpServer.Response.json(200, json.toString())
    }

    private fun listClients(): HttpServer.Response {
        val array = JSONArray()

        pairing.listClients().forEach { client ->
            array.put(
                JSONObject()
                    .put("id", client.id)
                    .put("label", client.label)
                    .put("scopes", JSONArray(client.scopes))
                    .put("createdAt", client.createdAt)
                    .put("lastSeenAt", client.lastSeenAt),
            )
        }

        return HttpServer.Response.json(200, JSONObject().put("clients", array).toString())
    }

    /**
     * Widerrufen heißt: ab jetzt **und** rückwirkend auf alles, was schon
     * steht. Der Eintrag allein zu löschen genügt nicht — Bild und Eingabe
     * laufen über Dauerverbindungen, und keine davon wird nach dem Aufbau noch
     * einmal geprüft.
     */
    private fun revoke(id: String): HttpServer.Response {
        if (!pairing.revoke(id)) {
            return HttpServer.Response.error(404, "Unbekannter Client.")
        }

        val closed = live.close(id)

        return HttpServer.Response.json(
            200,
            JSONObject().put("revoked", id).put("closed", closed).toString(),
        )
    }

    // ---- Der unverschlüsselte Port ---------------------------------------

    private fun handleTrust(request: HttpServer.Request): HttpServer.Response =
        if (request.path == "/ca.crt" && request.method == "GET") {
            HttpServer.Response(
                200,
                contentType = "application/x-x509-ca-cert",
                body = material.authorityDer,
                headers = mapOf("X-Certificate-Fingerprint" to material.fingerprint),
            )
        } else {
            // Dieser Port darf unter keinen Umständen dieselben Endpunkte
            // bedienen wie der verschlüsselte. Deshalb steht hier eine eigene
            // Weiche und keine Route in der großen Liste, wo sie jemand
            // übersehen könnte.
            HttpServer.Response(404)
        }

    // ---- Kleinkram --------------------------------------------------------

    private fun json(request: HttpServer.Request): JSONObject? =
        runCatching { JSONObject(request.text()) }.getOrNull()

    private fun badJson(): HttpServer.Response =
        HttpServer.Response.error(400, "Der Rumpf war kein gültiges JSON.")

    private fun describe(outcome: PairOutcome): String = when (outcome) {
        PairOutcome.BAD_CODE -> "Code falsch oder abgelaufen."
        PairOutcome.BAD_LABEL -> "Der Name des Geräts fehlt oder ist zu lang."
        PairOutcome.BAD_PUBLIC_KEY -> "Der öffentliche Schlüssel ist kein ECDSA-P-256-Schlüssel."
        PairOutcome.BAD_SCOPE -> "Unbekanntes Recht angefordert."
        PairOutcome.OK -> "Kopplung fehlgeschlagen."
    }

    private fun sslFactory(): SSLServerSocketFactory {
        val managers = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
            .apply { init(material.keyStore, material.password) }

        return SSLContext.getInstance("TLS")
            .apply { init(managers.keyManagers, null, null) }
            .serverSocketFactory
    }
}

/**
 * Was im QR-Code der Kopplung steht. Leseseite ist `app/src/lib/pairingUri.ts`,
 * Schwesterfassung `agent/Auth/PairingUri.cs` — dasselbe Format, damit derselbe
 * Scanner beides versteht.
 */
object PairingUri {

    fun build(host: String, port: Int, code: String, caFingerprint: String?): String {
        val trimmed = host.trim()

        require(trimmed.isNotEmpty()) { "Ohne Adresse ergibt der QR-Code keinen Sinn." }
        require(port in 1..65535) { "Der Port liegt außerhalb des möglichen Bereichs." }
        require(Regex("^\\d{6}$").matches(code)) { "Der Kopplungscode besteht aus sechs Ziffern." }

        val name = java.net.URLEncoder.encode(trimmed.lowercase(Locale.ROOT), "UTF-8")
        val uri = "remotedesktop://pair?host=$name&port=$port&code=$code"

        return if (caFingerprint.isNullOrBlank()) {
            uri
        } else {
            val fingerprint = java.net.URLEncoder.encode(
                caFingerprint.trim().lowercase(Locale.ROOT), "UTF-8",
            )

            "$uri&ca=$fingerprint"
        }
    }
}

/**
 * Wer gerade eine Dauerverbindung offen hält.
 *
 * Ohne diese Liste überlebte eine stehende Verbindung ihren eigenen Widerruf:
 * geprüft wird beim Aufbau, und danach nie wieder. Ab Phase 30 melden sich hier
 * der Eingabe-Socket und der Videostrom an — bis dahin ist sie leer und tut
 * nichts, steht aber schon da, wo sie hingehört.
 */
class LiveConnections {

    /** Wofür eine Verbindung da ist. Bild und Eingabe laufen getrennt. */
    enum class Kind { SCREEN, INPUT }

    private class Entry(val kind: Kind, val cut: () -> Unit)

    private val gate = Any()
    private val open = HashMap<String, MutableList<Entry>>()

    /**
     * Wer erfahren will, dass sich die Zahl der offenen Verbindungen geändert
     * hat.
     *
     * <p>
     * Gebraucht für die Benachrichtigung des Vordergrunddienstes: sie stand
     * dort mit der eigenen Adresse, unabhängig davon, ob überhaupt jemand
     * verbunden war. Eine Adresse ist keine Nachricht — sie ändert sich nicht,
     * und man kann nichts mit ihr tun. Ob gerade jemand zusieht, schon.
     * </p>
     */
    @Volatile
    var onChange: ((Int) -> Unit)? = null

    /** Wie viele Sockets gerade offen sind — über alle Clients. */
    val count: Int get() = synchronized(gate) { open.values.sumOf { it.size } }

    /** Wie viele davon dieser Art sind — über alle Clients. */
    fun countOf(kind: Kind): Int = synchronized(gate) {
        open.values.sumOf { list -> list.count { it.kind == kind } }
    }

    /** Wie viele Verbindungen dieses eine Gerät offen hat — Bild und Eingabe. */
    fun countFor(clientId: String?): Int = synchronized(gate) {
        if (clientId == null) 0 else open[clientId]?.size ?: 0
    }

    /**
     * Meldet eine Verbindung an — und trennt dabei die vorige derselben Art
     * desselben Geräts.
     *
     * <p>
     * **Der Befund dahinter (18.08.2026):** wer sich verband, trennte und es
     * gleich noch einmal versuchte, kam nicht mehr durch — am Bildschirm blieb
     * es dunkel, während die Benachrichtigung am Handy behauptete, jemand sehe
     * zu. Der alte Socket war nämlich nur *scheinbar* weg: sein Thread hing
     * ohne Zeitlimit in einem `read()`, das niemand unterbrach. Die eine Hälfte
     * davon ist repariert, wo sie entstand (`HttpServer.openSocket` schließt
     * jetzt den TCP-Socket mit). Die andere Hälfte ist diese hier: ein Gerät hat
     * genau ein Bild und genau eine Eingabe. Ein zweiter Socket derselben Art
     * ist kein Nebeneinander, sondern eine Ablösung — und der frische gewinnt,
     * weil er der ist, an dem gerade jemand sitzt.
     * </p>
     */
    fun register(clientId: String?, kind: Kind, cut: () -> Unit): () -> Unit {
        if (clientId == null) {
            return {}
        }

        val entry = Entry(kind, cut)

        // Erst herausnehmen, dann trennen: der Rückruf schließt einen Socket,
        // und dessen Thread meldet sich gleich hier zurück, um sich abzumelden.
        // Innerhalb des Schlosses wäre das ein Selbstgespräch mit Nachschlüssel.
        val abgelöst = synchronized(gate) {
            val list = open.getOrPut(clientId) { mutableListOf() }
            val alt = list.filter { it.kind == kind }

            list.removeAll(alt)
            list.add(entry)

            alt
        }

        abgelöst.forEach { runCatching(it.cut) }

        announce()

        return {
            synchronized(gate) {
                open[clientId]?.remove(entry)
            }

            announce()
        }
    }

    /** Außerhalb des Schlosses: der Zuhörer darf hier alles tun, auch fragen. */
    private fun announce() {
        onChange?.invoke(count)
    }

    /** @return Wie viele Verbindungen dabei getrennt wurden. */
    fun close(clientId: String): Int {
        val cuts = synchronized(gate) { open.remove(clientId).orEmpty() }

        cuts.forEach { runCatching(it.cut) }
        announce()

        return cuts.size
    }

    fun closeAll() = synchronized(gate) {
        open.keys.toList().forEach { close(it) }
    }
}
