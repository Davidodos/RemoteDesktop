package app.remotedesktop.client.host

import org.json.JSONObject

/**
 * Ein aufgenommenes Bild, schon als JPEG.
 *
 * Breite und Höhe sind die des gelieferten Bildes, nicht die des Displays: bei
 * heruntergerechneter Aufnahme ist es kleiner, und die App zeichnet es auf die
 * Fläche, die `meta` angekündigt hat.
 */
data class CapturedFrame(val jpeg: ByteArray, val width: Int, val height: Int)

/**
 * Woher die Bilder kommen. Auf dem Gerät ist das die Bildschirmaufnahme; im
 * Test eine Handvoll erfundener Bilder.
 */
interface FrameSource {
    /** Größe der gelieferten Bilder. */
    val width: Int
    val height: Int

    /**
     * Das nächste Bild, oder `null`, wenn gerade keins zu holen ist. Kein
     * Fehler: zwischen zwei Bildern liegt schlicht nichts an, und Android
     * liefert nach dem Drehen des Geräts kurz gar nichts.
     */
    fun next(quality: Int): CapturedFrame?

    /**
     * Ob die Aufnahme überhaupt noch läuft.
     *
     * <p>
     * **Der Unterschied, an dem es hing.** Ein `null` aus [next] heißt „gerade
     * nichts Neues" — auf einem Handy ist das der Normalfall, denn Android
     * liefert nur bei Änderung ein Bild. Ein stehender Bildschirm liefert
     * minutenlang nichts, und das ist richtig so. *Hier* steht die andere
     * Frage: ist die Quelle weg? Beides in einem `null` zu führen hieß, dass
     * ein ruhiger Bildschirm nach einer Sekunde als „nicht verfügbar" gemeldet
     * wurde.
     * </p>
     */
    val isRunning: Boolean get() = true

    fun close()
}

/**
 * Der Bild-Stream dieses Handys — dasselbe Protokoll wie `/ws/screen` beim
 * Windows-Agent.
 *
 * **Warum JPEG und nicht H.264.** Der Agent hat beide Stufen: JPEG über den
 * WebSocket, und darüber WebRTC mit Hardware-Encoder. Auf Android hieße die
 * zweite Stufe eine WebRTC-Bibliothek von rund zehn Megabyte — in einer App,
 * die sich über GitHub selbst aktualisiert, bei jedem Update aufs Neue. Die
 * erste Stufe kostet nichts, weil die App sie ohnehin schon spricht: derselbe
 * Achtbyte-Kopf, dieselben Textnachrichten, dieselbe Qualitätsregelung. Reicht
 * sie am echten Gerät nicht, ist H.264 der nächste Schritt — und dann mit
 * einem Grund statt auf Verdacht.
 *
 * Anders als auf Windows gibt es keine Änderungsrechtecke: Android meldet nicht,
 * was sich geändert hat. Jedes Bild geht als ein Ausschnitt über die volle
 * Fläche.
 */
