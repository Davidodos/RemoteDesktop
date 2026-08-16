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
    private val deviceName: String,
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
     * Wohin die Eingaben gehen. Gibt eine Meldung zurück, wenn es nicht geht —
     * etwa weil die Bedienungshilfe aus ist. Ein Lambda, damit der Server ohne
     * Android unter Test steht.
     */
    private val input: (InputCommand) -> String? = { NO_INPUT },
    private val live: LiveConnections = LiveConnections(),
    /**
     * Wer die Verbindung bestätigt. Gibt `false` zurück, wenn niemand
     * zugestimmt hat — Ablehnung ist die Vorgabe. Ein Lambda, damit der Server
     * ohne Android unter Test steht; im Test steht hier `true`, weil sonst jeder
     * Anmeldeweg an einer Frage hängen bliebe, die niemand beantwortet.
     */
    private val confirm: (String) -> Boolean = { true },
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

        request.path == "/ws/screen" -> screenSocket(request)

        request.path == "/ws/input" -> inputSocket(request)

        else -> HttpServer.Response.error(404, "Diesen Endpunkt gibt es hier nicht.")
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
            val source = screenSource()

            if (source == null) {
                // Kein Fehler im Sinne von kaputt: es hat nur noch niemand die
                // Aufnahme bestätigt. Die App zeigt den Satz an, statt ein
                // schwarzes Bild stehen zu lassen.
                socket.sendText(
                    JSONObject()
                        .put("t", "error")
                        .put(
                            "message",
                            "Dieses Gerät gibt seinen Bildschirm noch nicht frei. " +
                                "Am Handy unter „Dieses Gerät freigeben\" die " +
                                "Bildschirmaufnahme einschalten.",
                        )
                        .toString(),
                )

                socket.close()
                return@Response
            }

            val display = screen()
            val stream = ScreenStream(source, display.width, display.height)

            val release = live.register(clientOf(request)) { socket.close() }

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
            val release = live.register(clientOf(request)) { socket.close() }

            // Je Verbindung höchstens eine Meldung derselben Art. Ohne das
            // stünde bei jedem Antippen dieselbe Zeile in der Statuszeile, und
            // die eine, auf die es ankommt, ginge darin unter.
            val reported = HashSet<String>()

            try {
                socket.listen(onText = { message ->
                    val command = InputCommands.parse(message) ?: return@listen
                    val failure = input(command)

                    if (failure != null && reported.add(failure)) {
                        socket.sendText(
                            JSONObject().put("t", "error").put("message", failure).toString(),
                        )
                    }
                })
            } finally {
                socket.close()
                release()
            }
        }

    /**
     * Wem diese Verbindung gehört. Gebraucht für den Widerruf: eine
     * Dauerverbindung wird nach dem Aufbau nie wieder geprüft und überlebte
     * sonst ihre eigene Berechtigung.
     */
    private fun clientOf(request: HttpServer.Request): String? =
        credentialOf(request)?.let { sessions.find(it)?.clientId }

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
            .put("hostname", deviceName)
            .put("version", version)
            .put("protocol", PROTOCOL)
            .put("capabilities", JSONArray(HostScopes.CAPABILITIES))
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
            .put("hostname", deviceName)
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
                    .put("name", deviceName)
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

        // Erst prüfen, dann fragen: wer nicht gekoppelt ist, soll am Handy
        // keine Karte auslösen können. Eine Kopplung sagt, *wer* fragen darf —
        // dass jetzt gerade jemand zusehen darf, sagt nur ein Mensch.
        if (!confirm(result.client?.label ?: "Ein gekoppeltes Gerät")) {
            sessions.close(result.token)

            return HttpServer.Response.error(
                403,
                "Am anderen Gerät hat niemand zugestimmt. Jede Verbindung wird " +
                    "dort einzeln bestätigt — die App muss offen sein.",
            )
        }

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

    private val gate = Any()
    private val open = HashMap<String, MutableList<() -> Unit>>()

    fun register(clientId: String?, cut: () -> Unit): () -> Unit {
        if (clientId == null) {
            return {}
        }

        synchronized(gate) {
            open.getOrPut(clientId) { mutableListOf() }.add(cut)
        }

        return {
            synchronized(gate) {
                open[clientId]?.remove(cut)
            }
            Unit
        }
    }

    /** @return Wie viele Verbindungen dabei getrennt wurden. */
    fun close(clientId: String): Int = synchronized(gate) {
        val cuts = open.remove(clientId).orEmpty()

        cuts.forEach { runCatching(it) }

        cuts.size
    }

    fun closeAll() = synchronized(gate) {
        open.keys.toList().forEach { close(it) }
    }
}
