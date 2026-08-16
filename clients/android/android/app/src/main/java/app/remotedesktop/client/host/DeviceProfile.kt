package app.remotedesktop.client.host

import java.io.File
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.SecureRandom
import java.security.spec.ECGenParameterSpec
import java.security.spec.PKCS8EncodedKeySpec
import java.security.spec.X509EncodedKeySpec
import java.util.Base64
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
 * Der Ausweis dieses Handys als *Client* — das Schlüsselpaar, mit dem sich seine
 * Oberfläche bei fremden Geräten anmeldet.
 *
 * **Der Befund dahinter (16.08.2026):** er gehörte der App und lag in ihrem
 * Speicher; der Host kannte ihn nur, weil die App ihn beim Start hinterlegte.
 * Damit hing der Ausweis am Lebenslauf einer Weboberfläche — am Rechner reichte
 * es, das Fenster zu öffnen und die Fernsteuerung nie anzuzeigen, und die
 * Gegenseite bekam beim Koppeln ein leeres `clientKey`. Jetzt liegt er bei den
 * übrigen Schlüsseln des Geräts, und die App holt ihn sich von dort.
 *
 * Der private Teil verlässt das Handy nie. Er liegt in den privaten Dateien der
 * App, genau wie der Host-Schlüssel daneben — und beide sind seit derselben
 * Sitzung vom Cloud-Backup ausgenommen.
 *
 * Schwesterfassungen: `setup/ClientKeyFile.cs` und `app/src/lib/clientKey.ts`.
 */
class LocalClientKey(private val file: File) {

    private val gate = Any()
    private var pair: KeyPair? = null

    /** Öffentlicher Schlüssel als Base64 im SPKI-Format. */
    val publicKey: String get() = Base64.getEncoder().encodeToString(keyPair().public.encoded)

    /** Privater Schlüssel als Base64 im PKCS-8-Format — so nimmt ihn WebCrypto an. */
    val privateKey: String get() = Base64.getEncoder().encodeToString(keyPair().private.encoded)

    /**
     * Das abgelegte Paar, oder ein neues an derselben Stelle. Wer zuerst
     * fragt, legt es an: der Host beim Koppeln, die App beim Anmelden.
     */
    private fun keyPair(): KeyPair = synchronized(gate) {
        pair?.let { return it }

        val loaded = existing() ?: create()

        pair = loaded

        loaded
    }

    private fun create(): KeyPair {
        val created = KeyPairGenerator.getInstance("EC").apply {
            initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
        }.generateKeyPair()

        file.parentFile?.mkdirs()

        // Beide Hälften, eine je Zeile — dasselbe Format wie beim
        // Host-Schlüssel. Java gibt aus einem privaten EC-Schlüssel den
        // öffentlichen nicht wieder heraus.
        file.writeText(
            Base64.getEncoder().encodeToString(created.private.encoded) +
                "\n" +
                Base64.getEncoder().encodeToString(created.public.encoded),
        )

        return created
    }

    /**
     * Eine unlesbare Datei zählt als „keine": dann entsteht ein neues Paar, und
     * alle Kopplungen müssen erneuert werden. Unangenehm, aber sichtbar — ein
     * halber Ausweis wäre es nicht.
     */
    private fun existing(): KeyPair? {
        if (!file.exists()) {
            return null
        }

        return runCatching {
            val lines = file.readText().trim().lines()

            if (lines.size != 2) {
                return null
            }

            val factory = KeyFactory.getInstance("EC")

            KeyPair(
                factory.generatePublic(
                    X509EncodedKeySpec(Base64.getDecoder().decode(lines[1].trim())),
                ),
                factory.generatePrivate(
                    PKCS8EncodedKeySpec(Base64.getDecoder().decode(lines[0].trim())),
                ),
            )
        }.getOrNull()
    }
}
