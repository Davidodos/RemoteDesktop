package app.remotedesktop.client.host

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import java.util.Locale

/**
 * Die drei kurzlebigen Geheimnisse der Kopplung: der angezeigte Code, die
 * Challenge und das Sitzungstoken.
 *
 * Alle drei liegen ausschließlich im Arbeitsspeicher und überleben keinen
 * Neustart des Dienstes. Das ist Absicht — nichts, was jemand auf der Platte
 * findet, öffnet einen Zugang.
 *
 * `now` ist einstellbar, damit sich Ablaufzeiten prüfen lassen, ohne im Test
 * fünf Minuten zu warten.
 */

/** Zeitquelle. Im Betrieb die Uhr, im Test eine Zahl, die der Test selbst dreht. */
typealias Clock = () -> Long

/**
 * Der sechsstellige Code, den das Handy anzeigt und den man am anderen Gerät
 * eintippt.
 *
 * Sechs Ziffern sind wenig — eine Million Möglichkeiten rät man durch, wenn man
 * beliebig oft raten darf. Die Sicherheit hängt deshalb nicht an der Länge,
 * sondern an drei Grenzen: fünf Minuten Gültigkeit, genau eine Verwendung, und
 * ein Zähler für Fehlversuche.
 */
class PairingCodes(private val now: Clock = System::currentTimeMillis) {

    companion object {
        const val LIFETIME_MS = 5 * 60 * 1000L
        const val MAX_ATTEMPTS = 5
    }

    private val gate = Any()
    private val random = SecureRandom()

    private var code: String? = null
    private var expiresAt = 0L
    private var failed = 0

    /**
     * Erzeugt einen Code und verwirft einen noch offenen. Zwei gültige Codes
     * gleichzeitig wären nur eine zweite Angriffsfläche — angezeigt wird
     * ohnehin immer der neueste.
     */
    fun issue(): String = synchronized(gate) {
        val fresh = String.format(Locale.ROOT, "%06d", random.nextInt(1_000_000))

        code = fresh
        expiresAt = now() + LIFETIME_MS
        failed = 0

        fresh
    }

    /** Wie lange der offene Code noch gilt; `null` ohne offenen Code. */
    fun remainingMs(): Long? = synchronized(gate) {
        if (code == null) {
            return null
        }

        val remaining = expiresAt - now()

        if (remaining > 0) remaining else null
    }

    /** Verwirft einen offenen Code, ohne einen neuen auszugeben. */
    fun clear() = synchronized(gate) {
        code = null
    }

    /**
     * Prüft den eingetippten Code und verbraucht ihn dabei. Ein zweiter Aufruf
     * mit demselben Code schlägt fehl, auch wenn er richtig war.
     */
    fun tryRedeem(presented: String): Boolean = synchronized(gate) {
        val open = code

        if (open == null || now() >= expiresAt) {
            code = null
            return false
        }

        if (constantTimeEquals(presented, open)) {
            code = null
            return true
        }

        if (++failed >= MAX_ATTEMPTS) {
            code = null
        }

        false
    }
}

/**
 * Die Zufallszahlen, mit denen sich ein gekoppelter Client ausweist.
 *
 * Der Host gibt eine aus, der Client unterschreibt sie mit seinem privaten
 * Schlüssel. Dass sie nur einmal gilt und schnell verfällt, ist der ganze
 * Punkt: eine mitgeschnittene Unterschrift ist danach wertlos.
 */
class ChallengeStore(private val now: Clock = System::currentTimeMillis) {

    companion object {
        const val LIFETIME_MS = 30 * 1000L

        /**
         * Obergrenze, damit ein Client nicht durch stures Anfordern den
         * Speicher des Handys füllt.
         */
        private const val MAX_OUTSTANDING = 64
    }

    private val gate = Any()
    private val random = SecureRandom()
    private val open = HashMap<String, Pair<String, Long>>()

    /** @return Die Challenge als Base64 — so geht sie durch JSON. */
    fun issue(clientId: String): String {
        val nonce = Base64.getEncoder().encodeToString(ByteArray(32).also(random::nextBytes))

        synchronized(gate) {
            dropExpired()

            if (open.size >= MAX_OUTSTANDING) {
                open.clear()
            }

            open[nonce] = clientId to (now() + LIFETIME_MS)
        }

        return nonce
    }

