package app.remotedesktop.client.host

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Handler
import android.os.Looper
import android.util.Log
import java.io.ByteArrayOutputStream

/**
 * Die Bildschirmaufnahme des Handys.
 *
 * **Der Systemdialog lässt sich nicht umgehen.** Android fragt bei jeder neuen
 * Aufnahme nach, und ohne Root gibt es keinen Weg daran vorbei. Einmal
 * bestätigt, hält die Erlaubnis, solange der Vordergrunddienst lebt — auch bei
 * gesperrtem Bildschirm und über Tage. Sie stirbt mit dem Neustart des Geräts;
 * dann muss wieder jemand tippen. Das ist die eine Einschränkung, die dieses
 * Vorhaben hat, und sie gehört auf den Bildschirm und nicht in eine Fußnote.
 *
 * Die Erlaubnis wird hier verwahrt und nicht bei jedem Verbindungsaufbau neu
 * erfragt: ein zweiter Client, der sich dazuschaltet, würde sonst einen Dialog
 * auf einem Gerät auslösen, das niemand in der Hand hat.
 */
object ScreenCapture {

    private const val TAG = "ScreenCapture"

    /**
     * Längste Kante der Aufnahme. Ein Handy hat heute 2400 Pixel Höhe; die
     * vollständig als JPEG zu übertragen kostet mehr, als es zeigt — auf dem
     * Gegenüber ist das Bild ohnehin kleiner. Bei 1280 bleibt Text lesbar.
     */
    private const val MAX_EDGE = 1280

    @Volatile
    private var permission: Intent? = null

    @Volatile
    private var resultCode: Int = 0

    /**
     * Die laufende Aufnahme.
     *
     * <p>
     * **Sie überlebt die einzelne Verbindung.** Vorher wurde sie mit jedem
     * Bild-Socket geöffnet und am Ende mit `stop()` beendet — und `stop()`
     * löst `MediaProjection.Callback.onStop` aus, was die Erlaubnis
     * wegwarf. Riss die Verbindung einmal ab, musste jemand die Freigabe in
     * den Einstellungen erneut erteilen. Eine Bestätigung gilt aber der
     * Aufnahme und nicht dem WebSocket, der sie gerade benutzt.
     * </p>
     *
     * <p>
     * Nebenbei ist es das, was Android ohnehin verlangt: seit Android 14 gibt
     * `getMediaProjection` zu **einer** Zustimmung genau **eine** Projektion
     * heraus. Ein zweiter Aufruf mit demselben Token bekommt nichts.
     * </p>
     */
    @Volatile
    private var projection: MediaProjection? = null

    /** Ob jemand die Aufnahme bereits bestätigt hat. */
    val isPermitted: Boolean get() = permission != null

    /**
     * Verwahrt die Bestätigung aus dem Systemdialog.
     *
     * Das `Intent` ist der eigentliche Schlüssel — ohne es gibt
     * `getMediaProjection` nichts heraus, gleich, was sonst stimmt.
     */
    fun remember(resultCode: Int, data: Intent) {
        // Eine neue Zustimmung ersetzt die alte Projektion; die alte gehört zu
        // einem Token, das niemand mehr benutzt.
        stopProjection()

        this.resultCode = resultCode
        this.permission = data
    }

    fun forget() {
        permission = null
        resultCode = 0
        stopProjection()
    }

    /**
     * Öffnet eine Aufnahme. `null` heißt: es liegt keine Bestätigung vor, oder
     * Android hat sie zurückgezogen.
     *
     * @param display Die echte Größe des Bildschirms; daraus ergibt sich, wie
     *   weit heruntergerechnet wird.
     */
    @Synchronized
    fun open(context: Context, display: HostServer.Screen): FrameSource? {
        val running = projection ?: start(context) ?: return null

        return runCatching { ProjectionSource(context, running, display) }
            .onFailure { Log.e(TAG, "Aufnahme lässt sich nicht öffnen.", it) }
            .getOrNull()
    }

    /** Holt die Projektion zur verwahrten Zustimmung — einmal je Zustimmung. */
    private fun start(context: Context): MediaProjection? {
        val data = permission ?: return null

        val manager = context.getSystemService(Context.MEDIA_PROJECTION_SERVICE)
            as MediaProjectionManager

        val fresh = runCatching { manager.getMediaProjection(resultCode, data) }.getOrNull()

        if (fresh == null) {
            // Android nimmt die Erlaubnis zurück, wenn der Vordergrunddienst
            // nicht rechtzeitig mit dem passenden Typ läuft. Sie zu behalten
            // hieße, bei jedem Versuch dasselbe Nichts zu bekommen.
            Log.w(TAG, "Die Aufnahme-Erlaubnis gilt nicht mehr.")
            forget()
            return null
        }

        // Der Rückruf gehört der Projektion und nicht dem einzelnen Strom: er
        // meldet, dass der Nutzer die Aufnahme in der Systemleiste beendet hat,
        // und das gilt dann für alle.
        fresh.registerCallback(
            object : MediaProjection.Callback() {
                override fun onStop() = forget()
            },
            Handler(Looper.getMainLooper()),
        )

        projection = fresh

        return fresh
    }

