package app.remotedesktop.client;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * Die Zurück-Taste und das Beenden — die Gegenseite von
 * {@code app/src/platform/navigation.ts}.
 *
 * <p>
 * Ohne dieses Plugin beendete ein Druck auf Zurück die App sofort, egal wo
 * man stand. Jetzt meldet {@link MainActivity} den Druck hierher, die Seite
 * entscheidet, und beendet wird nur auf ihren Zuruf.
 * </p>
 */
@CapacitorPlugin(name = "AppNavigation")
public class AppNavigationPlugin extends Plugin {

    /** Beendet die App — der zweite Druck auf der obersten Ebene. */
    @PluginMethod
    public void exit(PluginCall call) {
        call.resolve();

        if (getActivity() != null) {
            getActivity().finishAndRemoveTask();
        }
    }

    /** Ob die Seite überhaupt zuhört. Sonst gilt das Verhalten von Android. */
    boolean isListening() {
        return hasListeners("back");
    }

    void back() {
        notifyListeners("back", new JSObject());
    }
}
