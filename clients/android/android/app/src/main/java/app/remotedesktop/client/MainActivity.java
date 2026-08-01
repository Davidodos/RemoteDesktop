package app.remotedesktop.client;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {

    @Override
    public void onCreate(Bundle savedInstanceState) {
        // Plugins, die im App-Projekt selbst liegen, findet Capacitor nicht von
        // allein — anders als die aus node_modules. Die Anmeldung muss vor
        // super.onCreate() stehen, weil die Brücke dort gebaut wird.
        registerPlugin(SessionServicePlugin.class);
        registerPlugin(AppUpdatePlugin.class);

        super.onCreate(savedInstanceState);
    }
}
