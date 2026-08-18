package app.remotedesktop.client;

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
