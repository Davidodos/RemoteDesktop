package app.remotedesktop.client.host

import java.io.File
import org.json.JSONArray
import org.json.JSONObject

/**
 * Der Steckbrief eines Geräts: alles, was die Gegenseite braucht, um es später
 * von sich aus zu erreichen.
 *
 * Gegenstück zu `agent/Auth/DeviceProfile.cs` — dieselben Regeln, damit ein
 * Handy und ein PC sich hier gleich verhalten.
 *
 * Er geht bei der Kopplung in beide Richtungen über die Leitung. Danach hat jede
 * Seite, was sie braucht, und **niemand muss noch einmal ins Netz**. Das ist der
 * Unterschied zum Vorgänger: der reichte einen Kopplungscode weiter, den die
 * andere Seite binnen fünf Minuten einlösen musste — und band die Gegenrichtung
 * damit an einen laufenden Server, eine offene App und eine Uhr. Ein Steckbrief
 * hat keine Frist: er ist eine Beschreibung, kein Geheimnis.
 */
data class DeviceProfile(
    val host: String,
    val port: Int,
    val name: String,
    val caFingerprint: String?,
    val agentFingerprint: String?,
    /**
     * Der öffentliche Schlüssel, mit dem sich die **Oberfläche** dieses Geräts
     * anmeldet. Er gehört in die `clients.json` der Gegenseite — das ist die
     * ganze Gegenrichtung, in einem Feld.
     */
    val clientKey: String?,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("host", host)
        .put("port", port)
        .put("name", name)
        .put("caFingerprint", caFingerprint)
        .put("agentFingerprint", agentFingerprint)
        .put("clientKey", clientKey)

    companion object {

        /** Länger nennt sich kein Gerät; alles darüber ist ein Versehen. */
        private const val MAX_NAME = 64

        /**
         * Prüft einen hereingereichten Steckbrief. Unbrauchbares wird verworfen
         * und nicht halb übernommen: die Kopplung selbst gelingt trotzdem, nur
         * eben in eine Richtung. Ein halber Eintrag führte später zu einem
         * Fehlschlag an einer Stelle, an der niemand mehr weiß, woher er kam.
         */
        fun sanitize(json: JSONObject?): DeviceProfile? {
            if (json == null) {
                return null
            }

            val host = json.optString("host").trim()
            val port = json.optInt("port")

            if (host.isEmpty() || host.length > 255 || port !in 1..65535) {
                return null
            }

            val name = json.optString("name").trim()
            val key = json.optString("clientKey").trim()

            return DeviceProfile(
                host = host,
                port = port,
                name = if (name.isNotEmpty() && name.length <= MAX_NAME) name else host,
                caFingerprint = hex(json.optString("caFingerprint"), 64),
                agentFingerprint = hex(json.optString("agentFingerprint"), 16),

                // Ein Schlüssel, den dieser Host nicht prüfen kann, ist keiner.
                // Er landete sonst als Karteileiche in der clients.json.
                clientKey = key.takeIf { PairingService.isUsablePublicKey(it) },
            )
        }

        fun fromJson(json: JSONObject): DeviceProfile? = sanitize(json)

        private fun hex(value: String?, length: Int): String? {
            val trimmed = value.orEmpty().trim().lowercase()

            return trimmed.takeIf {
                it.length == length && it.all { c -> c.isDigit() || c in 'a'..'f' }
            }
        }
    }
}

/**
 * Die Steckbriefe, die beim Koppeln hier abgegeben wurden — der Posteingang für
 * die App dieses Handys.
 *
 * **Warum sie liegen bleiben:** wer koppelt, ist die Client-Seite; sie hält den
 * privaten Geräteschlüssel und die Geräteliste. Der Host hat beides nicht. Er
 * nimmt den Steckbrief an und legt ihn hin, bis die App danach sieht.
 *
 * **Und warum auf Platte:** ein Steckbrief ist kein Geheimnis und hat keine
 * Frist. Er darf einen Neustart überleben — genau das war der Fehler des
 * Vorgängers.
 *
 * **Gelesen und geleert sind zwei Schritte.** Wer nur liest, verliert nichts,
 * wenn danach etwas schiefgeht — und wenn etwas schiefgeht, ist es hier fatal:
 * ein Steckbrief, der beim Abholen verschwindet und dessen Eintragen dann
 * scheitert, ist endgültig weg, und am Bildschirm steht „noch kein Gerät
 * gekoppelt" ohne zweiten Versuch.
 */
