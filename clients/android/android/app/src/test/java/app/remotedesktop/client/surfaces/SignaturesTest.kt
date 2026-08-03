package app.remotedesktop.client.surfaces

import java.security.KeyPairGenerator
import java.security.SecureRandom
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.util.Base64
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Die eine Zusage, die hier zählt: **was der native Teil unterschreibt, prüft
 * der Agent auch.**
 *
 * Er prüft in IEEE P1363 (r und s hintereinander, je 32 Byte), Java liefert DER.
 * Der Fehler wäre lautlos — eine Unterschrift, die zwar entsteht, aber immer
 * abgelehnt wird — und er käme erst auf dem Gerät heraus, wo er wie ein Angriff
 * aussieht. Genau dieselbe Falle steht in den Notizen zu Phase 14.
 *
 * Geprüft wird gegen den P1363-Prüfer der JVM, weil .NET dasselbe Format
 * verlangt: `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`.
 */
class SignaturesTest {

    private val keys = KeyPairGenerator.getInstance("EC")
        .apply { initialize(ECGenParameterSpec("secp256r1")) }
        .generateKeyPair()

    private val privateKey: String = Base64.getEncoder().encodeToString(keys.private.encoded)

    private fun nonce(): String {
        val bytes = ByteArray(32)
        SecureRandom().nextBytes(bytes)

        return Base64.getEncoder().encodeToString(bytes)
    }

    @Test
    fun `die Unterschrift wird im Format des Agents angenommen`() {
        val challenge = nonce()

        val verifier = Signature.getInstance("SHA256withECDSAinP1363Format")
        verifier.initVerify(keys.public)
        verifier.update(Base64.getDecoder().decode(challenge))

        val signature = Base64.getDecoder().decode(Signatures.sign(privateKey, challenge))

        assertTrue(verifier.verify(signature))
    }

    @Test
    fun `eine veraenderte Challenge faellt durch`() {
        val challenge = nonce()
        val signature = Base64.getDecoder().decode(Signatures.sign(privateKey, challenge))

        val verifier = Signature.getInstance("SHA256withECDSAinP1363Format")
        verifier.initVerify(keys.public)
        verifier.update(Base64.getDecoder().decode(nonce()))

        assertFalse(verifier.verify(signature))
    }

    @Test
    fun `die Unterschrift ist immer 64 Byte lang`() {
        // DER ist mal 70, mal 71, mal 72 Byte lang — je nachdem, ob r und s ein
        // gesetztes oberstes Bit haben. P1363 kennt das nicht: feste Länge,
        // links mit Nullen aufgefüllt. Zwanzig Läufe treffen beide Fälle.
        repeat(20) {
            val signature = Base64.getDecoder().decode(Signatures.sign(privateKey, nonce()))

            assertEquals(64, signature.size)
        }
    }

    @Test
    fun `die fuehrende Null aus DER faellt weg`() {
        // Von Hand gebaut: r hat das oberste Bit gesetzt und trägt deshalb in
        // DER eine 0x00 davor, s ist kurz und muss links aufgefüllt werden.
        val r = ByteArray(32) { 0xff.toByte() }
        val der = byteArrayOf(0x30, 0x27, 0x02, 0x21, 0x00) +
            r +
            byteArrayOf(0x02, 0x02, 0x01, 0x02)

        val p1363 = Signatures.toP1363(der)

        assertEquals(64, p1363.size)
        assertEquals(0xff.toByte(), p1363[0])
        assertEquals(0x01.toByte(), p1363[62])
        assertEquals(0x02.toByte(), p1363[63])
        assertEquals(0x00.toByte(), p1363[61])
    }

    @Test(expected = IllegalArgumentException::class)
    fun `etwas anderes als DER wird abgelehnt`() {
        Signatures.toP1363(ByteArray(64) { 0x41 })
    }
}