class ScreenStream(
    private val source: FrameSource,
    private val displayWidth: Int,
    private val displayHeight: Int,
    private val fps: Int = DEFAULT_FPS,
    private val now: Clock = System::currentTimeMillis,
    private val sleep: (Long) -> Unit = Thread::sleep,
) {

    companion object {
        const val DEFAULT_FPS = 20

        /** Die Stufen, zwischen denen „auto" wandert, und was die festen Stufen bedeuten. */
        private val QUALITY_STEPS = intArrayOf(35, 45, 55, 65, 75, 85)

        /** Wie oft die Kennzahlen an die App gehen. */
        private const val STATS_INTERVAL_MS = 1000L

        /** Über dieser Bilddauer wird die Qualität gesenkt, darunter gehoben. */
        private const val SLOW_FRAME_MS = 90
        private const val FAST_FRAME_MS = 35

        const val HEADER_BYTES = 8
    }

    /** Was die App über den Socket schicken darf. */
    private var paused = false
    private var mode = "auto"
    private var step = 3

    private var frames = 0
    private var bytes = 0L
    private var lastStats = 0L

    /**
     * Läuft, bis die Verbindung endet.
     *
     * Der Socket gehört danach dem Aufrufer — geschlossen wird dort, wo er
     * geöffnet wurde.
     */
    fun run(socket: WebSocketConnection) {
        socket.sendText(
            JSONObject()
                .put("t", "meta")
                .put("monitor", 0)
                .put("width", displayWidth)
                .put("height", displayHeight)
                .put("fps", fps)
                .put("count", 1)
                .toString(),
        )

        lastStats = now()

        var announcedMissing = false

        while (socket.isOpen) {
            val started = now()

            if (paused) {
                sleep(100)
                continue
            }

            val frame = source.next(quality())

            if (frame == null) {
                // **Kein „nicht verfügbar", nur weil nichts kommt.** Android
                // liefert ein Bild, wenn sich etwas ändert — sonst keins. Ein
                // Handy, das ruhig auf dem Tisch liegt, liefert minutenlang
                // nichts, und das ist kein Fehler, sondern ein Bildschirm, auf
                // dem sich nichts tut. Gemeldet wird nur, wenn die Aufnahme
                // wirklich weg ist.
                if (!source.isRunning && !announcedMissing) {
                    socket.sendText(JSONObject().put("t", "unavailable").toString())
                    announcedMissing = true
                }

                // **Auch dann werden die Kennzahlen geschickt.** Sie standen
                // vorher hinter dem Bild, und damit verstummte der Socket,
                // sobald sich eine Weile nichts bewegte — nach sechs Sekunden
                // Stille hielt die Gegenseite die Verbindung für tot und baute
                // sie neu auf. Das war der zweite Teil desselben Befunds.
                sendStatsIfDue(socket)
                sleep(budget())
                continue
            }

            if (announcedMissing) {
                socket.sendText(JSONObject().put("t", "available").toString())
                announcedMissing = false
            }

            socket.sendBinary(frame(frame))

            frames++
            bytes += HEADER_BYTES + frame.jpeg.size

            val elapsed = now() - started

            adjust(elapsed)
            sendStatsIfDue(socket)

            val rest = budget() - elapsed

            if (rest > 0) {
                sleep(rest)
            }
        }

        source.close()
    }

    /** Nimmt einen Steuerbefehl der App entgegen. */
    fun apply(message: String) {
        val json = runCatching { JSONObject(message) }.getOrNull() ?: return

        when (json.optString("t")) {
            "pause" -> paused = true
            "resume" -> paused = false
            "refresh" -> Unit // Es gibt keine Teilbilder — das nächste ist ohnehin vollständig.
            "quality" -> {
                val wanted = json.optString("value")

                if (wanted in setOf("auto", "high", "medium", "low")) {
                    mode = wanted
                }
            }
        }
    }

    /**
     * Der Achtbyte-Kopf und das Bild dahinter — Position und Größe des
     * Ausschnitts, wie ihn `FrameHeader` auf Windows schreibt. Hier ist es immer
     * die ganze Fläche.
     */
    private fun frame(captured: CapturedFrame): ByteArray {
        val message = ByteArray(HEADER_BYTES + captured.jpeg.size)

        writeShort(message, 0, 0)
        writeShort(message, 2, 0)
        writeShort(message, 4, displayWidth)
        writeShort(message, 6, displayHeight)

        captured.jpeg.copyInto(message, HEADER_BYTES)

        return message
    }

    private fun writeShort(target: ByteArray, offset: Int, value: Int) {
        target[offset] = (value and 0xFF).toByte()
        target[offset + 1] = ((value shr 8) and 0xFF).toByte()
    }

    private fun quality(): Int = when (mode) {
        "high" -> 85
        "medium" -> 65
        "low" -> 40
        else -> QUALITY_STEPS[step]
    }

    /**
     * Die Regelung von „auto": dauert ein Bild zu lange, geht die Qualität eine
     * Stufe herunter, sonst langsam wieder hinauf. Eine Stufe je Bild — schneller
     * geregelt sähe man das Pumpen.
     */
    private fun adjust(elapsedMs: Long) {
        if (mode != "auto") {
            return
        }

        if (elapsedMs > SLOW_FRAME_MS && step > 0) {
            step--
        } else if (elapsedMs < FAST_FRAME_MS && step < QUALITY_STEPS.lastIndex) {
            step++
        }
    }

    private fun budget(): Long = (1000L / fps).coerceAtLeast(1)

    private fun sendStatsIfDue(socket: WebSocketConnection) {
        val moment = now()
        val span = moment - lastStats

        if (span < STATS_INTERVAL_MS) {
            return
        }

        socket.sendText(
            JSONObject()
                .put("t", "stats")
                .put("fps", Math.round(frames * 1000.0 / span * 10) / 10.0)
                .put("kbps", (bytes * 8 / span).toInt())
                .put("quality", quality())
                .put("scale", if (displayWidth == 0) 1.0 else source.width.toDouble() / displayWidth)
                .put("mode", mode)
                .toString(),
        )

        frames = 0
        bytes = 0
        lastStats = moment
    }
}
