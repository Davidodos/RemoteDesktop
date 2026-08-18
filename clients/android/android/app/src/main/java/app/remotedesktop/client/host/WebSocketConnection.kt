package app.remotedesktop.client.host

import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

/**
 * Eine stehende WebSocket-Verbindung.
 *
 * Blockierend, wie der Rest des Servers: der Thread, der die Verbindung
 * angenommen hat, bleibt bei ihr, bis sie endet. Gesendet wird dagegen aus
 * beliebigen Threads — der Bild-Stream läuft in seiner eigenen Schleife —,
 * deshalb ist das Schreiben gesperrt. Zwei ineinander geschriebene Rahmen wären
 * kein halbes Bild, sondern eine kaputte Verbindung.
 *
 * @param onClosed Was beim Schließen sonst noch geschehen muss — in der Praxis:
 *   den TCP-Socket zumachen. Das ist keine Aufräumarbeit, sondern die einzige
 *   Art, den lesenden Thread aus seinem `read()` zu holen; ohne Zeitlimit
 *   wartet er dort sonst, bis die App endet. Siehe `HttpServer.openSocket`.
 */
class WebSocketConnection(
    private val input: InputStream,
    private val output: OutputStream,
    private val onClosed: () -> Unit = {},
) {

    private val writeGate = Any()

    @Volatile
    private var closed = false

    val isOpen: Boolean get() = !closed

    fun sendText(text: String) = send(WebSocketFrames.OPCODE_TEXT, text.toByteArray(Charsets.UTF_8))

    fun sendBinary(payload: ByteArray) = send(WebSocketFrames.OPCODE_BINARY, payload)

    /**
     * Hört zu, bis die Gegenseite auflegt oder etwas schiefgeht.
     *
     * Fehler beenden die Verbindung und werden nicht gemeldet: eine
     * Fernsteuerung, die beim Wechsel von WLAN auf Mobilfunk eine Meldung
     * aufwirft, hat für den Nutzer nichts erklärt. Der Client verbindet von
     * allein neu — dafür ist `inputChannel.ts` gebaut.
     */
    fun listen(onText: (String) -> Unit, onBinary: (ByteArray) -> Unit = {}) {
        try {
            while (!closed) {
                val frame = WebSocketFrames.read(input) { payload ->
                    send(WebSocketFrames.OPCODE_PONG, payload)
                } ?: break

                when (frame.opcode) {
                    WebSocketFrames.OPCODE_TEXT ->
                        onText(String(frame.payload, Charsets.UTF_8))

                    WebSocketFrames.OPCODE_BINARY -> onBinary(frame.payload)

                    else -> Unit
                }
            }
        } catch (broken: IOException) {
            // Ende der Verbindung, aus welchem Grund auch immer.
        } finally {
            close()
        }
    }

    fun close() {
        if (closed) {
            return
        }

        closed = true

        runCatching { send(WebSocketFrames.OPCODE_CLOSE, ByteArray(0)) }
        onClosed()
    }

    private fun send(opcode: Int, payload: ByteArray) {
        if (closed && opcode != WebSocketFrames.OPCODE_CLOSE) {
            return
        }

        var broke = false

        synchronized(writeGate) {
            try {
                output.write(WebSocketFrames.encode(opcode, payload))
                output.flush()
            } catch (broken: IOException) {
                closed = true
                broke = true
            }
        }

        // **Außerhalb des Schlosses, und nicht nur ein Flag.** Vorher wurde hier
        // bloß `closed` gesetzt: der lesende Thread hing weiter in seinem
        // `read()`, weil den niemand unterbrach, und die Verbindung zählte für
        // immer als offen. Ein Schreibfehler heißt aber, dass die Gegenseite weg
        // ist — dann gehört der Socket zu, und das erledigt `onClosed`.
        if (broke && opcode != WebSocketFrames.OPCODE_CLOSE) {
            onClosed()
        }
    }
}