class PeerInbox(private val file: File) {

    private val gate = Any()
    private var peers: List<DeviceProfile> = read()

    fun add(peer: DeviceProfile) = synchronized(gate) {
        peers = peers.filter { key(it) != key(peer) } + peer
        write()
    }

    /** Was hier liegt. Ohne Nebenwirkung — siehe oben. */
    fun list(): List<DeviceProfile> = synchronized(gate) { peers.toList() }

    /**
     * Vergisst, was eingetragen ist. Erst jetzt — sonst käme ein Gerät, das
     * jemand aus seiner Liste entfernt hat, von allein zurück.
     */
    fun forget(ids: Collection<String>) = synchronized(gate) {
        val remaining = peers.filterNot { ids.contains(key(it)) }

        if (remaining.size != peers.size) {
            peers = remaining
            write()
        }

        Unit
    }

    companion object {
        /**
         * Woran zwei Einträge dasselbe Gerät sind. Der Fingerabdruck des
         * Agents, solange es einen gibt — er überlebt einen Namens- und
         * Adresswechsel.
         */
        fun key(peer: DeviceProfile): String =
            peer.agentFingerprint ?: "${peer.host}:${peer.port}"
    }

    private fun write() {
        val array = JSONArray().apply { peers.forEach { put(it.toJson()) } }

        // Erst daneben schreiben, dann umbenennen — wie in der ClientStore.
        val temporary = File(file.parentFile, file.name + ".tmp")

        file.parentFile?.mkdirs()
        temporary.writeText(array.toString(2))

        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }

    /**
     * Eine fehlende Datei ist der Normalfall. Eine kaputte bleibt folgenlos:
     * was hier steht, ist eine Bequemlichkeit, und dafür soll kein Host das
     * Starten verweigern.
     */
    private fun read(): List<DeviceProfile> {
        if (!file.exists()) {
            return emptyList()
        }

        return try {
            val array = JSONArray(file.readText())

            (0 until array.length()).mapNotNull { DeviceProfile.fromJson(array.getJSONObject(it)) }
        } catch (broken: Exception) {
            emptyList()
        }
    }
}

/**
 * Der Ausweis der App dieses Handys — der öffentliche Schlüssel, mit dem sie
 * sich bei fremden Geräten anmeldet.
 *
 * Der Host kennt ihn, weil eine Kopplung immer in beide Richtungen geht: wer
 * sich hier koppelt, bekommt ihn in der Antwort und trägt ihn bei sich ein. Der
 * Host hat ihn nicht selbst — er gehört der App, die ihn beim Start hinterlegt.
 *
 * Es ist ein öffentlicher Schlüssel. Er verrät nichts und erlaubt nichts; Macht
 * bekommt er erst dadurch, dass ihn die Gegenseite in ihre eigene `clients.json`
 * aufnimmt, und das tut sie nur nach einer bestandenen Kopplung.
 */
class LocalClient(private val file: File) {

    private val gate = Any()
    private var key: String? = read()

    val publicKey: String? get() = synchronized(gate) { key }

    /** @return `false`, wenn der Schlüssel keiner ist. */
    fun remember(publicKey: String?): Boolean {
        if (!PairingService.isUsablePublicKey(publicKey.orEmpty())) {
            return false
        }

        synchronized(gate) {
            if (key == publicKey) {
                return true
            }

            key = publicKey

            file.parentFile?.mkdirs()
            file.writeText(JSONObject().put("publicKey", publicKey).toString())
        }

        return true
    }

    private fun read(): String? {
        if (!file.exists()) {
            return null
        }

        return try {
            JSONObject(file.readText()).optString("publicKey").ifEmpty { null }
        } catch (broken: Exception) {
            null
        }
    }
}
