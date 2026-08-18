package app.remotedesktop.client;

import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.provider.Settings;

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
 *
 * <p>
 * <b>Die Zusage lautet erst dann „installiert", wenn es installiert ist.</b>
 * Vorher galt der Aufruf als erledigt, sobald die APK bei Android abgegeben war
 * — also lange vor jedem Dialog. Die Oberfläche schrieb „Android fragt gleich
 * nach" und blieb dabei stehen, gleich ob jemand bestätigte, ablehnte oder gar
 * nichts sah. Jetzt wartet der Aufruf auf {@link InstallReceiver}.
 * </p>
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
     * Lädt die APK, übergibt sie an die {@code PackageInstaller}-Sitzung und
     * wartet auf das Ergebnis.
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

        // Erst der Schalter, dann der Download. Andersherum lädt das Handy
        // Dutzende Megabyte, um sie danach wegzuwerfen — und der Grund dafür
        // stünde nirgends.
        if (!ApkInstaller.mayInstall(getContext())) {
            openUnknownSources();

            call.reject(
                    "Dieses Handy erlaubt RemoteDesktop noch nicht, Apps zu installieren. "
                            + "Der Schalter dafür ist gerade aufgegangen — einschalten und "
                            + "das Update noch einmal starten.");

            return;
        }

        new Thread(() -> {
            InstallReceiver.await(failure -> {
                if (failure == null) {
                    call.resolve();
                } else {
                    call.reject(failure);
                }
            });

            try {
                ApkInstaller.downloadAndInstall(getContext(), url);
            } catch (Exception e) {
                // Der Wartende wird abgeräumt: sonst hinge er an einer Sitzung,
                // die es nicht mehr gibt, und der nächste Versuch fände ihn vor.
                InstallReceiver.await(null);

                // Die Rohmeldung nennt Adressen und Pfade; die App macht daraus
                // einen Satz für das Fehlerband.
                call.reject("Das Update ließ sich nicht installieren: " + e.getMessage(), e);
            }
        }, "remotedesktop-update").start();
    }

    /**
     * Führt in die Systemeinstellung „Unbekannte Apps installieren".
     *
     * Mehr kann eine App hier nicht tun: umlegen muss den Schalter ein Mensch.
     * Ein Fehlschlag bleibt still — auf manchen Geräten gibt es diese Seite
     * nicht, und dann ist der Satz in der Meldung immer noch der Weg.
     */
    private void openUnknownSources() {
        Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES)
                .setData(Uri.parse("package:" + getContext().getPackageName()))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

        try {
            getContext().startActivity(intent);
        } catch (RuntimeException ignored) {
            // Kein Weg dorthin — die Meldung sagt trotzdem, worum es geht.
        }
    }
}
