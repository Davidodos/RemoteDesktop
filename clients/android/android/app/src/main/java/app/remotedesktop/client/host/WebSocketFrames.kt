package app.remotedesktop.client.host

import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest
import java.util.Base64

/**
 * Das Rahmenformat von RFC 6455, so weit wie hier gebraucht.
 *
 * Kein zweiter Grund für eine Bibliothek: das Format ist zwei Bildschirmseiten
 * lang und vollständig festgelegt. Was daran schiefgehen kann — die Maske
 * vergessen, die erweiterte Länge falsch lesen, Fortsetzungsrahmen nicht
 * zusammensetzen —, steht unten je einmal und ist je einmal geprüft.
 *
 * **Die Maske ist keine Verschlüsselung.** Sie steht im Rahmen daneben. Sie
 * existiert, damit ein Zwischenspeicher unterwegs den Inhalt nicht für eine
 * HTTP-Anfrage hält. Ein Client muss maskieren, ein Server darf es nicht.
 */
internal object WebSocketFrames {

    const val OPCODE_CONTINUATION = 0x0
    const val OPCODE_TEXT = 0x1
    const val OPCODE_BINARY = 0x2
    const val OPCODE_CLOSE = 0x8
    const val OPCODE_PING = 0x9
    const val OPCODE_PONG = 0xA

    /** Die feste Zeichenfolge aus RFC 6455, mit der der Handschlag beantwortet wird. */
    private const val MAGIC = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

    /**
     * Obergrenze für eine Nachricht vom Client. Der schickt hier nur kurze
     * Steuerbefehle — alles Größere ist ein Fehler oder ein Versuch, den
     * Speicher zu füllen.
     */
    private const val MAX_MESSAGE_BYTES = 256 * 1024

    data class Frame(val opcode: Int, val payload: ByteArray, val fin: Boolean)

    /** Die Antwort auf `Sec-WebSocket-Key`. */
    fun accept(key: String): String =
        Base64.getEncoder().encodeToString(
            MessageDigest.getInstance("SHA-1").digest((key.trim() + MAGIC).toByteArray()),
        )

    /**
     * Liest einen Rahmen. Fortsetzungsrahmen werden **nicht** hier
     * zusammengesetzt — das tut [read], weil erst dort feststeht, wie viel
     * insgesamt zusammenkommen darf.
     */
    fun readFrame(input: InputStream): Frame {
        val first = input.readOrThrow()
        val second = input.readOrThrow()

        val fin = first and 0x80 != 0
        val opcode = first and 0x0F
        val masked = second and 0x80 != 0

        var length = (second and 0x7F).toLong()

        if (length == 126L) {
            length = ((input.readOrThrow().toLong() shl 8) or input.readOrThrow().toLong())
        } else if (length == 127L) {
            length = 0

            repeat(8) { length = (length shl 8) or input.readOrThrow().toLong() }
        }

        if (length > MAX_MESSAGE_BYTES) {
            throw IOException("Rahmen mit $length Bytes ist zu groß.")
        }

        // Ein Client, der nicht maskiert, verstößt gegen die Festlegung. Das
        // ist kein Grund für Nachsicht: entweder spricht die Gegenseite das
        // Protokoll, oder sie hat hier nichts zu suchen.
        if (!masked) {
            throw IOException("Rahmen vom Client ohne Maske.")
        }

        val mask = ByteArray(4).also { input.readFully(it) }
        val payload = ByteArray(length.toInt()).also { input.readFully(it) }

        for (index in payload.indices) {
            payload[index] = (payload[index].toInt() xor mask[index % 4].toInt()).toByte()
        }

        return Frame(opcode, payload, fin)
    }

    /**
     * Liest eine vollständige Nachricht und setzt Fortsetzungsrahmen zusammen.
     *
     * @return `null`, wenn die Gegenseite auflegt.
     */
    fun read(input: InputStream, onPing: (ByteArray) -> Unit = {}): Frame? {
        var opcode = -1
        val buffer = ByteArrayOutputStream()

        while (true) {
            val frame = readFrame(input)

            when (frame.opcode) {
                OPCODE_CLOSE -> return null

                // Ein Ping wird sofort beantwortet, auch mitten in einer
                // zerstückelten Nachricht: er darf zwischen Fortsetzungsrahmen
                // stehen, und wer ihn dort verschluckt, fliegt nach dem
                // Zeitlimit der Gegenseite heraus.
                OPCODE_PING -> onPing(frame.payload)

                OPCODE_PONG -> Unit

                OPCODE_CONTINUATION -> {
                    if (opcode < 0) {
                        throw IOException("Fortsetzung ohne Anfang.")
                    }

                    buffer.write(frame.payload)

                    if (frame.fin) {
                        return Frame(opcode, buffer.toByteArray(), true)
                    }
                }

                else -> {
                    if (frame.fin) {
                        return frame
                    }

                    opcode = frame.opcode
                    buffer.reset()
                    buffer.write(frame.payload)
                }
            }

            if (buffer.size() > MAX_MESSAGE_BYTES) {
                throw IOException("Zusammengesetzte Nachricht ist zu groß.")
            }
        }
    }

    /** Baut einen Rahmen zum Senden — ohne Maske, wie es einem Server zusteht. */
    fun encode(opcode: Int, payload: ByteArray): ByteArray {
        val out = ByteArrayOutputStream(payload.size + 10)

        out.write(0x80 or opcode)

        when {
            payload.size < 126 -> out.write(payload.size)

            payload.size <= 0xFFFF -> {
                out.write(126)
                out.write((payload.size shr 8) and 0xFF)
                out.write(payload.size and 0xFF)
            }

            else -> {
                out.write(127)

                for (shift in 56 downTo 0 step 8) {
                    out.write(((payload.size.toLong() shr shift) and 0xFF).toInt())
                }
            }
        }

        out.write(payload)

        return out.toByteArray()
    }

    private fun InputStream.readOrThrow(): Int {
        val next = read()

        if (next < 0) {
            throw EOFException("Die Gegenseite hat aufgelegt.")
        }

        return next
    }

    private fun InputStream.readFully(target: ByteArray) {
        var read = 0

        while (read < target.size) {
            val step = read(target, read, target.size - read)

            if (step < 0) {
                throw EOFException("Der Rahmen endete zu früh.")
            }

            read += step
        }
    }
}
