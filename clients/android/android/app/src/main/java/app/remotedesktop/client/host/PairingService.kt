package app.remotedesktop.client.host

import java.security.KeyFactory
import java.security.interfaces.ECPublicKey
import java.security.spec.X509EncodedKeySpec
import java.util.Base64

enum class PairOutcome { OK, BAD_CODE, BAD_PUBLIC_KEY, BAD_SCOPE, BAD_LABEL }

enum class SessionOutcome { OK, UNKNOWN_CLIENT, BAD_CHALLENGE, BAD_SIGNATURE }

data class PairResult(val outcome: PairOutcome, val client: PairedClient? = null)

data class SessionResult(
    val outcome: SessionOutcome,
    val token: String? = null,
    val client: PairedClient? = null,
)

/**
 * Der Ablauf der Kopplung und der Anmeldung, ohne HTTP.
 *
 * Bewusst getrennt vom Server: hier steht, was gilt, dort nur, welcher
 * Statuscode dabei herauskommt. Sonst ließe sich die Kopplung — der
 * empfindlichste Teil des Hosts — nur mit einem laufenden Webserver prüfen.
 *
 * Zeile für Zeile dasselbe wie `agent/Auth/PairingService.cs`. Das ist kein
 * Zufall und keine Bequemlichkeit: die Gegenseite ist derselbe Client, und wo
 * die Regeln auseinanderliefen, hätte man zwei Kopplungen mit zwei
 * Fehlerbildern.
 */
class PairingService(
    private val clients: ClientStore,
    private val codes: PairingCodes,
    private val challenges: ChallengeStore,
    private val sessions: SessionStore,
    private val now: Clock = System::currentTimeMillis,
) {


    /**
     * Nimmt einen Client auf, der den angezeigten Code richtig eingetippt hat.
     *
     * Der Code wird zuerst geprüft und dabei verbraucht. Wer ihn errät, soll
     * nicht dadurch einen zweiten Versuch bekommen, dass sein Schlüssel
     * unbrauchbar war.
     */
    fun pair(
        code: String,
        label: String,
        publicKey: String,
        scopes: List<String>?,
    ): PairResult {
        if (!codes.tryRedeem(code)) {
            return PairResult(PairOutcome.BAD_CODE)
        }

        val trimmed = label.trim()

        if (trimmed.isEmpty() || trimmed.length > MAX_LABEL_LENGTH) {
            return PairResult(PairOutcome.BAD_LABEL)
        }

        if (!isUsablePublicKey(publicKey)) {
            return PairResult(PairOutcome.BAD_PUBLIC_KEY)
        }

        // Was ein Client anfordert, das dieses Gerät gar nicht kennt, wird
        // stillschweigend weggelassen statt abgelehnt: die App fragt überall
        // dieselbe Liste an, und ein Handy kann davon nun einmal weniger. Ein
        // Fehlschlag hier hieße, dass sich ein Handy nie koppeln lässt, solange
        // die App auch nach „power" fragt.
        val wanted = scopes?.filter(HostScopes::isKnown).orEmpty()
        val granted = wanted.ifEmpty { HostScopes.ALL }

        val moment = now()

        // Die Kennung kommt aus dem Schlüssel selbst. Koppelt dasselbe Gerät
        // erneut, ersetzt der neue Eintrag den alten, statt die Liste mit
        // Karteileichen zu füllen.
        val client = PairedClient(
            id = shortFingerprint(Base64.getDecoder().decode(publicKey)),
            label = trimmed,
            publicKey = publicKey,
            scopes = granted,
            createdAt = moment,
            lastSeenAt = moment,
        )

        clients.add(client)

        return PairResult(PairOutcome.OK, client)
    }

    /** @return Die Challenge, oder `null` bei unbekanntem Client. */
    fun challenge(clientId: String): String? =
        if (clients.find(clientId) == null) null else challenges.issue(clientId)

    /**
     * Prüft die Unterschrift über die Challenge und öffnet bei Erfolg eine
     * Sitzung.
     */
    fun openSession(clientId: String, nonce: String, signature: String): SessionResult {
        val client = clients.find(clientId)
            ?: return SessionResult(SessionOutcome.UNKNOWN_CLIENT)

        val data = challenges.tryConsume(clientId, nonce)
            ?: return SessionResult(SessionOutcome.BAD_CHALLENGE)

        if (!HostIdentity.verifyClientSignature(client.publicKey, data, signature)) {
            return SessionResult(SessionOutcome.BAD_SIGNATURE)
        }

        clients.touch(client.id, now())

        return SessionResult(SessionOutcome.OK, sessions.open(client), client)
    }

    /**
     * Widerruft einen Client und wirft ihn zugleich aus seinen laufenden
     * Sitzungen. Beides gehört zusammen — der Eintrag allein zu löschen
     * verschöbe die Wirkung um bis zu zwölf Stunden.
     *
     * Die stehenden Verbindungen trennt der Server obendrein; siehe
     * `LiveConnections` dort.
     */
    fun revoke(clientId: String): Boolean {
        sessions.closeAll(clientId)

        return clients.revoke(clientId)
    }

    /**
     * Nimmt die Oberfläche der Gegenseite auf — ohne Code.
     *
     * Das ist die Gegenrichtung einer Kopplung, die gerade in die eine Richtung
     * bestanden wurde. Ein zweiter Code wäre kein Gewinn an Sicherheit: der
     * Schlüssel kam über dieselbe beglaubigte Verbindung, an deren Anfang jemand
     * einen Code eingetippt oder einen QR-Code gescannt hat. Deshalb ist dieser
     * Weg auch nur der App selbst zugänglich und steht nirgends im Netz.
     *
     * @return `false`, wenn der Schlüssel keiner ist.
     */
    fun grant(publicKey: String, label: String): Boolean {
        if (!isUsablePublicKey(publicKey)) {
            return false
        }

        val trimmed = label.trim()
        val at = now()

        clients.add(
            PairedClient(
                id = shortFingerprint(Base64.getDecoder().decode(publicKey)),
                label = if (trimmed.isNotEmpty() && trimmed.length <= MAX_LABEL_LENGTH) {
                    trimmed
                } else {
                    "Gekoppeltes Gerät"
                },
                publicKey = publicKey,
                scopes = HostScopes.ALL,
                createdAt = at,
                lastSeenAt = at,
            ),
        )

        return true
    }

    fun listClients(): List<PairedClient> = clients.list()

    companion object {

        private const val MAX_LABEL_LENGTH = 64

        /**
         * Ob das ein Schlüssel ist, mit dem dieser Host etwas anfangen kann.
         * Öffentlich, weil derselbe Test auch für den Steckbrief der Gegenseite
         * gilt — zwei Fassungen davon wären zwei Gelegenheiten, verschieden
         * streng zu sein.
         */
        fun isUsablePublicKey(publicKey: String): Boolean = runCatching {
            val key = KeyFactory.getInstance("EC").generatePublic(
                X509EncodedKeySpec(Base64.getDecoder().decode(publicKey)),
            )

            // Nur P-256 wird angenommen. Eine andere Kurve wäre kein Angriff,
            // aber ein Fall, den nie jemand getestet hat.
            (key as? ECPublicKey)?.params?.curve?.field?.fieldSize == 256
        }.getOrDefault(false)
    }
}
