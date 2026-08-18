package app.remotedesktop.client;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.os.Build;

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
 *
 * <p>
 * <b>Der Rückkanal ist kein Beiwerk.</b> Android antwortet auf ein
 * {@code commit()} zuerst mit {@code STATUS_PENDING_USER_ACTION} und schickt den
 * Bestätigungsdialog als Absicht mit — starten muss ihn die App. Vorher zeigte
 * der Rückkanal auf die {@code MainActivity}, die davon nichts wusste: der
 * Download lief durch, und danach passierte sichtbar gar nichts. Jetzt liegt er
 * auf {@link InstallReceiver}.
 * </p>
 */
final class ApkInstaller {

    /** Wie viel auf einmal kopiert wird. */
    private static final int BUFFER_SIZE = 64 * 1024;

    /** Genug für einen langsamen Mobilfunk-Download, kurz genug zum Aufgeben. */
    private static final int TIMEOUT_MS = 60_000;

    private ApkInstaller() {
    }

    /**
     * Ob dieses Gerät der App überhaupt erlaubt, etwas zu installieren.
     *
     * Seit Android 8 ist {@code REQUEST_INSTALL_PACKAGES} im Manifest nur die
     * halbe Miete — die zweite Hälfte ist ein Schalter in den Systemeinstellungen,
     * den ein Mensch umlegen muss. Fehlt er, endet {@code commit()} wortlos, und
     * genau das sah nach einem kaputten Update aus.
     */
    static boolean mayInstall(Context context) {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.O
                || context.getPackageManager().canRequestPackageInstalls();
    }

    static void downloadAndInstall(Context context, String url) throws IOException {
        PackageInstaller installer = context.getPackageManager().getPackageInstaller();

        PackageInstaller.SessionParams params =
                new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);

        // Sagt Android, dass hier eine bestehende App erneuert wird. Ohne die
        // Angabe behandelt es die Sitzung als Erstinstallation und zeigt eine
        // Rückfrage, die mehr behauptet, als geschieht.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            params.setInstallReason(android.content.pm.PackageManager.INSTALL_REASON_USER);
        }

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
     * Der Rückkanal für den Systemdialog.
     *
     * {@code FLAG_MUTABLE} ist Bedingung und keine Nachlässigkeit: das System
     * legt in diese Absicht den Status und den Bestätigungsdialog hinein. Eine
     * unveränderliche Absicht käme leer an, und der Empfänger hätte nichts, was
     * er anzeigen könnte.
     */
    private static PendingIntent confirmationIntent(Context context, int sessionId) {
        Intent intent = new Intent(context, InstallReceiver.class)
                .setAction(InstallReceiver.ACTION)
                .setPackage(context.getPackageName());

        return PendingIntent.getBroadcast(
                context,
                sessionId,
                intent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
    }
}
