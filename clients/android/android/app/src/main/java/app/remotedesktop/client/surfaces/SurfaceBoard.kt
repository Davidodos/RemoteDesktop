package app.remotedesktop.client.surfaces

import org.json.JSONException
import org.json.JSONObject

/**
 * Der Steckbrief, den die App für die Flächen hinterlegt.
 *
 * Gegenstück zu `app/src/lib/surfaceBoard.ts`. Er enthält alles, was Widget,
 * Tile und Kürzel zum Auslösen brauchen — bis auf den privaten Schlüssel: der
 * bleibt, wo die App ihn hat (siehe [SurfaceStore]).
 */
data class SurfaceBoard(
    val deviceId: String,
    val deviceName: String,
    val node: Node,
    val actions: List<Action>,
    /** Fehlt, wenn dieser Rechner von hier aus nicht geweckt werden kann. */
    val wake: Wake?,
) {
    data class Node(val host: String, val port: Int, val clientId: String)

    data class Action(val id: String, val label: String)

    /** `via` ist der Bote, nicht das Ziel — ein schlafender Rechner hört nichts. */
    data class Wake(val mac: String, val via: Node)

    companion object {
        /**
         * `null` heißt: keine Flächen. Das ist kein Fehler, sondern der Zustand
         * vor der ersten Verbindung — und der Zustand, den die App herstellt,
         * wenn ein Gerät nicht mehr gekoppelt ist.
         */
        fun parse(json: String?): SurfaceBoard? {
            if (json.isNullOrEmpty()) {
                return null
            }

            return try {
                read(JSONObject(json))
            } catch (broken: JSONException) {
                // Hier steht, was eine frühere Fassung der App hinterlassen hat.
                // Ein Widget, das daran abstürzt, wäre die schlechteste aller
                // Antworten — es zeigt dann eben nichts.
                null
            }
        }

        private fun read(root: JSONObject): SurfaceBoard {
            val actions = root.optJSONArray("actions")

            return SurfaceBoard(
                deviceId = root.getString("deviceId"),
                deviceName = root.getString("deviceName"),
                node = node(root.getJSONObject("node")),
                actions = (0 until (actions?.length() ?: 0)).map { index ->
                    val action = actions!!.getJSONObject(index)
                    Action(action.getString("id"), action.getString("label"))
                },
                wake = root.optJSONObject("wake")?.let { wake ->
                    Wake(wake.getString("mac"), node(wake.getJSONObject("via")))
                },
            )
        }

        private fun node(json: JSONObject): Node =
            Node(json.getString("host"), json.getInt("port"), json.getString("clientId"))
    }
}
