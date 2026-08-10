package app.remotedesktop.client.host

import java.io.File
import java.math.BigInteger
import java.nio.file.Files
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.SecureRandom
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.util.Base64
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Die Kopplung am Handy — dieselben Fälle wie `agent.Tests/PairingServiceTests`.
 *
 * Der Client wird hier echt nachgespielt: eigenes Schlüsselpaar, echte
 * Unterschrift im Format der WebCrypto-API. Nur so fällt auf, wenn die
 * Umrechnung nach DER schiefgeht — und die geht bei etwa jeder zweiten
 * Unterschrift schief, wenn man das Vorzeichen vergisst.
 */
class PairingServiceTest {

    private var clock = 1_000_000L

    private val folder: File = Files.createTempDirectory("host-pair").toFile()
    private val clients = ClientStore(File(folder, "clients.json"))
    private val codes = PairingCodes { clock }
    private val challenges = ChallengeStore { clock }
    private val sessions = SessionStore { clock }
    private val service = PairingService(clients, codes, challenges, sessions) { clock }

    private val client = newKeyPair()
    private val publicKey = Base64.getEncoder().encodeToString(client.public.encoded)

    @Test
    fun `koppelt mit dem angezeigten Code`() {
        val result = service.pair(codes.issue(), "Laptop", publicKey, null)

        assertEquals(PairOutcome.OK, result.outcome)
        assertEquals("Laptop", result.client?.label)
        assertEquals(HostScopes.ALL, result.client?.scopes)
    }

    @Test
    fun `ein falscher Code koppelt nicht`() {
        codes.issue()

        assertEquals(PairOutcome.BAD_CODE, service.pair("000000", "Laptop", publicKey, null).outcome)
    }

    @Test
    fun `derselbe Code geht nur einmal`() {
        val code = codes.issue()

        assertEquals(PairOutcome.OK, service.pair(code, "Laptop", publicKey, null).outcome)
        assertEquals(PairOutcome.BAD_CODE, service.pair(code, "Zweitgerät", publicKey, null).outcome)
    }

    @Test
    fun `nach fuenf Minuten ist der Code hin`() {
        val code = codes.issue()
        clock += PairingCodes.LIFETIME_MS + 1

        assertEquals(PairOutcome.BAD_CODE, service.pair(code, "Laptop", publicKey, null).outcome)
    }

    @Test
    fun `nach fuenf Fehlversuchen ist der Code hin`() {
        val code = codes.issue()

        repeat(PairingCodes.MAX_ATTEMPTS) {
            assertEquals(PairOutcome.BAD_CODE, service.pair("000000", "X", publicKey, null).outcome)
        }

        assertEquals(PairOutcome.BAD_CODE, service.pair(code, "Laptop", publicKey, null).outcome)
    }

    @Test
    fun `ein Name muss sein und darf nicht ausufern`() {
        assertEquals(
            PairOutcome.BAD_LABEL,
            service.pair(codes.issue(), "   ", publicKey, null).outcome,
        )

        assertEquals(
            PairOutcome.BAD_LABEL,
            service.pair(codes.issue(), "x".repeat(65), publicKey, null).outcome,
        )
    }

    @Test
    fun `ein unbrauchbarer Schluessel koppelt nicht`() {
        assertEquals(
            PairOutcome.BAD_PUBLIC_KEY,
            service.pair(codes.issue(), "Laptop", "kein Schlüssel", null).outcome,
        )
    }

    /**
     * Die App fragt überall dieselben Rechte an. Ein Handy kann davon weniger —
     * würde es deshalb ablehnen, ließe es sich nie koppeln.
     */
    @Test
    fun `Rechte, die es hier nicht gibt, werden weggelassen statt abgelehnt`() {
        val result = service.pair(
            codes.issue(),
            "Laptop",
            publicKey,
            listOf("screen", "input", "power", "wake"),
        )

        assertEquals(PairOutcome.OK, result.outcome)
        assertEquals(listOf("screen", "input"), result.client?.scopes)
    }

    @Test
    fun `meldet sich mit einer echten Unterschrift an`() {
        val paired = service.pair(codes.issue(), "Laptop", publicKey, null).client!!

        val nonce = service.challenge(paired.id)
        assertNotNull(nonce)

        val result = service.openSession(paired.id, nonce!!, sign(nonce))

        assertEquals(SessionOutcome.OK, result.outcome)
        assertNotNull(result.token)
        assertEquals(paired.id, sessions.find(result.token!!)?.clientId)
    }

