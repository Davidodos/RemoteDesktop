package app.remotedesktop.client;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * Lädt eine APK und reicht sie an den {@link PackageInstaller} weiter.
 *
 * Bewusst über eine Installer-Sitzung und nicht über eine Datei plus
 * {@code FileProvider}: die Sitzung nimmt einen Strom entgegen, sodass die APK
 * nie im Dateisystem der App landet, wo sie nach einem abgebrochenen Vorgang
 * liegen bliebe.
 */
final class ApkInstaller {

    /** Wie viel auf einmal kopiert wird. */
    private static final int BUFFER_SIZE = 64 * 1024;

    /** Genug für einen langsamen Mobilfunk-Download, kurz genug zum Aufgeben. */
    private static final int TIMEOUT_MS = 60_000;

    private ApkInstaller() {
    }

    static void downloadAndInstall(Context context, String url) throws IOException {
        PackageInstaller installer = context.getPackageManager().getPackageInstaller();

        PackageInstaller.SessionParams params =
                new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);

        int sessionId = installer.createSession(params);

        try (PackageInstaller.Session session = installer.openSession(sessionId)) {
            copyInto(session, url);
            session.commit(confirmationIntent(context, sessionId).getIntentSender());
        } catch (IOException | RuntimeException e) {
            // Eine offene Sitzung bliebe sonst stehen und zählte gegen das
            // Limit an gleichzeitigen Installationen.
            installer.abandonSession(sessionId);
            throw e;
        }
    }

    private static void copyInto(PackageInstaller.Session session, String url) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();

        connection.setConnectTimeout(TIMEOUT_MS);
        connection.setReadTimeout(TIMEOUT_MS);
        connection.setInstanceFollowRedirects(true);

        try {
            int status = connection.getResponseCode();

            if (status != HttpURLConnection.HTTP_OK) {
                throw new IOException("Der Server antwortete mit HTTP " + status + ".");
            }

            long erwartet = connection.getContentLengthLong();
            long geschrieben = 0;

            try (InputStream source = connection.getInputStream();
                 OutputStream target = session.openWrite("apk", 0, erwartet)) {

                byte[] buffer = new byte[BUFFER_SIZE];
                int gelesen;

                while ((gelesen = source.read(buffer)) > 0) {
                    target.write(buffer, 0, gelesen);
                    geschrieben += gelesen;
                }

                session.fsync(target);
            }

            // Ein abgebrochener Download ergäbe eine halbe APK. Android würde
            // sie ablehnen, aber die Meldung dafür hilft niemandem weiter.
            if (erwartet > 0 && geschrieben != erwartet) {
                throw new IOException("Der Download ist unvollständig geblieben.");
            }
        } finally {
            connection.disconnect();
        }
    }

    /**
     * Der Rückkanal für den Systemdialog. Er muss angegeben werden, auch wenn
     * die App das Ergebnis nicht auswertet — ohne ihn erscheint die Rückfrage
     * gar nicht erst.
     */
    private static PendingIntent confirmationIntent(Context context, int sessionId) {
        Intent intent = new Intent(context, MainActivity.class);

        return PendingIntent.getActivity(
                context,
                sessionId,
                intent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
    }
}
