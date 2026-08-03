package app.remotedesktop.client.surfaces

import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin

/**
 * Die Brücke, über die die App den Steckbrief für die Flächen hinterlegt.
 *
 * Aufgerufen aus `app/src/platform/capacitor.ts`; dort heißt das
 * `surfaces.publish()`. Die App weiß nicht, dass dahinter ein Widget, eine
 * Kachel und eine Kürzelliste hängen — im Browser und im Windows-Fenster
 * passiert an derselben Stelle nichts.
 */
@CapacitorPlugin(name = "Surfaces")
class SurfacesPlugin : Plugin() {

    @PluginMethod
    fun publish(call: PluginCall) {
        val board = call.getString("board").orEmpty()

        SurfaceStore.save(context, board)

        // Beides sofort und nicht beim nächsten Aufklappen: ein Widget, das
        // einen Rechner zeigt, mit dem man längst nicht mehr arbeitet, führt
        // genau die Tipps aus, die niemand wollte.
        ActionWidget.refresh(context)
        Shortcuts.publish(context, SurfaceBoard.parse(board))

        call.resolve()
    }
}
