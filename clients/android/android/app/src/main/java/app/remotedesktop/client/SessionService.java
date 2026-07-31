package app.remotedesktop.client;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;

import androidx.core.app.NotificationCompat;
import androidx.core.app.ServiceCompat;

/**
 * Hält die laufende Sitzung am Leben, solange die App im Hintergrund ist.
 *
 * Das ist der eigentliche Grund für die APK: ein Browser-Tab im Hintergrund
 * wird gedrosselt, der Eingabe-Socket fällt zu und der Videostrom pausiert. Ein
 * Vordergrunddienst nimmt Android die Entscheidung ab — der Preis dafür ist die
 * sichtbare Benachrichtigung, und die ist hier keine Last, sondern die einzige
 * Stelle, an der man sieht, dass noch eine Verbindung offen ist.
 */
public class SessionService extends Service {

    /** Name des Rechners, mit dem gerade verbunden ist. */
    public static final String EXTRA_DEVICE = "device";

    private static final String CHANNEL_ID = "session";
    private static final int NOTIFICATION_ID = 1;

    @Override
    public IBinder onBind(Intent intent) {
        // Gesteuert wird der Dienst ausschließlich über startService/stopService
        // aus SessionServicePlugin. Eine Bindung bräuchte einen zweiten
        // Lebenszyklus, ohne dass jemand etwas davon hätte.
        return null;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String device = intent == null ? null : intent.getStringExtra(EXTRA_DEVICE);

        createChannel();
        ServiceCompat.startForeground(
            this,
            NOTIFICATION_ID,
            buildNotification(device),
            foregroundType()
        );

        // NOT_STICKY: startet Android den Dienst nach einem Abschuss neu, gibt es
        // keine Sitzung mehr, die er offenhalten könnte. Eine Benachrichtigung
        // ohne Verbindung dahinter wäre schlicht gelogen.
        return START_NOT_STICKY;
    }

    private int foregroundType() {
        // Ab Android 14 muss jeder Vordergrunddienst seinen Typ nennen, sonst
        // beendet das System ihn beim Start mit einer Ausnahme. Darunter gibt es
        // die Typen noch nicht.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            return ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE;
        }

        return 0;
    }

    private Notification buildNotification(String device) {
        Intent open = new Intent(this, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);

        PendingIntent tap = PendingIntent.getActivity(
            this,
            0,
            open,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE
        );

        String text = device == null || device.isEmpty()
            ? getString(R.string.session_notification_text_unknown)
            : getString(R.string.session_notification_text, device);

        return new NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.session_notification_title))
            .setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentIntent(tap)
            .setOngoing(true)
            // Ohne LOW klingelt bei jedem Verbindungsaufbau das Telefon.
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build();
    }

    private void createChannel() {
        NotificationChannel channel = new NotificationChannel(
            CHANNEL_ID,
            getString(R.string.session_channel_name),
            NotificationManager.IMPORTANCE_LOW
        );
        channel.setDescription(getString(R.string.session_channel_description));
        channel.setShowBadge(false);

        NotificationManager manager = getSystemService(NotificationManager.class);

        if (manager != null) {
            manager.createNotificationChannel(channel);
        }
    }

    /** Startet den Dienst für ein Gerät; ein zweiter Aufruf tauscht nur den Text. */
    static void start(Context context, String device) {
        Intent intent = new Intent(context, SessionService.class);
        intent.putExtra(EXTRA_DEVICE, device);
        context.startForegroundService(intent);
    }

    static void stop(Context context) {
        context.stopService(new Intent(context, SessionService.class));
    }
}