    /**
     * Löst die Challenge ein. Gelingt es, ist sie verbraucht — auch wenn die
     * Signaturprüfung danach scheitert. Sonst ließe sich dieselbe Challenge
     * beliebig oft gegen Unterschriften probieren.
     */
    fun tryConsume(clientId: String, nonce: String): ByteArray? {
        synchronized(gate) {
            dropExpired()

            val entry = open.remove(nonce) ?: return null

            if (entry.first != clientId) {
                return null
            }
        }

        return try {
            Base64.getDecoder().decode(nonce)
        } catch (broken: IllegalArgumentException) {
            null
        }
    }

    private fun dropExpired() {
        val moment = now()

        open.entries.removeAll { it.value.second <= moment }
    }
}

/** Eine offene Sitzung: wer sie hat und was er damit darf. */
data class HostSession(val clientId: String, val scopes: List<String>) {
    fun allows(scope: String?): Boolean = scope == null || scopes.contains(scope)
}

/**
 * Die Sitzungstokens, die ein Client nach bestandener Signaturprüfung bekommt.
 * Zwölf Stunden, danach weist er sich neu aus — das kostet ihn eine Unterschrift
 * und keine Sekunde.
 */
class SessionStore(private val now: Clock = System::currentTimeMillis) {

    companion object {
        const val LIFETIME_MS = 12 * 60 * 60 * 1000L
    }

    private class Entry(val token: String, val session: HostSession, val expiresAt: Long)

    private val gate = Any()
    private val random = SecureRandom()
    private val sessions = ArrayList<Entry>()

    fun open(client: PairedClient): String {
        val token = Base64.getEncoder().encodeToString(ByteArray(32).also(random::nextBytes))

        synchronized(gate) {
            dropExpired()
            sessions.add(
                Entry(token, HostSession(client.id, client.scopes), now() + LIFETIME_MS),
            )
        }

        return token
    }

    /**
     * Sucht die Sitzung zu einem vorgelegten Token.
     *
     * Die Liste wird durchgegangen statt in eine Map geschlagen, damit jeder
     * Vergleich in fester Zeit läuft. Bei einer Handvoll Sitzungen kostet das
     * nichts; ein Hash-Zugriff dagegen verrät über die Laufzeit, ob ein Token
     * überhaupt existiert.
     */
    fun find(presented: String): HostSession? = synchronized(gate) {
        dropExpired()

        var found: HostSession? = null

        for (entry in sessions) {
            if (constantTimeEquals(presented, entry.token)) {
                found = entry.session
            }
        }

        found
    }

    /**
     * Schließt alle Sitzungen eines Clients. Ohne das liefe ein widerrufenes
     * Gerät bis zum Ablauf seines Tokens weiter — der Widerruf muss sofort
     * wirken, sonst ist er keiner.
     */
    fun closeAll(clientId: String) = synchronized(gate) {
        sessions.removeAll { it.session.clientId == clientId }
    }

    private fun dropExpired() {
        val moment = now()

        sessions.removeAll { it.expiresAt <= moment }
    }
}

/**
 * Vergleich in fester Zeit.
 *
 * Ein früher Abbruch verriete über die Laufzeit, wie weit jemand richtig
 * geraten hat. Eine abweichende Länge ist ohnehin falsch und wird sofort
 * aussortiert — sie verrät nichts, weil die Länge beider Werte bekannt ist.
 */
internal fun constantTimeEquals(left: String, right: String): Boolean {
    if (left.length != right.length) {
        return false
    }

    var difference = 0

    for (index in left.indices) {
        difference = difference or (left[index].code xor right[index].code)
    }

    return difference == 0
}

/** Die ersten 16 Hex-Stellen des SHA-256 über einen Wert. */
internal fun shortFingerprint(data: ByteArray): String =
    MessageDigest.getInstance("SHA-256")
        .digest(data)
        .joinToString("") { String.format(Locale.ROOT, "%02x", it) }
        .take(16)
