package app.remotedesktop.client;

import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * Fassung ablesen und eine neue APK installieren.
 *
 * Außerhalb von Google Play zeigt Android <b>immer</b> einen Bestätigungsdialog,
 * bevor eine App installiert wird, und die App braucht dafür
 * {@code REQUEST_INSTALL_PACKAGES}. „Ein Knopf und fertig" heißt hier also: ein
 * Knopf und ein Systemdialog. Stiller geht es nur über Play — und eine bereits
 * seitlich installierte APK ließe sich davon ohnehin nicht mehr aktualisieren,
 * weil die Signatur nicht passt.
 *
 * Genau diese Signaturprüfung ist zugleich der Schutz: Android lässt eine APK
 * nur über eine installierte drüber, wenn sie mit demselben Schlüssel
 * unterschrieben ist. Eine untergeschobene Datei scheitert daran, bevor
 * irgendetwas von ihr läuft.
 */
@CapacitorPlugin(name = "AppUpdate")
public class AppUpdatePlugin extends Plugin {

    /** Die installierte Fassung, damit die App weiß, ob es etwas Neues gibt. */
    @PluginMethod
    public void current(PluginCall call) {
        try {
            PackageManager packages = getContext().getPackageManager();
            PackageInfo info = packages.getPackageInfo(getContext().getPackageName(), 0);

            JSObject result = new JSObject();
            result.put("version", info.versionName == null ? "" : info.versionName);
            result.put("versionCode", info.getLongVersionCode());

            call.resolve(result);
        } catch (PackageManager.NameNotFoundException e) {
            // Kann nicht vorkommen — wir fragen nach uns selbst. Aber ein
            // Absturz an dieser Stelle wäre das Letzte, was jemand erwartet.
            call.reject("Eigene Fassung nicht lesbar.", e);
        }
    }

    /**
     * Lädt die APK und übergibt sie an die {@code PackageInstaller}-Sitzung.
     *
     * Der Download läuft in einem eigenen Thread: er dauert je nach Verbindung
     * Sekunden bis Minuten, und der Haupt-Thread trägt in dieser Zeit die
     * Oberfläche.
     */
    @PluginMethod
    public void install(PluginCall call) {
        String url = call.getString("url");

        if (url == null || url.isEmpty()) {
            call.reject("Keine Adresse zum Herunterladen.");
            return;
        }

        new Thread(() -> {
            try {
                ApkInstaller.downloadAndInstall(getContext(), url);
                call.resolve();
            } catch (Exception e) {
                // Die Rohmeldung nennt Adressen und Pfade; die App macht daraus
                // einen Satz für das Fehlerband.
                call.reject("Das Update ließ sich nicht installieren: " + e.getMessage(), e);
            }
        }, "remotedesktop-update").start();
    }
}
