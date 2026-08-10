package app.remotedesktop.client.host

import java.io.ByteArrayOutputStream
import java.math.BigInteger
import java.util.Calendar
import java.util.Date
import java.util.Locale
import java.util.TimeZone

/**
 * Der kleinste DER-Schreiber, der für ein X.509-Zertifikat reicht.
 *
 * **Warum von Hand und nicht mit BouncyCastle.** Android bringt keine
 * Möglichkeit mit, ein Zertifikat auszustellen — `KeyPairGenerator` liefert
 * Schlüssel, aber niemand unterschreibt sie. Die übliche Antwort darauf ist
 * BouncyCastle, und die kostet rund acht Megabyte im APK. Für genau zwei
 * Zertifikate mit festem Aufbau ist das ein schlechter Tausch: hier stehen
 * dreihundert Zeilen, die ausschließlich das können, was gebraucht wird, und
 * die ein Test gegen `CertificateFactory` und einen echten TLS-Handschlag
 * prüft. Entweder es ist richtig kodiert, oder gar nichts läuft.
 *
 * Alles hier ist DER, nicht BER: bestimmte Länge, kürzestmögliche Kodierung,
 * keine Wahlfreiheit. Genau deshalb lässt es sich so knapp schreiben.
 */
internal object Der {

    /** Ein Element aus Kennung, Länge und Inhalt. */
    fun tagged(tag: Int, content: ByteArray): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(tag)
        writeLength(out, content.size)
        out.write(content)
        return out.toByteArray()
    }

    fun sequence(vararg parts: ByteArray): ByteArray = tagged(0x30, join(parts))

    fun set(vararg parts: ByteArray): ByteArray = tagged(0x31, join(parts))

    /**
     * Ganzzahl im Zweierkomplement. `BigInteger.toByteArray` liefert bereits
     * genau das Format, das DER verlangt — auch das führende Nullbyte, mit dem
     * eine große positive Zahl von einer negativen unterschieden wird.
     */
    fun integer(value: BigInteger): ByteArray = tagged(0x02, value.toByteArray())

    fun integer(value: Int): ByteArray = integer(BigInteger.valueOf(value.toLong()))

    fun boolean(value: Boolean): ByteArray =
        tagged(0x01, byteArrayOf(if (value) 0xFF.toByte() else 0x00))

    /**
     * Bitfolge. `unused` sagt, wie viele Bits im letzten Byte nicht zählen —
     * bei einem `keyUsage` mit drei gesetzten Bits sind das fünf.
     */
    fun bitString(content: ByteArray, unused: Int = 0): ByteArray =
        tagged(0x03, byteArrayOf(unused.toByte()) + content)

    fun octetString(content: ByteArray): ByteArray = tagged(0x04, content)

    fun nullValue(): ByteArray = byteArrayOf(0x05, 0x00)

    fun utf8(text: String): ByteArray = tagged(0x0C, text.toByteArray(Charsets.UTF_8))

    fun ia5(text: String): ByteArray = tagged(0x16, text.toByteArray(Charsets.US_ASCII))

    /**
     * Ein Objektbezeichner wie `1.2.840.10045.4.3.2`.
     *
     * Die ersten beiden Zahlen teilen sich das erste Byte (`40 * a + b`), alle
     * weiteren stehen in Siebenbit-Gruppen mit gesetztem Fortsetzungsbit.
     */
    fun oid(dotted: String): ByteArray {
        val parts = dotted.split('.').map { it.toLong() }

        require(parts.size >= 2) { "Ein OID braucht mindestens zwei Zahlen: $dotted" }

        val out = ByteArrayOutputStream()
        out.write((parts[0] * 40 + parts[1]).toInt())

        for (index in 2 until parts.size) {
            writeBase128(out, parts[index])
        }

        return tagged(0x06, out.toByteArray())
    }

    /**
     * Zeitangabe als `UTCTime`.
     *
     * X.509 schreibt für Jahre bis 2049 dieses Format vor und erst danach
     * `GeneralizedTime`. Die CA hier lebt zehn Jahre — der Fall tritt also erst
     * nach 2039 ein, und dann steht diese App längst nicht mehr auf einem
     * Gerät. Ein Zertifikat mit dem falschen Zeittyp lehnt jeder Client ab,
     * deshalb steht die Grenze hier trotzdem als Prüfung.
     */
    fun utcTime(date: Date): ByteArray {
        val calendar = Calendar.getInstance(TimeZone.getTimeZone("UTC"), Locale.ROOT)
        calendar.time = date

        val year = calendar.get(Calendar.YEAR)

        require(year in 1950..2049) { "UTCTime deckt nur 1950–2049 ab, nicht $year" }

        val text = String.format(
            Locale.ROOT,
            "%02d%02d%02d%02d%02d%02dZ",
            year % 100,
            calendar.get(Calendar.MONTH) + 1,
            calendar.get(Calendar.DAY_OF_MONTH),
            calendar.get(Calendar.HOUR_OF_DAY),
            calendar.get(Calendar.MINUTE),
            calendar.get(Calendar.SECOND),
        )

        return tagged(0x17, text.toByteArray(Charsets.US_ASCII))
    }

    /** Ein Element mit ausdrücklicher Nummer, das seinen Inhalt umschließt. */
    fun explicit(number: Int, content: ByteArray): ByteArray = tagged(0xA0 or number, content)

    /** Ein Element mit ersetzter Kennung — der Inhalt bleibt, wie er ist. */
    fun implicit(number: Int, content: ByteArray): ByteArray = tagged(0x80 or number, content)

    /** Wie {@link implicit}, aber für zusammengesetzten Inhalt. */
    fun implicitSequence(number: Int, content: ByteArray): ByteArray =
        tagged(0xA0 or number, content)

    private fun join(parts: Array<out ByteArray>): ByteArray {
        val out = ByteArrayOutputStream()
        parts.forEach(out::write)
        return out.toByteArray()
    }

    /**
     * Kurze Längen stehen in einem Byte, längere zuerst mit der Anzahl der
     * Längenbytes. Die kürzestmögliche Form ist Pflicht — eine Länge von 5 in
     * zwei Bytes wäre BER und kein DER, und manche Prüfer weisen das zurück.
     */
    private fun writeLength(out: ByteArrayOutputStream, length: Int) {
        if (length < 0x80) {
            out.write(length)
            return
        }

        val bytes = BigInteger.valueOf(length.toLong()).toByteArray().dropWhile { it == 0.toByte() }

        out.write(0x80 or bytes.size)
        bytes.forEach { out.write(it.toInt()) }
    }

    private fun writeBase128(out: ByteArrayOutputStream, value: Long) {
        var shift = 63 - java.lang.Long.numberOfLeadingZeros(value or 1)
        shift -= shift % 7

        while (shift > 0) {
            out.write((0x80 or ((value shr shift) and 0x7F).toInt()))
            shift -= 7
        }

        out.write((value and 0x7F).toInt())
    }
}
