package app.remotedesktop.client.host

import java.io.ByteArrayOutputStream
import java.io.PipedInputStream
import java.io.PipedOutputStream
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Der Bild-Stream — gegen dasselbe Protokoll geprüft, das der Windows-Agent
 * spricht.
 *
 * Die App unterscheidet nicht, wer am anderen Ende sitzt. Also muss auch das
 * hier stimmen: `meta` zuerst, dann Binärnachrichten mit dem Achtbyte-Kopf,
 * dazwischen `stats`. Ein abweichendes Feld fällt am Gerät als schwarzes Bild
 * auf und sonst gar nicht.
 */
class ScreenStreamTest {

    /** Eine Quelle, die abgezählt viele erfundene Bilder liefert. */
    private class FakeSource(
        override val width: Int,
        override val height: Int,
        private val frames: Int,
        private val missing: Int = 0,
    ) : FrameSource {

        /** Wie oft überhaupt gefragt wurde — steigt auch, wenn nichts kommt. */
        @Volatile
        var calls = 0

        var delivered = 0
        var lastQuality = 0

        @Volatile
        var closed = false

        /** Ob die Aufnahme noch läuft — siehe [FrameSource.isRunning]. */
        @Volatile
        var running = true

        override val isRunning: Boolean get() = running

        override fun next(quality: Int): CapturedFrame? {
            calls++
            lastQuality = quality

            if (delivered < missing) {
                delivered++
                return null
            }

            if (delivered - missing >= frames) {
                return null
            }

            delivered++

            return CapturedFrame(ByteArray(120) { it.toByte() }, width, height)
        }

        override fun close() {
            closed = true
        }
    }

    /** Ein Socket, der nur mitschreibt, was hinausginge. */
    private fun collector(): Pair<WebSocketConnection, MutableList<Any>> {
        val messages = mutableListOf<Any>()

        val input = PipedInputStream().also { PipedOutputStream(it) }

        val output = object : ByteArrayOutputStream() {
            @Synchronized
            override fun write(payload: ByteArray) {
                // Der Rahmen wird hier wieder aufgetrennt: der Test will die
                // Nachricht sehen, nicht ihre Verpackung.
                val body = strip(payload)

                when (payload[0].toInt() and 0x0F) {
                    WebSocketFrames.OPCODE_TEXT -> messages.add(String(body, Charsets.UTF_8))
                    WebSocketFrames.OPCODE_BINARY -> messages.add(body)
                    else -> Unit
                }
            }
        }

        return WebSocketConnection(input, output) to messages
    }

    private fun strip(frame: ByteArray): ByteArray {
        var offset = 2
        val length = frame[1].toInt() and 0x7F

        if (length == 126) {
            offset += 2
        } else if (length == 127) {
            offset += 8
        }

        return frame.copyOfRange(offset, frame.size)
    }

    @Test(timeout = 10_000)
    fun `meldet sich mit meta und schickt dann Bilder`() {
        val source = FakeSource(640, 1424, frames = 3)
        val (socket, messages) = collector()

        val stream = ScreenStream(source, 640, 1424, fps = 60, sleep = {})

        // Nach drei Bildern liefert die Quelle nichts mehr; der Socket wird
        // geschlossen, sobald sie leer läuft.
        Thread {
            while (source.calls <= 3) {
                Thread.sleep(1)
            }

            socket.close()
        }.start()

        stream.run(socket)

        val meta = JSONObject(messages.first() as String)

        assertEquals("meta", meta.getString("t"))
        assertEquals(0, meta.getInt("monitor"))
        assertEquals(640, meta.getInt("width"))
        assertEquals(1424, meta.getInt("height"))
        assertEquals(1, meta.getInt("count"))

        val binary = messages.filterIsInstance<ByteArray>()

        assertTrue("Es müssen Bilder angekommen sein", binary.isNotEmpty())
        assertEquals(ScreenStream.HEADER_BYTES + 120, binary.first().size)
    }

    @Test(timeout = 10_000)
    fun `der Kopf beschreibt die volle Flaeche`() {
        val source = FakeSource(640, 1424, frames = 1)
        val (socket, messages) = collector()

        Thread {
            while (source.calls <= 1) {
                Thread.sleep(1)
            }

            socket.close()
        }.start()

        ScreenStream(source, 640, 1424, fps = 60, sleep = {}).run(socket)

        val header = messages.filterIsInstance<ByteArray>().first()

        // Position 0/0, Größe wie angekündigt — kleine Ausschnitte gibt es hier
        // nicht, weil Android nicht meldet, was sich geändert hat.
        assertEquals(0, read(header, 0))
        assertEquals(0, read(header, 2))
        assertEquals(640, read(header, 4))
        assertEquals(1424, read(header, 6))
    }

