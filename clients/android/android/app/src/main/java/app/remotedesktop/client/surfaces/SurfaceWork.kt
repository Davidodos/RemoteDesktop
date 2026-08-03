package app.remotedesktop.client.surfaces

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.widget.Toast
import java.util.concurrent.Executors

/**
 * Führt aus, was eine Fläche angestoßen hat — im Hintergrund, mit sichtbarem
 * Ausgang.
 *
 * **Ein Tipp, ein Versuch.** Der Plan sah hier den `WorkManager` vor; der
 * bringt Wiederholungen mit, und genau die sind hier falsch: eine Aktion startet
 * ein Programm oder drückt Tasten, und beides ein zweites Mal auszuführen ist
 * kein Wiederherstellen, sondern ein zweiter Eingriff. Ein Rundruf hat gut zehn
 * Sekunden Zeit — mehr als genug für drei Anfragen über Tailscale.
 *
 * **Und er scheitert sichtbar.** Ein Widget-Tipp, der still nichts tut, ist das,
 * was man sich von einer Fernsteuerung am wenigsten wünscht.
 */
object SurfaceWork {

    private const val TAG = "Surfaces"

    /**
     * Ein einzelner Strang: die Aufrufe sollen sich nicht überholen, wenn
     * jemand zweimal hintereinander tippt.
     */
    private val pool = Executors.newSingleThreadExecutor()

    private val main = Handler(Looper.getMainLooper())

    /**
     * @param success was gemeldet wird, wenn es geklappt hat
     * @param done läuft in jedem Fall, auch bei einem Fehler — daran hängt das
     *   Ende des Rundrufs bzw. der Kurzaktivität
     */
    fun run(context: Context, success: String, done: () -> Unit = {}, task: () -> Unit) {
        val app = context.applicationContext

        pool.execute {
            val message = try {
                task()
                success
            } catch (failure: Exception) {
                // Absichtlich weit gefasst: hier stehen Netzfehler, kaputte
                // Schlüssel und Antworten, die kein JSON sind, gleichberechtigt
                // nebeneinander — und keiner davon darf den Prozess mitnehmen,
                // der zufällig gerade die App trägt.
                Log.w(TAG, "Fläche konnte nichts ausrichten.", failure)
                failure.message ?: "Es hat nicht geklappt."
            }

            main.post {
                Toast.makeText(app, message, Toast.LENGTH_SHORT).show()
                done()
            }
        }
    }

    /** Sagt etwas, ohne etwas zu tun — für Fälle, in denen es nichts zu tun gibt. */
    fun report(context: Context, message: String, done: () -> Unit = {}) {
        val app = context.applicationContext

        main.post {
            Toast.makeText(app, message, Toast.LENGTH_SHORT).show()
            done()
        }
    }

    /** Nur im Hintergrund arbeiten, ohne Meldung — für die Zustandsprüfung der Kachel. */
    fun background(task: () -> Unit) {
        pool.execute {
            try {
                task()
            } catch (failure: Exception) {
                Log.w(TAG, "Hintergrundarbeit einer Fläche fehlgeschlagen.", failure)
            }
        }
    }

    /**
     * Der gemeinsame Weg von Widget und Kürzel: Steckbrief und Schlüssel holen,
     * Aktion auslösen, Ergebnis melden.
     */
    fun invoke(context: Context, actionId: String, done: () -> Unit = {}) {
        val board = SurfaceStore.board(context)
        val key = SurfaceStore.clientKey(context)
        val label = board?.actions?.find { it.id == actionId }?.label ?: actionId

        if (board == null || key == null) {
            report(context, "Kein gekoppeltes Gerät. Einmal die App öffnen und verbinden.", done)
            return
        }

        run(context, "$label ausgelöst.", done) {
            AgentLink(board.node, key).invokeAction(actionId)
        }
    }
}
