package app.remotedesktop.client;

import android.Manifest;
import android.os.Build;

import com.getcapacitor.PermissionState;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;

/**
 * Die Brücke, über die die React-App den Vordergrunddienst startet und stoppt.
 *
 * Aufgerufen wird sie aus `app/src/platform/capacitor.ts`; dort heißt das
 * `session.begin()` und `session.end()`. Die App weiß nicht, dass dahinter ein
 * Android-Dienst steckt — im Browser und im Windows-Fenster passiert an
 * derselben Stelle schlicht nichts.
 */
@CapacitorPlugin(
    name = "SessionService",
    permissions = {
        @Permission(alias = SessionServicePlugin.NOTIFICATIONS, strings = { Manifest.permission.POST_NOTIFICATIONS })
    }
)
public class SessionServicePlugin extends Plugin {

    static final String NOTIFICATIONS = "notifications";

    @PluginMethod
    public void start(PluginCall call) {
        // Ab Android 13 ist die Benachrichtigung erlaubnispflichtig. Der Dienst
        // liefe auch ohne sie, aber dann sähe niemand, dass eine Verbindung
        // offen ist — und genau das ist der Handel, den man mit dem System
        // eingeht.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
            && getPermissionState(NOTIFICATIONS) != PermissionState.GRANTED) {
            requestPermissionForAlias(NOTIFICATIONS, call, "afterNotificationPermission");
            return;
        }

        startService(call);
    }

    @PermissionCallback
    private void afterNotificationPermission(PluginCall call) {
        // Auch bei Ablehnung wird gestartet: die Sitzung offenzuhalten ist der
        // Zweck, die Benachrichtigung nur die Auflage dafür. Abzubrechen hieße,
        // dem Nutzer wegen einer stummen Leiste die Funktion zu nehmen.
        startService(call);
    }

    private void startService(PluginCall call) {
        SessionService.start(getContext(), call.getString("device"));
        call.resolve();
    }

    @PluginMethod
    public void stop(PluginCall call) {
        SessionService.stop(getContext());
        call.resolve();
    }
}