    @Test
    fun `eine fremde Unterschrift kommt nicht durch`() {
        val paired = service.pair(codes.issue(), "Laptop", publicKey, null).client!!
        val nonce = service.challenge(paired.id)!!

        val stranger = newKeyPair()

        assertEquals(
            SessionOutcome.BAD_SIGNATURE,
            service.openSession(paired.id, nonce, sign(nonce, stranger)).outcome,
        )
    }

    @Test
    fun `dieselbe Challenge geht nur einmal`() {
        val paired = service.pair(codes.issue(), "Laptop", publicKey, null).client!!
        val nonce = service.challenge(paired.id)!!

        assertEquals(SessionOutcome.OK, service.openSession(paired.id, nonce, sign(nonce)).outcome)
        assertEquals(
            SessionOutcome.BAD_CHALLENGE,
            service.openSession(paired.id, nonce, sign(nonce)).outcome,
        )
    }

    @Test
    fun `eine abgelaufene Challenge geht gar nicht`() {
        val paired = service.pair(codes.issue(), "Laptop", publicKey, null).client!!
        val nonce = service.challenge(paired.id)!!

        clock += ChallengeStore.LIFETIME_MS + 1

        assertEquals(
            SessionOutcome.BAD_CHALLENGE,
            service.openSession(paired.id, nonce, sign(nonce)).outcome,
        )
    }

    @Test
    fun `ein unbekannter Client bekommt keine Challenge`() {
        assertNull(service.challenge("gibtesnicht"))
        assertEquals(
            SessionOutcome.UNKNOWN_CLIENT,
            service.openSession("gibtesnicht", "x", "y").outcome,
        )
    }

    @Test
    fun `der Widerruf wirkt sofort und auf die laufende Sitzung`() {
        val paired = service.pair(codes.issue(), "Laptop", publicKey, null).client!!
        val nonce = service.challenge(paired.id)!!
        val token = service.openSession(paired.id, nonce, sign(nonce)).token!!

        assertNotNull(sessions.find(token))
        assertTrue(service.revoke(paired.id))

        assertNull("Das Token muss mit dem Eintrag sterben", sessions.find(token))
        assertFalse(service.revoke(paired.id))
    }

    @Test
    fun `erneutes Koppeln ersetzt den Eintrag statt ihn zu verdoppeln`() {
        service.pair(codes.issue(), "Laptop", publicKey, null)
        service.pair(codes.issue(), "Laptop neu", publicKey, null)

        assertEquals(1, service.listClients().size)
        assertEquals("Laptop neu", service.listClients().first().label)
    }

    @Test
    fun `gekoppelte Geraete ueberleben einen Neustart`() {
        val paired = service.pair(codes.issue(), "Laptop", publicKey, null).client!!

        val again = ClientStore(File(folder, "clients.json"))

        assertEquals(paired.id, again.find(paired.id)?.id)
        assertEquals(paired.publicKey, again.find(paired.id)?.publicKey)
    }

    // ---- Hilfen -----------------------------------------------------------

    private fun newKeyPair(): KeyPair =
        KeyPairGenerator.getInstance("EC").apply {
            initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
        }.generateKeyPair()

    /**
     * Unterschreibt wie der Browser: r‖s mit fester Länge statt DER. Java kann
     * nur DER, also wird hier zurückgerechnet — der umgekehrte Weg zu dem, den
     * [HostIdentity] geht.
     */
    private fun sign(nonce: String, pair: KeyPair = client): String {
        val der = Signature.getInstance("SHA256withECDSA").run {
            initSign(pair.private)
            update(Base64.getDecoder().decode(nonce))
            sign()
        }

        return Base64.getEncoder().encodeToString(derToConcat(der))
    }

    private fun derToConcat(der: ByteArray): ByteArray {
        var index = 2

        if (der[1].toInt() and 0x80 != 0) {
            index += der[1].toInt() and 0x7F
        }

        fun readInteger(): BigInteger {
            index++
            val length = der[index++].toInt()
            val value = BigInteger(der.copyOfRange(index, index + length))
            index += length
            return value
        }

        val r = readInteger()
        val s = readInteger()

        return pad(r) + pad(s)
    }

    private fun pad(value: BigInteger): ByteArray {
        val bytes = value.toByteArray().dropWhile { it == 0.toByte() }.toByteArray()

        return ByteArray(32 - bytes.size) + bytes
    }
}
