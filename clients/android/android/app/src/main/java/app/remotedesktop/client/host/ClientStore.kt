package app.remotedesktop.client.host

import java.io.File
import org.json.JSONArray
import org.json.JSONObject

/**
 * Ein Client, der sich einmal an diesem Handy angemeldet hat.
 *
 * Gespeichert wird nur der öffentliche Schlüssel. Das Handy kann damit prüfen,
 * ob der Client der ist, für den er sich ausgibt — aber es kann sich nicht
 * selbst als dieser Client ausgeben. Wer die Datei liest, hat nichts in der
 * Hand.
 */
data class PairedClient(
    val id: String,
    val label: String,
    val publicKey: String,
    val scopes: List<String>,
    val createdAt: Long,
    val lastSeenAt: Long,
) {
    fun allows(scope: String?): Boolean = scope == null || scopes.contains(scope)

    fun toJson(): JSONObject = JSONObject().apply {
        put("id", id)
        put("label", label)
        put("publicKey", publicKey)
        put("scopes", JSONArray(scopes))
        put("createdAt", createdAt)
        put("lastSeenAt", lastSeenAt)
    }

    companion object {
        fun fromJson(json: JSONObject): PairedClient {
            val scopes = json.optJSONArray("scopes")

            return PairedClient(
                id = json.getString("id"),
                label = json.optString("label"),
                publicKey = json.getString("publicKey"),
                scopes = (0 until (scopes?.length() ?: 0)).map { scopes!!.getString(it) },
                createdAt = json.optLong("createdAt"),
                lastSeenAt = json.optLong("lastSeenAt"),
            )
        }
    }
}

/**
 * Die `clients.json` dieses Handys: welche Geräte es kennt und was sie dürfen.
 *
 * Der Host vertraut ausschließlich dieser Datei. Steht ein Gerät nicht darin,
 * kommt es nicht herein — gleich, was es behauptet.
 */
class ClientStore(private val file: File) {

    private val gate = Any()
    private var clients: List<PairedClient> = read()

    fun list(): List<PairedClient> = synchronized(gate) { clients.toList() }

    fun find(id: String): PairedClient? = synchronized(gate) { clients.firstOrNull { it.id == id } }

    fun add(client: PairedClient) = synchronized(gate) {
        clients = clients.filter { it.id != client.id } + client
        write()
    }

    /** @return `false`, wenn es den Client gar nicht gab. */
    fun revoke(id: String): Boolean = synchronized(gate) {
        val remaining = clients.filter { it.id != id }

        if (remaining.size == clients.size) {
            return false
        }

        clients = remaining
        write()
        true
    }

    /**
     * Hält fest, wann der Client zuletzt eine Sitzung geöffnet hat. Das ist die
     * einzige Grundlage, auf der sich später entscheiden lässt, welcher Eintrag
     * ein vergessenes altes Gerät ist.
     */
    fun touch(id: String, seenAt: Long) = synchronized(gate) {
        if (clients.none { it.id == id }) {
            return
        }

        clients = clients.map { if (it.id == id) it.copy(lastSeenAt = seenAt) else it }
        write()
    }

    private fun write() {
        val array = JSONArray().apply { clients.forEach { put(it.toJson()) } }

        // Erst daneben schreiben, dann umbenennen: ein Absturz mitten im
        // Schreiben würde sonst die Liste aller zugelassenen Geräte zerstören —
        // und damit den Zugang zu einem Handy, das man gerade nicht in der Hand
        // hat.
        val temporary = File(file.parentFile, file.name + ".tmp")

        file.parentFile?.mkdirs()
        temporary.writeText(array.toString(2))

        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }

    /**
     * Eine fehlende Datei ist der Normalfall beim ersten Start. Eine kaputte
     * Datei bleibt hier ebenfalls folgenlos — anders als auf Windows, wo eine
     * Ausnahme jemanden vor den Rechner ruft. Hier gibt es niemanden, der eine
     * Ausnahme sähe: der Dienst käme gar nicht erst hoch, und das Handy wäre
     * still weg. Wer die Datei verliert, koppelt neu.
     */
    private fun read(): List<PairedClient> {
        if (!file.exists()) {
            return emptyList()
        }

        return try {
            val array = JSONArray(file.readText())

            (0 until array.length()).map { PairedClient.fromJson(array.getJSONObject(it)) }
        } catch (broken: Exception) {
            emptyList()
        }
    }
}
