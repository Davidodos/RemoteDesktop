package app.remotedesktop.client;

import android.Manifest;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;

import com.getcapacitor.BridgeActivity;

import app.remotedesktop.client.host.HostPlugin;
import app.remotedesktop.client.host.HostPreference;
import app.remotedesktop.client.host.HostService;
import app.remotedesktop.client.surfaces.CertificateTrustPlugin;
import app.remotedesktop.client.surfaces.SurfacesPlugin;

public class MainActivity extends BridgeActivity {

    @Override
    public void onCreate(Bundle savedInstanceState) {
        // Plugins, die im App-Projekt selbst liegen, findet Capacitor nicht von
        // allein — anders als die aus node_modules. Die Anmeldung muss vor
        // super.onCreate() stehen, weil die Brücke dort gebaut wird.
        registerPlugin(SessionServicePlugin.class);
        registerPlugin(AppUpdatePlugin.class);
        registerPlugin(SurfacesPlugin.class);
        registerPlugin(CertificateTrustPlugin.class);
        registerPlugin(HostPlugin.class);

        super.onCreate(savedInstanceState);

        // Nach einem Update nichts Altes mehr zeigen. Muss nach super stehen:
        // die Brücke und mit ihr die WebView entstehen erst dort.
        UpgradeCleanup.runIfUpgraded(
                this, getBridge() == null ? null : getBridge().getWebView());

        askForNotifications();

        // Der Host läuft, solange die App offen ist — nicht länger.
        //
        // Vorher war er auf Dauerbetrieb ausgelegt: einmal eingeschaltet, lief
        // er weiter, auch wenn die App längst weggewischt war. Das ist zu viel
        // für ein Gerät, das man in der Hosentasche trägt. Wer sein Handy vom
        // PC aus steuern will, hat es ohnehin in der Hand.
        if (HostPreference.INSTANCE.isEnabled(this)) {
            HostService.Companion.start(this);
        }
    }

    /**
     * Fragt beim Start nach der Erlaubnis für Benachrichtigungen.
     *
     * <p>
     * <b>Warum sofort und nicht bei der ersten Benachrichtigung.</b> Sie ist
     * hier keine Beigabe: über sie läuft die Rückfrage „darf dieses Gerät jetzt
     * verbinden?", und die kommt womöglich, bevor dieses Handy jemals selbst
     * etwas gesteuert hat. Wer sie nicht erteilt hat, sieht eine Anfrage nur,
     * wenn er die App gerade offen vor sich hat — und eine Anfrage, die niemand
     * sieht, gilt nach einer halben Minute als abgelehnt. Der Vordergrunddienst
     * braucht sie ebenfalls: ohne sie zeigt Android seine Benachrichtigung
     * nicht, und dann läuft der Host sichtbar für niemanden.
     * </p>
     *
     * <p>
     * Vor Android 13 gibt es die Erlaubnis nicht — dort sind Benachrichtigungen
     * von vornherein erlaubt. Abgelehnt wird nicht nachgefasst: Android zeigt
     * die Frage ohnehin kein zweites Mal, und eine App, die bei jedem Start
     * dasselbe verlangt, bekommt sie erst recht nicht.
     * </p>
     */
    private void askForNotifications() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            return;
        }

        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                == PackageManager.PERMISSION_GRANTED) {
            return;
        }

        requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, 1);
    }

    /**
     * Und aus, wenn die App zu ist. Nicht in onStop(): der Bildschirm geht aus,
     * jemand nimmt einen Anruf entgegen, die App wandert in den Hintergrund —
     * währenddessen soll die laufende Sitzung nicht abreißen. Erst wenn die
     * Activity endgültig verschwindet, verschwindet auch der Server.
     */
    @Override
    public void onDestroy() {
        if (!isChangingConfigurations()) {
            HostService.Companion.stop(this);
        }

        super.onDestroy();
    }
}
