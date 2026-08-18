package app.remotedesktop.client;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.util.Log;
import android.webkit.WebView;

import java.io.File;

/**
 * Nach einem Update läuft nichts Altes mehr mit.
 *
 * <p>
 * <b>Der Befund dahinter:</b> die Oberfläche liegt als Datei in der APK und wird
 * von der WebView über {@code https://localhost} geladen — also über einen Weg,
 * auf dem ein HTTP-Zwischenspeicher greift. Eine neue APK brachte die neue
 * Oberfläche mit, die WebView zeigte aber weiter die aus ihrem Zwischenspeicher.
 * Sichtbar war das als eine App, die jeder Änderung genau einen Start
 * hinterherhinkte: was am Handy geprüft wurde, war die vorletzte Fassung.
 * </p>
 *
 * <p>
 * <b>Was bleibt:</b> alles, was jemand eingestellt oder gekoppelt hat. Der
 * {@code localStorage} mit der Geräteliste, die Preferences mit Name und
 * Freigaben, der Ordner {@code host/} mit Schlüsseln, Zertifikat und
 * {@code clients.json}. Weg ist ausschließlich Zwischengespeichertes — also das,
 * was sich jederzeit neu herstellen lässt und deshalb nichts verliert.
 * </p>
 */
final class UpgradeCleanup {

    private static final String TAG = "UpgradeCleanup";

    private static final String FILE = "install";
    private static final String KEY_VERSION = "versionCode";

    private UpgradeCleanup() {
    }

    /**
     * Räumt auf, wenn seit dem letzten Start eine andere Fassung installiert
     * wurde.
     *
     * Verglichen wird auf Ungleichheit und nicht auf „neuer": ein Zurückrollen
     * auf eine ältere Fassung hinterlässt denselben veralteten Zwischenspeicher,
     * nur andersherum.
     *
     * @param webView Die Brücke der App — {@code null}, wenn es sie noch nicht
     *   gibt; dann bleibt es beim Rest.
     */
    static void runIfUpgraded(Context context, WebView webView) {
        long installed = versionCodeOf(context);
        SharedPreferences preferences =
                context.getSharedPreferences(FILE, Context.MODE_PRIVATE);

        long remembered = preferences.getLong(KEY_VERSION, -1);

        if (remembered == installed) {
            return;
        }

        // Zuerst merken: klappt das Aufräumen nur halb, soll es beim nächsten
        // Start nicht endlos wiederholt werden.
        preferences.edit().putLong(KEY_VERSION, installed).apply();

        // Beim allerersten Start gibt es nichts Altes — dann ist das Merken
        // der ganze Zweck des Aufrufs.
        if (remembered < 0) {
            return;
        }

        Log.i(TAG, "Neue Fassung (" + installed + ") — Zwischenspeicher wird geleert.");

        if (webView != null) {
            // true: auch das, was auf der Platte liegt. Ohne das bleibt genau
            // der Teil stehen, der einen Neustart überlebt — also der, der das
            // Problem war.
            webView.clearCache(true);
            webView.clearHistory();
        }

        deleteTree(new File(context.getCacheDir(), "org.chromium.android_webview"));
        deleteTree(context.getCodeCacheDir());
    }

    private static long versionCodeOf(Context context) {
        try {
            PackageInfo info = context.getPackageManager()
                    .getPackageInfo(context.getPackageName(), 0);

            return info.getLongVersionCode();
        } catch (PackageManager.NameNotFoundException impossible) {
            return -1;
        }
    }

    /**
     * Fehlschläge bleiben still: ein Zwischenspeicher, an den die App gerade
     * nicht herankommt, ist kein Grund, den Start zu verweigern.
     */
    private static void deleteTree(File target) {
        if (target == null || !target.exists()) {
            return;
        }

        File[] children = target.listFiles();

        if (children != null) {
            for (File child : children) {
                deleteTree(child);
            }
        }

        if (!target.delete()) {
            Log.d(TAG, "Nicht gelöscht: " + target);
        }
    }
}