    private fun stopProjection() {
        // Erst aus dem Feld nehmen, dann beenden: `stop()` ruft `onStop`, und
        // das landet wieder hier.
        val running = projection ?: return

        projection = null

        runCatching { running.stop() }
    }

    /** Die Zielgröße: heruntergerechnet, aber im Seitenverhältnis des Geräts. */
    fun scaled(display: HostServer.Screen): HostServer.Screen {
        val longest = maxOf(display.width, display.height)

        if (longest <= MAX_EDGE) {
            return display
        }

        val factor = MAX_EDGE.toDouble() / longest

        // Gerade Zahlen: ungerade Kanten sind bei manchen Encodern und beim
        // Skalieren eine Quelle für Ein-Pixel-Ränder.
        return HostServer.Screen(
            (display.width * factor).toInt() and 1.inv(),
            (display.height * factor).toInt() and 1.inv(),
        )
    }

    /**
     * Die laufende Aufnahme: virtueller Bildschirm → `ImageReader` → Bitmap →
     * JPEG.
     */
    private class ProjectionSource(
        context: Context,
        private val projection: MediaProjection,
        display: HostServer.Screen,
    ) : FrameSource {

        private val target = scaled(display)

        override val width: Int get() = target.width
        override val height: Int get() = target.height

        private val reader = ImageReader.newInstance(
            target.width, target.height, PixelFormat.RGBA_8888, 2,
        )

        private val handler = Handler(Looper.getMainLooper())

        private var virtualDisplay: VirtualDisplay? = null

        private var reusable: Bitmap? = null

        /**
         * Ob die Quelle noch etwas liefern kann. Sie ist geschlossen oder die
         * Zustimmung ist weg — beides heißt „nichts mehr zu holen", und das ist
         * etwas anderes als „gerade nichts Neues".
         */
        override val isRunning: Boolean
            get() = virtualDisplay != null && ScreenCapture.isPermitted

        init {
            // Der Rückruf hängt an der Projektion und wird dort einmal
            // angemeldet (siehe `start`) — seit Android 14 muss er stehen,
            // bevor der virtuelle Bildschirm entsteht.
            virtualDisplay = projection.createVirtualDisplay(
                "RemoteDesktop",
                target.width,
                target.height,
                context.resources.displayMetrics.densityDpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                reader.surface,
                null,
                handler,
            )
        }

        override fun next(quality: Int): CapturedFrame? {
            // Das jüngste Bild, nicht das älteste: bei einem Rückstand ist alles
            // davor bereits veraltet, und eine Fernsteuerung, die hinterherläuft,
            // ist unbrauchbarer als eine, die Bilder auslässt.
            val image = runCatching { reader.acquireLatestImage() }.getOrNull() ?: return null

            return try {
                val plane = image.planes[0]
                val padding = plane.rowStride - plane.pixelStride * target.width
                val stride = target.width + padding / plane.pixelStride

                val bitmap = reusable?.takeIf { it.width == stride }
                    ?: Bitmap.createBitmap(stride, target.height, Bitmap.Config.ARGB_8888)
                        .also { reusable = it }

                bitmap.copyPixelsFromBuffer(plane.buffer)

                val output = ByteArrayOutputStream()

                // Der Zuschnitt fällt beim Kodieren an, nicht davor: eine zweite
                // Bitmap je Bild wäre eine Zuweisung von mehreren Megabyte,
                // zwanzigmal in der Sekunde.
                val cropped = if (stride == target.width) {
                    bitmap
                } else {
                    Bitmap.createBitmap(bitmap, 0, 0, target.width, target.height)
                }

                cropped.compress(Bitmap.CompressFormat.JPEG, quality, output)

                CapturedFrame(output.toByteArray(), target.width, target.height)
            } catch (broken: Exception) {
                Log.w(TAG, "Bild konnte nicht kodiert werden.", broken)
                null
            } finally {
                image.close()
            }
        }

        /**
         * Nur der eigene Strom — **die Projektion bleibt**.
         *
         * Sie gehört der Zustimmung und nicht diesem WebSocket. Sie hier zu
         * stoppen war der Grund, warum nach jedem Verbindungsabbruch jemand die
         * Freigabe von Hand erneut erteilen musste: `stop()` löst `onStop` aus,
         * und das wirft die Erlaubnis weg. Beendet wird sie dort, wo sie auch
         * erteilt wurde — in [ScreenCapture.forget].
         */
        override fun close() {
            runCatching { virtualDisplay?.release() }
            virtualDisplay = null

            runCatching { reader.close() }

            reusable?.recycle()
            reusable = null
        }
    }
}
