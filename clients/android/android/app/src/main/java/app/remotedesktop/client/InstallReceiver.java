package app.remotedesktop.client;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.os.Build;

/**
 * Der Rückkanal des {@link PackageInstaller} — und der Grund, warum ein Update
 * am Handy vorher nichts tat.
 *
 * <p>
 * <b>Der Befund dahinter (18.08.2026):</b> die App lud die APK, übergab sie an
 * eine Installer-Sitzung und legte als Rückkanal einen {@code PendingIntent} auf
 * die {@code MainActivity}. Damit war der Download getan und der Bildschirm sagte
 * „Android fragt gleich nach" — aber es fragte nie jemand. Android beantwortet
 * eine {@code commit()}-Sitzung außerhalb von Google Play zuerst mit
 * {@link PackageInstaller#STATUS_PENDING_USER_ACTION}, und darin steckt unter
 * {@link Intent#EXTRA_INTENT} der Bestätigungsdialog als Absicht, die
 * <b>die App selbst starten muss</b>. Wer sie nicht startet, bekommt keinen
 * Dialog — und keinen Fehler. Genau das war zu sehen.
 * </p>
 *
 * <p>
 * Nicht freigegeben: das System schickt hier nur, was zu einer Sitzung dieser
 * App gehört. Freigegeben könnte jede App auf dem Handy eine Installation
 * vortäuschen.
 * </p>
 */
public class InstallReceiver extends BroadcastReceiver {

    /** Woran das System diesen Rückkanal erkennt. */
    static final String ACTION = "app.remotedesktop.client.INSTALL_STATUS";

    /**
     * Wer auf das Ergebnis wartet — höchstens einer, weil höchstens eine
     * Installation gleichzeitig läuft.
     *
     * <p>
     * Statisch, weil der Receiver vom System gebaut wird und nichts übergeben
     * bekommt. Das ist tragfähig, solange es genau einen Wartenden gibt: die
     * App bietet einen Knopf an, und der ist gesperrt, solange etwas läuft.
     * </p>
     */
    public interface Outcome {
        /** @param failure {@code null} heißt: hat geklappt. */
        void settled(String failure);
    }

    private static volatile Outcome waiting;

    static void await(Outcome outcome) {
        waiting = outcome;
    }

    private static void settle(String failure) {
        Outcome outcome = waiting;

        // Nur einmal. Ein zweiter Statusbericht zu derselben Sitzung — Android
        // schickt bei manchen Fassungen einen nach dem Neustart der App —
        // liefe sonst in eine Zusage, die längst beantwortet ist.
        waiting = null;

        if (outcome != null) {
            outcome.settled(failure);
        }
    }

    @Override
    public void onReceive(Context context, Intent intent) {
        int status = intent.getIntExtra(
                PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);

        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            showConfirmation(context, intent);
            return;
        }

        if (status == PackageInstaller.STATUS_SUCCESS) {
            settle(null);
            return;
        }

        String message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);

        settle(describe(status, message));
    }

    /**
     * Den Bestätigungsdialog des Systems öffnen.
     *
     * <p>
     * {@code FLAG_ACTIVITY_NEW_TASK} ist Pflicht: ein Receiver hat keine
     * Aufgabe, in die sich eine Activity legen ließe. Der Start gelingt, weil
     * die App in diesem Augenblick im Vordergrund steht — jemand hat gerade auf
     * „Jetzt installieren" getippt.
     * </p>
     */
    private void showConfirmation(Context context, Intent intent) {
        // Die typisierte Fassung gibt es erst ab Android 13; darunter bleibt nur
        // die alte, und ein Rückfall darauf ist kein Versäumnis, sondern die
        // einzige Variante, die dort existiert.
        Intent confirmation = Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                ? intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent.class)
                : legacyExtra(intent);

        if (confirmation == null) {
            settle("Android hat den Bestätigungsdialog nicht mitgeschickt.");
            return;
        }

        confirmation.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

        try {
            context.startActivity(confirmation);
        } catch (RuntimeException broken) {
            settle("Der Bestätigungsdialog ließ sich nicht öffnen: " + broken.getMessage());
        }
    }

    @SuppressWarnings("deprecation")
    private static Intent legacyExtra(Intent intent) {
        return intent.getParcelableExtra(Intent.EXTRA_INTENT);
    }

    /**
     * Aus der Statusnummer wird ein Satz, der weiterhilft.
     *
     * Die Rohmeldung des Systems ist mitunter leer und sonst englisch; für den
     * einen Fall, der wirklich vorkommt — jemand tippt „Abbrechen" —, wäre sie
     * ohnehin die falsche Erklärung.
     */
    private static String describe(int status, String message) {
        switch (status) {
            case PackageInstaller.STATUS_FAILURE_ABORTED:
                return "Die Installation wurde abgebrochen.";

            case PackageInstaller.STATUS_FAILURE_BLOCKED:
                return "Android hat die Installation blockiert.";

            case PackageInstaller.STATUS_FAILURE_CONFLICT:
                // Der häufigste echte Fehlschlag: eine APK mit anderem
                // Schlüssel. Genau das soll sie auch nicht überschreiben.
                return "Diese Fassung passt nicht über die installierte — "
                        + "sie ist mit einem anderen Schlüssel unterschrieben.";

            case PackageInstaller.STATUS_FAILURE_INCOMPATIBLE:
                return "Diese Fassung passt nicht zu diesem Gerät.";

            case PackageInstaller.STATUS_FAILURE_STORAGE:
                return "Auf dem Gerät ist zu wenig Platz.";

            case PackageInstaller.STATUS_FAILURE_INVALID:
                return "Die geladene Datei ist keine gültige App.";

            default:
                return message == null || message.isEmpty()
                        ? "Die Installation ist fehlgeschlagen."
                        : "Die Installation ist fehlgeschlagen: " + message;
        }
    }
}
