package app.remotedesktop.client.surfaces

import android.content.Context
import android.content.Intent
import android.content.pm.ShortcutInfo
import android.content.pm.ShortcutManager
import android.graphics.drawable.Icon
import android.util.Log
import app.remotedesktop.client.R

/**
 * Die Kürzel, die erscheinen, wenn man das App-Symbol gedrückt hält.
 *
 * Sie kommen aus demselben Steckbrief wie das Widget und werden bei jeder
 * Veröffentlichung neu gesetzt — was der Rechner nicht mehr anbietet,
 * verschwindet damit von allein.
 *
 * Der billigste der drei Wege: kein Platz auf dem Startbildschirm, keine
 * Einrichtung, und die Liste hält sich selbst aktuell.
 */
object Shortcuts {

    private const val TAG = "Surfaces"

    /** Die Startleiste zeigt vier bis fünf; darüber wird stillschweigend gekürzt. */
    private const val MAX = 4

    fun publish(context: Context, board: SurfaceBoard?) {
        val manager = context.getSystemService(ShortcutManager::class.java) ?: return
        val limit = minOf(MAX, manager.maxShortcutCountPerActivity)

        val shortcuts = board?.actions.orEmpty()
            .take(limit)
            .map { action -> build(context, action) }

        try {
            manager.dynamicShortcuts = shortcuts
        } catch (tooMany: IllegalArgumentException) {
            // Die Grenze des Startprogramms ist eine Zusage, keine Garantie —
            // manche Hersteller setzen sie tiefer, als sie melden. Ohne Kürzel
            // ist die App vollständig bedienbar; ein Absturz wäre der weitaus
            // höhere Preis.
            Log.w(TAG, "Kürzel abgelehnt.", tooMany)
        }
    }

    private fun build(context: Context, action: SurfaceBoard.Action): ShortcutInfo {
        val intent = Intent(context, ShortcutRelay::class.java)
            .setAction(Intent.ACTION_VIEW)
            .putExtra(ShortcutRelay.EXTRA_ACTION, action.id)

        return ShortcutInfo.Builder(context, action.id)
            .setShortLabel(action.label)
            .setLongLabel(action.label)
            .setIcon(Icon.createWithResource(context, R.mipmap.ic_launcher))
            .setIntent(intent)
            .build()
    }
}
