package app.remotedesktop.client.surfaces

import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import org.json.JSONObject

/**
 * Der Weg zum Agent, wenn die App nicht läuft.
 *
 * Dieselben Endpunkte, die sonst `transport/direct.ts` anspricht — nur ohne
 * WebView, ohne `fetch` und ohne Sitzungstoken aus einem früheren Aufruf: eine
 * Fläche wird angetippt, macht ihre drei Anfragen und ist wieder fort. Für ein
 * gemerktes Token gäbe es keinen Ort, an dem es sicherer läge als der
 * Schlüssel, aus dem man sich jederzeit ein neues holt.
 *
 * TLS prüft Android selbst: der Agent trägt ein Zertifikat aus
 * `tailscale cert`, und dessen Aussteller kennt jedes Gerät.
 */
class AgentLink(private val node: SurfaceBoard.Node, private val clientKey: String) {

    /** Löst eine Aktion aus. Was sie tut, steht ausschließlich am Zielrechner. */
    fun invokeAction(id: String) {
        val path = "/api/actions/" + URLEncoder.encode(id, "UTF-8") + "/invoke"

        post(path, null, token())
    }

    /** Lässt diesen Knoten ein Magic Packet aussenden — er ist der Bote. */
    fun wake(mac: String) {
        post("/api/wol", JSONObject().put("mac", mac), token())
    }

    fun sleep() {
        post("/api/power", JSONObject().put("action", "sleep"), token())
    }

    /**
     * Meldet diesen Client an und liefert das Sitzungstoken.
     *
     * Zwei Anfragen: eine Zufallszahl holen, sie mit dem Geräteschlüssel
     * unterschreiben, zurückschicken. Beide brauchen selbst keinen Ausweis —
     * das ist ja gerade der Zweck.
     */
    private fun token(): String {
        val nonce = post(
            "/api/session/challenge",
            JSONObject().put("clientId", node.clientId),
            null,
        )?.optString("nonce").orEmpty()

        if (nonce.isEmpty()) {
            throw IOException("Der Agent hat keine Challenge geschickt.")
        }

        val session = post(
            "/api/session",
            JSONObject()
                .put("clientId", node.clientId)
                .put("nonce", nonce)
                .put("signature", Signatures.sign(clientKey, nonce)),
            null,
        )?.optString("token").orEmpty()

        if (session.isEmpty()) {
            throw IOException("Der Agent hat die Anmeldung nicht bestätigt.")
        }

        return session
    }

    private fun post(path: String, body: JSONObject?, token: String?): JSONObject? {
        val connection = open(path)

        connection.requestMethod = "POST"
        connection.doOutput = true

        if (token != null) {
            connection.setRequestProperty("Authorization", "Bearer $token")
        }

        if (body != null) {
            connection.setRequestProperty("Content-Type", "application/json")
        }

        try {
            connection.outputStream.use { out ->
                out.write((body?.toString() ?: "{}").toByteArray(Charsets.UTF_8))
            }

            val status = connection.responseCode

            if (status !in 200..299) {
                throw IOException(explain(status, path))
            }

            val text = connection.inputStream.bufferedReader().use { it.readText() }

            return if (text.isEmpty()) null else JSONObject(text)
        } finally {
            connection.disconnect()
        }
    }

    /**
     * Der Statuscode allein hilft niemandem, der gerade auf sein Handy sieht —
     * die Fläche zeigt genau diesen Satz an.
     */
    private fun explain(status: Int, path: String): String = when (status) {
        401 -> "${node.host} kennt dieses Handy nicht mehr. In der App neu koppeln."
        403 -> "${node.host} verweigert das — dem gekoppelten Gerät fehlt das Recht dafür."
        404 -> "${node.host} kennt diese Aktion nicht mehr."
        else -> "${node.host} antwortete auf $path mit HTTP $status."
    }

    private fun open(path: String): HttpURLConnection {
        val connection =
            URL("https://${node.host}:${node.port}$path").openConnection() as HttpURLConnection

        // Kurz gehalten: ein Widget-Tipp läuft in einem Rundruf mit knapp
        // bemessener Zeit (siehe SurfaceWork), und ein schlafender Rechner
        // antwortet ohnehin nie.
        connection.connectTimeout = TIMEOUT_MS
        connection.readTimeout = TIMEOUT_MS
        connection.useCaches = false

        return connection
    }

    companion object {
        private const val TIMEOUT_MS = 4000

        /**
         * Ob der Knoten gerade antwortet. `/health` ist der einzige Endpunkt
         * ohne Ausweis — eine Anmeldung nur zum Nachsehen, ob jemand da ist,
         * wäre verkehrt herum.
         */
        fun isAwake(node: SurfaceBoard.Node): Boolean {
            val connection = try {
                URL("https://${node.host}:${node.port}/health").openConnection()
                    as HttpURLConnection
            } catch (unreachable: IOException) {
                return false
            }

            connection.connectTimeout = TIMEOUT_MS
            connection.readTimeout = TIMEOUT_MS
            connection.useCaches = false

            return try {
                connection.responseCode in 200..299
            } catch (asleep: IOException) {
                // Zeitüberschreitung, unbekannter Name, abgelehnte Verbindung —
                // für die Frage „antwortet er?" ist das alles dasselbe.
                false
            } finally {
                connection.disconnect()
            }
        }
    }
}
