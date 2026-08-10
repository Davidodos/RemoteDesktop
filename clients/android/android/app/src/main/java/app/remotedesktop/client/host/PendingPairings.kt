package app.remotedesktop.client.host

import org.json.JSONObject

/**
 * Das Angebot der Gegenseite, sich auch in die andere Richtung zu koppeln.
 *
 * Gegenstück zu `agent/Auth/PendingPairing.cs` — dieselben Regeln, damit ein
 * Handy und ein PC sich gleich verhalten.
 */
data class BackPairing(
    val host: String,
    val port: Int,
    val code: String,
    val caFingerprint: String?,
    val name: String?,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("host", host)
        .put("port", port)
        .put("code", code)
        .put("caFingerprint", caFingerprint)
        .put("name", name)
}

/**
 * Ein Angebot zur Gegenkopplung, das darauf wartet, eingelöst zu werden.
 *
 * **Warum es liegen bleibt:** wer koppelt, ist die Client-Seite — sie hält den
 * privaten Geräteschlüssel und die Geräteliste. Der Host hat beides nicht. Er
 * hebt das Angebot auf, bis die App danach fragt.
 *
 * Nur im Arbeitsspeicher und nur eines: ein zweites ersetzt das erste. Der Code
 * darin ist ohnehin nach fünf Minuten wertlos.
 */
class PendingPairings(private val now: Clock = System::currentTimeMillis) {

    companion object {
        /**
         * Etwas kürzer, als der Code darin gilt: ein Angebot, das noch dasteht,
         * wenn sein Code längst verfallen ist, führt nur zu einer Fehlermeldung
         * ohne Ursache.
         */
        const val LIFETIME_MS = 4 * 60 * 1000L

        /**
         * Prüft ein hereingereichtes Angebot. Unbrauchbares wird verworfen und
         * nicht aufgehoben: die Kopplung selbst gelingt trotzdem, nur eben in
         * eine Richtung.
         */
        fun sanitize(json: JSONObject?): BackPairing? {
            if (json == null) {
                return null
            }

            val host = json.optString("host").trim()
            val port = json.optInt("port")
            val code = json.optString("code").trim()

            if (host.isEmpty() || host.length > 255 || port !in 1..65535) {
                return null
            }

            if (code.length != 6 || !code.all { it.isDigit() }) {
                return null
            }

            val fingerprint = json.optString("caFingerprint").trim().lowercase()
            val name = json.optString("name").trim()

            return BackPairing(
                host = host,
                port = port,
                code = code,
                caFingerprint = fingerprint.takeIf {
                    it.length == 64 && it.all { c -> c.isDigit() || c in 'a'..'f' }
                },
                name = name.ifEmpty { null },
            )
        }
    }

    private val gate = Any()

    private var offer: BackPairing? = null
    private var expiresAt = 0L

    fun offer(pairing: BackPairing) = synchronized(gate) {
        offer = pairing
        expiresAt = now() + LIFETIME_MS
    }

    /**
     * Holt das Angebot und verbraucht es dabei. Ein zweiter Aufruf liefert
     * nichts — sonst versuchte die App bei jedem Nachsehen erneut, einen längst
     * eingelösten Code zu benutzen.
     */
    fun take(): BackPairing? = synchronized(gate) {
        val found = offer

        offer = null

        if (found == null || now() >= expiresAt) null else found
    }
}
