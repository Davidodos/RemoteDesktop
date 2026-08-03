package app.remotedesktop.client.surfaces

import java.security.KeyFactory
import java.security.Signature
import java.security.spec.PKCS8EncodedKeySpec
import java.util.Base64

/**
 * Unterschreibt die Challenge des Agents — dieselbe Anmeldung, die sonst die
 * WebView erledigt, nur ohne WebView.
 *
 * Der wunde Punkt ist das **Format**: der Agent prüft mit
 * `ECDsa.VerifyData(..., DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`,
 * also r und s hintereinander zu je 32 Byte. Java liefert stattdessen DER — eine
 * Sequenz aus zwei Ganzzahlen mit Längenangaben und, je nach Vorzeichen, einer
 * führenden Null. Wer das übersieht, bekommt eine Prüfung, die immer
 * fehlschlägt, und das sieht am anderen Ende aus wie ein Angriff.
 *
 * Umgerechnet wird selbst, statt `SHA256withECDSAinP1363Format` zu verlangen:
 * diesen Algorithmusnamen kennt nicht jede Android-Fassung ab API 26, und ein
 * Fehlschlag käme erst auf dem Gerät heraus.
 */
object Signatures {

    /** P-256: r und s sind je 32 Byte lang, mit führenden Nullen aufgefüllt. */
    private const val COORDINATE_BYTES = 32

    private const val SEQUENCE = 0x30.toByte()
    private const val INTEGER = 0x02.toByte()

    /**
     * @param privateKeyPkcs8 der private Geräteschlüssel, Base64 im PKCS-8-Format
     *   — genau so, wie ihn `lib/clientKey.ts` abgelegt hat
     * @param nonce die Challenge des Agents, Base64
     * @return die Unterschrift als Base64, r und s hintereinander
     */
    fun sign(privateKeyPkcs8: String, nonce: String): String {
        val key = KeyFactory.getInstance("EC")
            .generatePrivate(PKCS8EncodedKeySpec(decode(privateKeyPkcs8)))

        val signer = Signature.getInstance("SHA256withECDSA")
        signer.initSign(key)
        signer.update(decode(nonce))

        return Base64.getEncoder().encodeToString(toP1363(signer.sign()))
    }

    /** Aus `SEQUENCE { INTEGER r, INTEGER s }` werden 64 feste Bytes. */
    internal fun toP1363(der: ByteArray): ByteArray {
        require(der.size >= 8 && der[0] == SEQUENCE) { "Keine DER-Sequenz." }

        val r = readInteger(der, skipLength(der, 1))
        val s = readInteger(der, r.second)

        return padded(r.first) + padded(s.first)
    }

    /**
     * Über die Länge der Sequenz hinweg. Bei P-256 ist sie immer kurz kodiert
     * (unter 128 Byte); die lange Form steht trotzdem hier, weil eine
     * Fehlannahme sonst als falsche Unterschrift durchginge statt als Fehler.
     */
    private fun skipLength(der: ByteArray, at: Int): Int {
        val first = der[at].toInt() and 0xff

        return if (first < 0x80) at + 1 else at + 1 + (first and 0x7f)
    }

    /** Liefert den Inhalt der Ganzzahl und die Stelle dahinter. */
    private fun readInteger(der: ByteArray, at: Int): Pair<ByteArray, Int> {
        require(at + 1 < der.size && der[at] == INTEGER) { "Ganzzahl erwartet." }

        val length = der[at + 1].toInt() and 0xff
        val start = at + 2

        require(start + length <= der.size) { "Länge zeigt über das Ende hinaus." }

        return Pair(der.copyOfRange(start, start + length), start + length)
    }

    /**
     * DER schreibt eine führende Null, wenn das oberste Bit gesetzt ist —
     * sonst wäre die Zahl negativ. P1363 kennt kein Vorzeichen und will genau
     * 32 Byte, also fällt die Null weg und links wird aufgefüllt.
     */
    private fun padded(value: ByteArray): ByteArray {
        val trimmed = value.dropWhile { it == 0.toByte() }.toByteArray()

        require(trimmed.size <= COORDINATE_BYTES) { "Zahl passt nicht in 32 Byte." }

        val out = ByteArray(COORDINATE_BYTES)
        trimmed.copyInto(out, COORDINATE_BYTES - trimmed.size)

        return out
    }

    /**
     * `java.util.Base64` und nicht `android.util.Base64`: Ersteres gibt es seit
     * API 26 (das ist die Untergrenze dieser App) und es läuft auch in einem
     * gewöhnlichen JVM-Testlauf. Letzteres ist im Test eine leere Hülle.
     */
    private fun decode(value: String): ByteArray = Base64.getDecoder().decode(value)
}
