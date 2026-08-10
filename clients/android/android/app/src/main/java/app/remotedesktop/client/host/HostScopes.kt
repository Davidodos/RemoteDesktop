package app.remotedesktop.client.host

/**
 * Was ein gekoppelter Client an diesem Handy darf und welcher Pfad welches
 * Recht verlangt.
 *
 * Gegenstück zu `agent/Auth/AgentScopes.cs`, und wie dort eine Whitelist: ein
 * Pfad, der hier nicht steht, wird abgelehnt. Ein neuer Endpunkt, bei dem
 * jemand den Eintrag vergisst, fällt beim ersten Aufruf auf — die Alternative
 * wäre ein Endpunkt, den jeder Client mit irgendeinem Recht bedienen darf.
 *
 * Es sind nur drei. Ein Handy hat nichts herunterzufahren, keine
 * Medientasten für fremde Apps, keine Aktionen und weckt niemanden.
 */
object HostScopes {

    const val SCREEN = "screen"
    const val INPUT = "input"
    const val FILES = "files"

    val ALL: List<String> = listOf(SCREEN, INPUT, FILES)

    /** Was dieses Gerät kann — steht so in `/api/info`. */
    val CAPABILITIES: List<String> = listOf(SCREEN, INPUT, FILES)

    /**
     * Pfade, die jeder angemeldete Client aufrufen darf. `/api/info` sagt nur,
     * wie das Gerät heißt und was es kann — ohne diese Auskunft könnte die App
     * ihre Oberfläche nicht aufbauen.
     */
    private val WITHOUT_SCOPE = listOf("/api/info")

    /** Endpunkte, die selbst die Berechtigung erzeugen und deshalb ohne auskommen. */
    val WITHOUT_CREDENTIAL = listOf("/health", "/api/pair", "/api/session/challenge", "/api/session")

    /**
     * Nur vom Gerät selbst aus erreichbar: den Kopplungscode anzeigen und
     * Clients widerrufen. Beides setzt voraus, dass jemand das Handy in der
     * Hand hat — über das Netz wäre es genau der Weg, den die Kopplung
     * verhindern soll.
     */
    val LOCAL_ONLY = listOf("/api/pair/code", "/api/clients")

    private val MAPPING = listOf(
        "/ws/screen" to SCREEN,
        "/api/webrtc" to SCREEN,
        "/ws/input" to INPUT,
        "/api/files" to FILES,
    )

    fun isKnown(scope: String): Boolean = ALL.contains(scope)

    /**
     * Das nötige Recht für einen Pfad.
     *
     * @return `null` für einen unbekannten Pfad — dann wird abgelehnt, nicht
     *   durchgelassen. `Resolved(null)` heißt dagegen: bekannt und ohne Recht
     *   erreichbar.
     */
    fun resolve(path: String): Resolved? {
        if (WITHOUT_SCOPE.any { matches(path, it) }) {
            return Resolved(null)
        }

        for ((prefix, scope) in MAPPING) {
            if (matches(path, prefix)) {
                return Resolved(scope)
            }
        }

        return null
    }

    data class Resolved(val scope: String?)

    /**
     * Vergleicht auf Segmentgrenzen. Ein einfacher Präfixvergleich ließe
     * `/api/filesystem` als `/api/files` durchgehen.
     */
    fun matches(path: String, prefix: String): Boolean =
        path.equals(prefix, ignoreCase = true) || path.startsWith("$prefix/", ignoreCase = true)
}
