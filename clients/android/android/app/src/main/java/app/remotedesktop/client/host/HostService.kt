package app.remotedesktop.client.host

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import app.remotedesktop.client.MainActivity
import app.remotedesktop.client.R

/**
 * Hält den Host am Leben, solange dieses Gerät steuerbar sein soll.
 *
 * Ein Server, der mit der Activity stirbt, wäre keiner: das Handy soll auch
 * dann antworten, wenn es in der Tasche liegt und der Bildschirm aus ist. Der
 * Preis ist die sichtbare Benachrichtigung — und die ist hier kein Ärgernis,
 * sondern das Einzige, woran man sieht, dass das eigene Handy gerade von außen
 * erreichbar ist. Sie soll auffallen.
 */
class HostService : Service() {

    companion object {
        private const val TAG = "HostService"
        private const val CHANNEL_ID = "host"
        private const val NOTIFICATION_ID = 2

        fun start(context: Context) {
            val intent = Intent(context, HostService::class.java)

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, HostService::class.java))
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val runtime = HostRuntime.of(this)

        try {
            runtime.start()
        } catch (failure: Exception) {
            // Der häufigste Grund ist ein belegter Port 8443 — etwa weil eine
            // frühere Fassung des Dienstes noch hängt. Das ist eine Auskunft
            // wert und kein stiller Tod: ohne sie steht in der App „läuft" und
            // von außen antwortet nichts.
            Log.e(TAG, "Der Host konnte nicht starten.", failure)
            stopSelf()
            return START_NOT_STICKY
        }

        try {
            createChannel()
            ServiceCompat.startForeground(
                this,
                NOTIFICATION_ID,
                notification(runtime),
                foregroundType(),
            )
        } catch (failure: Exception) {
            // Android verweigert Vordergrunddienste aus Gründen, die sich von
            // Fassung zu Fassung ändern. Anders als bei der Sitzung ist das
            // hier ein Abbruch: ein Server, den das System jederzeit einsammeln
            // darf, ist schlimmer als keiner — er antwortet manchmal.
            Log.e(TAG, "Vordergrunddienst nicht gestartet.", failure)
            runtime.stop()
            stopSelf()
            return START_NOT_STICKY
        }

        // Neu starten, wenn das System den Dienst einsammelt: wer sein Handy
        // freigegeben hat, will es freigegeben wissen.
        return START_STICKY
    }

    override fun onDestroy() {
        HostRuntime.of(this).stop()
        super.onDestroy()
    }

    private fun notification(runtime: HostRuntime): Notification {
        val open = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

        val address = runtime.addresses().firstOrNull()

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.host_notification_title))
            .setContentText(
                if (address == null) {
                    getString(R.string.host_notification_text_unknown)
                } else {
                    getString(R.string.host_notification_text, address, runtime.port)
                },
            )
            .setSmallIcon(android.R.drawable.ic_menu_view)
            .setContentIntent(open)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun createChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return
        }

        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.host_channel_name),
            NotificationManager.IMPORTANCE_LOW,
        ).apply { description = getString(R.string.host_channel_description) }

        (getSystemService(NOTIFICATION_SERVICE) as NotificationManager)
            .createNotificationChannel(channel)
    }

    /**
     * Der Typ, mit dem sich der Dienst anmeldet.
     *
     * `mediaProjection` kommt dazu, sobald jemand die Aufnahme bestätigt hat —
     * und **muss** dazukommen, bevor `getMediaProjection` gerufen wird: seit
     * Android 14 zieht das System die Erlaubnis sonst wortlos zurück. Deshalb
     * meldet sich der Dienst nach der Bestätigung ein zweites Mal an, statt den
     * Typ von Anfang an zu führen — ein Dienst, der dauerhaft „nimmt gerade den
     * Bildschirm auf" behauptet, wäre eine Unwahrheit im Benachrichtigungstext.
     */
    private fun foregroundType(): Int {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
            return 0
        }

        val base = ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC

        return if (ScreenCapture.isPermitted) {
            base or ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
        } else {
            base
        }
    }
}
