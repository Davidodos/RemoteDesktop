package app.remotedesktop.client.host

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
     * Beendet die Bildschirmaufnahme samt Aufnahme-Erlaubnis.
     *
     * <p>
     * **Gerufen, wenn die letzte Verbindung überhaupt endet** — nicht, wenn
     * nur der Bild-Socket zu ist. Bis zum 18.09.2026 hing das am Bild-Socket
     * allein: baute der Client ihn neu auf, während sein Eingabe-Socket stand,
     * war die Erlaubnis weg, die Zustimmung der Sitzung aber noch da. Niemand
     * fragte neu, und der nächste Bild-Socket lief in „gibt seinen Bildschirm
     * noch nicht frei". Das war „Bild weg, Eingaben gehen noch".
     * </p>
     */
    private val releaseScreen: () -> Unit = {},
    /** Ob die Aufnahme-Erlaubnis gerade vorliegt. Siehe [requestScreen]. */
    private val screenPermitted: () -> Boolean = { true },
    /**
     * Bittet die Oberfläche, den Aufnahmedialog von Android zu öffnen.
     *
     * Gerufen, wenn eine Sitzung zugestimmt hat, die Erlaubnis aber fehlt —
     * etwa weil Android sie zurückgenommen hat. Die Karte „darf dieses Gerät
     * verbinden?" kommt in dem Fall nicht noch einmal; sie ist beantwortet.
     */
    private val requestScreen: () -> Unit = {},
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

        /**
         * Länger, wenn dafür ein Mensch den Aufnahmedialog bestätigen muss —
         * das dauert, bis das Handy in der Hand liegt.
         */
        private const val DIALOG_WAIT_MS = 25_000L

        /** Wie oft während der Rückfrage ein Lebenszeichen hinausgeht. */
        private const val AWAITING_INTERVAL_MS = 2_000L
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

        request.path == "/api/pair" && request.method == "POST" -> pair(request)

        request.path == "/api/session/challenge" && request.method == "POST" -> challenge(request)

        request.path == "/api/session" && request.method == "POST" -> openSession(request)

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
    internal fun awaitSource(): FrameSource? {
        if (!screenAllowed()) {
            return null
        }

        // Die Zustimmung steht, die Erlaubnis fehlt: dann muss der Dialog von
        // Android noch einmal her, und zwar jetzt — nicht erst bei der
        // nächsten Karte, die es in dieser Sitzung nicht mehr gibt.
        val needsDialog = !screenPermitted()

        if (needsDialog) {
            requestScreen()
        }

        val deadline = System.currentTimeMillis() + (if (needsDialog) DIALOG_WAIT_MS else SOURCE_WAIT_MS)

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
     * Schickt alle zwei Sekunden `{"t":"awaiting"}`, bis der Thread
     * unterbrochen wird — das Lebenszeichen, solange am Gerät gefragt wird.
     */
    private fun keepAwaiting(socket: WebSocketConnection): Thread =
        Thread({
            try {
                while (socket.isOpen) {
                    Thread.sleep(AWAITING_INTERVAL_MS)
                    socket.sendText(JSONObject().put("t", "awaiting").toString())
                }
            } catch (_: InterruptedException) {
                // Die Antwort ist da.
            } catch (_: Exception) {
                // Der Socket ist zu; der wartende Thread merkt es selbst.
            }
        }, "remotedesktop-awaiting").apply {
            isDaemon = true
            start()
        }

    /**
     * Räumt auf, wenn eine Verbindung endet.
     *
     * <p>
     * Geht die letzte Verbindung dieses Geräts, wird die Zustimmung des
     * Menschen vergessen: sie galt dieser Verbindung und nicht dem
     * Sitzungstoken, das zwölf Stunden lebt. Geht die letzte Verbindung
     * überhaupt, endet auch die Aufnahme samt Erlaubnis. Beides hängt an
     * Verbindungen, nicht am Bild-Socket allein — ein neu aufgebauter
     * Bild-Socket bei stehendem Eingabe-Socket ist kein Ende.
     * </p>
     */
    internal fun partOver(clientId: String?, session: HostSession?) {
        if (live.countFor(clientId) == 0) {
            session?.forget()
        }

        if (live.count == 0) {
            releaseScreen()
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
            // **Erst sagen, dass gewartet wird.** Der WebSocket steht in
            // diesem Augenblick schon — das Aufrüsten passiert, bevor dieser
            // Rückruf läuft. Für die Gegenseite sieht eine Verbindung, an der
            // gerade jemand um Zustimmung gebeten wird, deshalb genauso aus wie
            // eine, die gleich Bilder liefert: sie öffnet ihre
            // Bildschirmansicht und wartet dort vor einer schwarzen Fläche.
            // Diese eine Zeile macht daraus ein „Warte auf Bestätigung".
            //
            // Eine ältere Gegenstelle kennt die Nachricht nicht und wirft sie
            // weg — dann bleibt es beim Verhalten von vorher.
            socket.sendText(JSONObject().put("t", "awaiting").toString())

            // **Angemeldet wird sofort, nicht erst nach der Zustimmung.** Eine
            // Verbindung, die noch auf die Antwort wartet, ist eine Verbindung:
            // endete vorher eine andere desselben Geräts, sah `partOver` null
            // offene und vergaß Zustimmung und Aufnahme — und die wartende
            // fragte danach ein zweites Mal (18.09.2026). Ein älterer Bild-Socket
            // desselben Geräts wird dabei abgelöst.
            val release = live.register(clientOf(request), LiveConnections.Kind.SCREEN) {
                socket.close()
            }

            // Während gefragt wird, alle zwei Sekunden ein Lebenszeichen: eine
            // ältere App hält sonst die Stille für einen Abbruch.
            val heartbeat = keepAwaiting(socket)

            // Erst die Zustimmung, dann die Aufnahme. Andersherum stünde am
            // Handy ein Systemdialog, bevor jemand überhaupt zugestimmt hat,
            // dass dieses Gerät zusehen darf.
            val refused = requireConfirmation(request)
            val source = if (refused == null) awaitSource() else null

            heartbeat.interrupt()

            if (refused != null || source == null) {
                // Kein Fehler im Sinne von kaputt: es hat niemand zugestimmt
                // oder die Aufnahme bestätigt. Die App zeigt den Satz an, statt
                // ein schwarzes Bild stehen zu lassen.
                runCatching {
                    socket.sendText(
                        JSONObject()
                            .put("t", "error")
                            .put("message", refused ?: NO_SCREEN)
                            .toString(),
                    )
                }

                socket.close()
                release()
                partOver(clientOf(request), sessionOf(request))
                return@Response
            }

            val display = screen()
            val stream = ScreenStream(source, display.width, display.height)

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
                partOver(clientOf(request), sessionOf(request))
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
            // Sofort angemeldet, wie beim Bild — siehe dort.
            val release = live.register(clientOf(request), LiveConnections.Kind.INPUT) {
                socket.close()
            }

            requireConfirmation(request)?.let { failure ->
                runCatching {
                    socket.sendText(
                        JSONObject().put("t", "error").put("message", failure).toString(),
                    )
                }

                socket.close()
                release()
                partOver(clientOf(request), sessionOf(request))
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
                partOver(clientOf(request), sessionOf(request))
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

    private fun pair(request: HttpServer.Request): HttpServer.Response {
        val body = json(request) ?: return badJson()

        val result = pairing.pair(
            body.optString("code"),
            body.optString("label"),
            body.optString("publicKey"),
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

    /**
     * Widerrufen heißt: ab jetzt **und** rückwirkend auf alles, was schon
     * steht. Der Eintrag allein zu löschen genügt nicht — Bild und Eingabe
     * laufen über Dauerverbindungen, und keine davon wird nach dem Aufbau noch
     * einmal geprüft.
     */
    /**
     * Kopplungscode, Clientliste und Widerruf haben seit v1.4 **keine Route**
     * mehr: die App dieses Handys ruft sie über das Plugin auf, und über das
     * Netz gab es sie nur, weil der Rechner sie hat — dort braucht das Fenster
     * sie. Eine Route, die „nur lokal" erreichbar ist, ist eine Route, die
     * jede App auf dem Handy erreicht.
     */
    internal fun revoke(id: String): Boolean {
        if (!pairing.revoke(id)) {
            return false
        }

        live.close(id)

        return true
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