    /**
     * **Der Befund dahinter (17.08.2026):** ein Handy, auf dem sich nichts
     * bewegte, meldete nach einer Sekunde „Bildschirm nicht verfügbar". Android
     * liefert aber nur bei Änderung ein Bild — ein ruhiger Bildschirm liefert
     * minutenlang nichts, und das ist kein Fehler, sondern ein Bildschirm, auf
     * dem sich nichts tut.
     */
    @Test(timeout = 10_000)
    fun `ein ruhiger Bildschirm ist nicht dasselbe wie ein fehlender`() {
        val source = FakeSource(640, 1424, frames = 0)
        val (socket, messages) = collector()

        Thread {
            while (source.calls <= 50) {
                Thread.sleep(1)
            }

            socket.close()
        }.start()

        ScreenStream(source, 640, 1424, fps = 10, sleep = {}).run(socket)

        val texts = messages.filterIsInstance<String>().map { JSONObject(it).getString("t") }

        assertTrue("„unavailable\" steht da, obwohl die Aufnahme läuft", !texts.contains("unavailable"))
    }

    /**
     * Die zweite Hälfte desselben Befunds: die Kennzahlen standen hinter dem
     * Bild. Bewegte sich eine Weile nichts, verstummte der Socket ganz — und
     * nach sechs Sekunden Stille hielt die Gegenseite die Verbindung für tot
     * und baute sie neu auf.
     */
    @Test(timeout = 10_000)
    fun `auch ohne Bild verstummt der Socket nicht`() {
        val source = FakeSource(640, 1424, frames = 0)
        val (socket, messages) = collector()
        val uhr = java.util.concurrent.atomic.AtomicLong(0)

        Thread {
            while (source.calls <= 30) {
                Thread.sleep(1)
            }

            socket.close()
        }.start()

        ScreenStream(
            source, 640, 1424, fps = 10, now = { uhr.addAndGet(200) }, sleep = {},
        ).run(socket)

        val texts = messages.filterIsInstance<String>().map { JSONObject(it).getString("t") }

        assertTrue("keine Kennzahlen bei stehendem Bild", texts.contains("stats"))
    }

    /** Ist die Aufnahme wirklich weg, wird es gesagt — und die Rückkehr auch. */
    @Test(timeout = 10_000)
    fun `eine weggefallene Aufnahme wird gemeldet`() {
        val source = FakeSource(640, 1424, frames = 1, missing = 12)
        val (socket, messages) = collector()

        source.running = false

        Thread {
            while (source.calls <= 5) {
                Thread.sleep(1)
            }

            // Sie kommt zurück: ab jetzt liefert die Quelle wieder.
            source.running = true

            while (source.calls <= 20) {
                Thread.sleep(1)
            }

            socket.close()
        }.start()

        ScreenStream(source, 640, 1424, fps = 10, sleep = {}).run(socket)

        val texts = messages.filterIsInstance<String>().map { JSONObject(it).getString("t") }

        assertTrue("„unavailable\" fehlt", texts.contains("unavailable"))
        assertTrue("„available\" fehlt", texts.contains("available"))
    }

    @Test(timeout = 10_000)
    fun `eine feste Qualitaetsstufe kommt bei der Quelle an`() {
        val source = FakeSource(640, 1424, frames = 2)
        val (socket, _) = collector()

        val stream = ScreenStream(source, 640, 1424, fps = 60, sleep = {})

        stream.apply(JSONObject().put("t", "quality").put("value", "low").toString())

        Thread {
            while (source.calls <= 2) {
                Thread.sleep(1)
            }

            socket.close()
        }.start()

        stream.run(socket)

        assertEquals(40, source.lastQuality)
    }

    @Test(timeout = 10_000)
    fun `ein kaputter Steuerbefehl kostet den Stream nicht`() {
        val stream = ScreenStream(FakeSource(10, 10, frames = 0), 10, 10, sleep = {})

        stream.apply("kein JSON")
        stream.apply(JSONObject().put("t", "unbekannt").toString())
    }

    @Test(timeout = 10_000)
    fun `die Aufnahme wird am Ende geschlossen`() {
        val source = FakeSource(640, 1424, frames = 0)
        val (socket, _) = collector()

        socket.close()

        ScreenStream(source, 640, 1424, sleep = {}).run(socket)

        assertTrue("Ein virtueller Bildschirm, den niemand schließt, kostet Strom", source.closed)
    }

    private fun read(data: ByteArray, offset: Int): Int =
        (data[offset].toInt() and 0xFF) or ((data[offset + 1].toInt() and 0xFF) shl 8)
}
