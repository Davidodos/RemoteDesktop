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
    private val screen: () -> Screen,
    private val address: () -> String?,
    private val live: LiveConnections = LiveConnections(),
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

        else -> HttpServer.Response.error(404, "Diesen Endpunkt gibt es hier nicht.")
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
            .put("hostname", deviceName)
            .put("version", version)
            .put("protocol", PROTOCOL)
            .put("capabilities", JSONArray(HostScopes.CAPABILITIES))
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

        val json = JSONObject()
            .put("clientId", client.id)
            .put("scopes", JSONArray(client.scopes))
            .put("hostname", deviceName)
            .put("agentPublicKey", identity.publicKey)
            .put("agentFingerprint", identity.fingerprint)
            .put("caFingerprint", material.fingerprint)

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
