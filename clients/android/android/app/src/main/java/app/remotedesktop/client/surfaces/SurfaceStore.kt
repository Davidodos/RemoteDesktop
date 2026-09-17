package app.remotedesktop.client.surfaces

import android.content.Context
import java.io.File
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

    /** Wo der Host seinen Ausweis führt — siehe `host/HostRuntime.kt`. */
    private const val HOST_FOLDER = "host"
    private const val HOST_KEY_FILE = "clientkey.txt"

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
     * Seit 31h liegt er in `host/clientkey.txt` — derselben Datei, aus der
     * das Host-Plugin ihn der App gibt. Die Preferences bleiben als Rückfall
     * für Installationen von vor 31h; bis zum 18.09.2026 wurden nur sie
     * gelesen, und auf einer frischen Installation sagten Widget, Kachel und
     * Kürzel deshalb immer „noch kein Rechner verbunden".
     *
     * `null` heißt: die App hat noch nie gekoppelt. Dann gibt es auch nichts
     * auszulösen — die Flächen sagen das und tun sonst nichts.
     */
    fun clientKey(context: Context): String? =
        hostKey(context) ?: legacyKey(context)

    /**
     * Nur lesen, nie anlegen: ein Widget-Tipp soll keinen Ausweis erzeugen,
     * den danach niemand kennt. Das Anlegen gehört dem Host (`LocalClientKey`).
     */
    private fun hostKey(context: Context): String? {
        val file = File(File(context.filesDir, HOST_FOLDER), HOST_KEY_FILE)

        if (!file.exists()) {
            return null
        }

        val lines = runCatching { file.readText().trim().lines() }.getOrNull() ?: return null

        return lines.firstOrNull()?.trim()?.takeIf { lines.size == 2 && it.isNotEmpty() }
    }

    private fun legacyKey(context: Context): String? {
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
