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

    /** Ob jemand die Aufnahme bereits bestätigt hat. */
    val isPermitted: Boolean get() = permission != null

    /**
     * Verwahrt die Bestätigung aus dem Systemdialog.
     *
     * Das `Intent` ist der eigentliche Schlüssel — ohne es gibt
     * `getMediaProjection` nichts heraus, gleich, was sonst stimmt.
     */
    fun remember(resultCode: Int, data: Intent) {
        this.resultCode = resultCode
        this.permission = data
    }

    fun forget() {
        permission = null
        resultCode = 0
    }

    /**
     * Öffnet eine Aufnahme. `null` heißt: es liegt keine Bestätigung vor, oder
     * Android hat sie zurückgezogen.
     *
     * @param display Die echte Größe des Bildschirms; daraus ergibt sich, wie
     *   weit heruntergerechnet wird.
     */
    fun open(context: Context, display: HostServer.Screen): FrameSource? {
        val data = permission ?: return null

        val manager = context.getSystemService(Context.MEDIA_PROJECTION_SERVICE)
            as MediaProjectionManager

        val projection = runCatching { manager.getMediaProjection(resultCode, data) }
            .getOrNull()

        if (projection == null) {
            // Android nimmt die Erlaubnis zurück, wenn der Vordergrunddienst
            // nicht rechtzeitig mit dem passenden Typ läuft. Sie zu behalten
            // hieße, bei jedem Versuch dasselbe Nichts zu bekommen.
            Log.w(TAG, "Die Aufnahme-Erlaubnis gilt nicht mehr.")
            forget()
            return null
        }

        return runCatching { ProjectionSource(context, projection, display) }
            .onFailure { Log.e(TAG, "Aufnahme lässt sich nicht öffnen.", it) }
            .getOrNull()
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

        /**
         * Seit Android 14 muss ein Rückruf angemeldet sein, bevor der virtuelle
         * Bildschirm entsteht — sonst wirft `createVirtualDisplay`. Er ist
         * zugleich die einzige Stelle, an der man erfährt, dass der Nutzer die
         * Aufnahme in der Systemleiste beendet hat.
         */
        private val callback = object : MediaProjection.Callback() {
            override fun onStop() {
                ScreenCapture.forget()
            }
        }

        private var virtualDisplay: VirtualDisplay? = null

        private var reusable: Bitmap? = null

        init {
            projection.registerCallback(callback, handler)

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

        override fun close() {
            runCatching { virtualDisplay?.release() }
            runCatching { reader.close() }
            runCatching { projection.unregisterCallback(callback) }
            runCatching { projection.stop() }

            reusable?.recycle()
            reusable = null
        }
    }
}
