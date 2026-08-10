package app.remotedesktop.client.host

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import kotlin.random.Random
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test

/**
 * Das Rahmenformat von RFC 6455.
 *
 * Die drei Stellen, an denen eine selbst geschriebene Fassung erfahrungsgemäß
 * scheitert: die erweiterte Länge, die Maske und zerstückelte Nachrichten. Alle
 * drei stehen hier — jede einzeln, und keine davon fällt beim Ausprobieren am
 * Gerät auf, weil ein falscher Rahmen die Verbindung wortlos beendet.
 */
class WebSocketFramesTest {

    @Test
    fun `beantwortet den Handschlag wie im Standard`() {
        // Das Beispiel aus RFC 6455, Abschnitt 1.3.
        assertEquals(
            "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=",
            WebSocketFrames.accept("dGhlIHNhbXBsZSBub25jZQ=="),
        )
    }

    @Test
    fun `liest eine kurze maskierte Textnachricht`() {
        val frame = WebSocketFrames.read(client(WebSocketFrames.OPCODE_TEXT, "hallo".toByteArray()))

        assertEquals(WebSocketFrames.OPCODE_TEXT, frame?.opcode)
        assertEquals("hallo", String(frame!!.payload))
    }

    /** 126 bis 65535 Bytes stehen in zwei zusätzlichen Bytes. */
    @Test
    fun `liest die mittlere Laengenform`() {
        val payload = Random(1).nextBytes(1000)
        val frame = WebSocketFrames.read(client(WebSocketFrames.OPCODE_BINARY, payload))

        assertArrayEquals(payload, frame?.payload)
    }

    /** Darüber acht Bytes. Ein Bild reißt diese Grenze mühelos. */
    @Test
    fun `liest die lange Laengenform`() {
        val payload = Random(2).nextBytes(70_000)
        val frame = WebSocketFrames.read(client(WebSocketFrames.OPCODE_BINARY, payload))

        assertArrayEquals(payload, frame?.payload)
    }

    @Test
    fun `setzt zerstueckelte Nachrichten zusammen`() {
        val out = ByteArrayOutputStream()

        out.write(maskedFrame(WebSocketFrames.OPCODE_TEXT, "hal".toByteArray(), fin = false))
        out.write(maskedFrame(WebSocketFrames.OPCODE_CONTINUATION, "lo".toByteArray(), fin = true))

        val frame = WebSocketFrames.read(ByteArrayInputStream(out.toByteArray()))

        assertEquals("hallo", String(frame!!.payload))
        assertEquals(WebSocketFrames.OPCODE_TEXT, frame.opcode)
    }

    @Test
    fun `beantwortet einen Ping, auch zwischen Fortsetzungen`() {
        val out = ByteArrayOutputStream()

        out.write(maskedFrame(WebSocketFrames.OPCODE_TEXT, "hal".toByteArray(), fin = false))
        out.write(maskedFrame(WebSocketFrames.OPCODE_PING, "!".toByteArray(), fin = true))
        out.write(maskedFrame(WebSocketFrames.OPCODE_CONTINUATION, "lo".toByteArray(), fin = true))

        val pings = mutableListOf<String>()

        val frame = WebSocketFrames.read(ByteArrayInputStream(out.toByteArray())) {
            pings.add(String(it))
        }

        assertEquals(listOf("!"), pings)
        assertEquals("hallo", String(frame!!.payload))
    }

    @Test
    fun `ein Close beendet die Nachrichtenfolge`() {
        assertNull(WebSocketFrames.read(client(WebSocketFrames.OPCODE_CLOSE, ByteArray(0))))
    }

    /**
     * Ein Client, der nicht maskiert, spricht das Protokoll nicht. Ihn
     * durchzulassen hieße, eine untergeschobene HTTP-Anfrage als Nachricht zu
     * behandeln — genau der Fall, für den es die Maske gibt.
     */
    @Test
    fun `ein unmaskierter Rahmen wird abgewiesen`() {
        val unmasked = byteArrayOf(0x81.toByte(), 0x02, 'h'.code.toByte(), 'i'.code.toByte())

        assertThrows(IOException::class.java) {
            WebSocketFrames.read(ByteArrayInputStream(unmasked))
        }
    }

    @Test
    fun `ein abgeschnittener Rahmen wirft, statt Unsinn zu liefern`() {
        val full = maskedFrame(WebSocketFrames.OPCODE_TEXT, "hallo".toByteArray(), fin = true)

        assertThrows(EOFException::class.java) {
            WebSocketFrames.read(ByteArrayInputStream(full.copyOfRange(0, full.size - 2)))
        }
    }

    /** Ein Server maskiert nicht — sonst versteht ihn kein Browser. */
    @Test
    fun `schreibt ohne Maske und mit der kuerzesten Laengenform`() {
        assertArrayEquals(
            byteArrayOf(0x81.toByte(), 0x02, 'h'.code.toByte(), 'i'.code.toByte()),
            WebSocketFrames.encode(WebSocketFrames.OPCODE_TEXT, "hi".toByteArray()),
        )

        val medium = WebSocketFrames.encode(WebSocketFrames.OPCODE_BINARY, ByteArray(300))

        assertEquals(0x82.toByte(), medium[0])
        assertEquals(126.toByte(), medium[1])
        assertEquals(300, ((medium[2].toInt() and 0xFF) shl 8) or (medium[3].toInt() and 0xFF))
    }

    @Test
    fun `Geschriebenes laesst sich maskiert wieder lesen`() {
        val payload = Random(3).nextBytes(5000)
        val written = WebSocketFrames.encode(WebSocketFrames.OPCODE_BINARY, payload)

        // Der Rahmen des Servers, wieder maskiert wie vom Client: dieselbe
        // Nutzlast muss herauskommen.
        val body = written.copyOfRange(written.size - payload.size, written.size)
        val frame = WebSocketFrames.read(client(WebSocketFrames.OPCODE_BINARY, body))

        assertArrayEquals(payload, frame?.payload)
    }

    // ---- Hilfen -----------------------------------------------------------

    private fun client(opcode: Int, payload: ByteArray) =
        ByteArrayInputStream(maskedFrame(opcode, payload, fin = true))

    /** Baut einen Rahmen so, wie ein Browser ihn schickt: immer maskiert. */
    private fun maskedFrame(opcode: Int, payload: ByteArray, fin: Boolean): ByteArray {
        val out = ByteArrayOutputStream()

        out.write((if (fin) 0x80 else 0x00) or opcode)

        when {
            payload.size < 126 -> out.write(0x80 or payload.size)

            payload.size <= 0xFFFF -> {
                out.write(0x80 or 126)
                out.write((payload.size shr 8) and 0xFF)
                out.write(payload.size and 0xFF)
            }

            else -> {
                out.write(0x80 or 127)

                for (shift in 56 downTo 0 step 8) {
                    out.write(((payload.size.toLong() shr shift) and 0xFF).toInt())
                }
            }
        }

        val mask = byteArrayOf(0x12, 0x34, 0x56, 0x78)

        out.write(mask)

        payload.forEachIndexed { index, byte ->
            out.write(byte.toInt() xor mask[index % 4].toInt())
        }

        return out.toByteArray()
    }
}
