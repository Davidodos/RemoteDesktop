package app.remotedesktop.client.surfaces

import android.content.Context
import org.json.JSONException
import org.json.JSONObject

/**
 * Wo der Steckbrief liegt und wo der Schlüssel bleibt.
 *
 * Der Steckbrief bekommt eine eigene Ablage — er ist eine Zugabe für die
 * Flächen und hat im Speicher der App nichts verloren. Der **private
 * Geräteschlüssel** wird dagegen nicht kopiert: er wird dort gelesen, wo die App
 * ihn ohnehin hält (die Preferences von Capacitor). Eine zweite Kopie desselben
 * Geheimnisses wäre ein zweiter Ort, an dem es abhandenkommen kann, und beim
 * Entkoppeln ein zweiter, den jemand zu leeren vergisst.
 */
object SurfaceStore {

    private const val FILE = "remotedesktop.surfaces"
    private const val KEY_BOARD = "board"

    /** Ablage und Schlüsselname von `@capacitor/preferences` bzw. `lib/storage.ts`. */
    private const val CAPACITOR_FILE = "CapacitorStorage"
    private const val CAPACITOR_KEY = "remotedesktop.clientKey"

    fun save(context: Context, board: String) {
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_BOARD, board)
            .apply()
    }

    fun board(context: Context): SurfaceBoard? =
        SurfaceBoard.parse(
            context.getSharedPreferences(FILE, Context.MODE_PRIVATE).getString(KEY_BOARD, null),
        )

    /**
     * Der private Teil des Geräteschlüssels, Base64 im PKCS-8-Format.
     *
     * `null` heißt: die App hat noch nie gekoppelt. Dann gibt es auch nichts
     * auszulösen — die Flächen sagen das und tun sonst nichts.
     */
    fun clientKey(context: Context): String? {
        val raw = context.getSharedPreferences(CAPACITOR_FILE, Context.MODE_PRIVATE)
            .getString(CAPACITOR_KEY, null)
            ?: return null

        return try {
            JSONObject(raw).optString("privateKey").takeIf { it.isNotEmpty() }
        } catch (broken: JSONException) {
            null
        }
    }
}
