package app.remotedesktop.client.surfaces

import android.graphics.drawable.Icon
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import app.remotedesktop.client.R

/**
 * Die Kachel in den Schnelleinstellungen: den Rechner wecken oder schlafen
 * legen.
 *
 * Sie kennt zwei Zustände, und welcher gilt, entscheidet der Rechner selbst —
 * gefragt wird, sobald die Kachel sichtbar wird. Läuft er, legt ein Tipp ihn
 * schlafen; schläft er, weckt ihn der Bote aus dem Steckbrief.
 *
 * **Der Weckknopf ist nur da, wenn wirklich jemand wecken kann.** Ein Magic
 * Packet kommt über keinen Router: wenn im Netz des Rechners gerade niemand
 * wach ist, gibt es keinen Boten, und dann ist die Kachel nicht verfügbar statt
 * scheinbar bedienbar.
 */
class WakeTile : TileService() {

    private val main = Handler(Looper.getMainLooper())

    /** Was ein Tipp gerade bedeuten würde. Ergebnis der Prüfung beim Aufklappen. */
    private enum class Mode { SLEEP, WAKE, NONE }

    private var mode = Mode.NONE

    override fun onStartListening() {
        super.onStartListening()

        val board = SurfaceStore.board(applicationContext)

        if (board == null || SurfaceStore.clientKey(applicationContext) == null) {
            apply(Mode.NONE, getString(R.string.tile_unpaired))
            return
        }

        // Bis die Antwort da ist, steht die Kachel auf „prüfe…". Sie mit dem
        // Zustand von vorhin zu zeigen wäre der Fehler, den man auf dem Weg zum
        // Schreibtisch bemerkt: getippt, nichts passiert, Rechner war längst an.
        apply(Mode.NONE, getString(R.string.tile_checking), label = board.deviceName)

        SurfaceWork.background {
            val awake = AgentLink.isAwake(board.node)
            val wake = board.wake

            val next = when {
                awake -> Mode.SLEEP
                wake == null -> Mode.NONE
                AgentLink.isAwake(wake.via) -> Mode.WAKE
                else -> Mode.NONE
            }

            main.post {
                apply(
                    next,
                    when (next) {
                        Mode.SLEEP -> getString(R.string.tile_state_awake)
                        Mode.WAKE -> getString(R.string.tile_state_asleep)
                        Mode.NONE -> if (wake == null) {
                            getString(R.string.tile_no_messenger)
                        } else {
                            getString(R.string.tile_messenger_offline)
                        }
                    },
                    label = board.deviceName,
                )
            }
        }
    }

    override fun onClick() {
        super.onClick()

        val board = SurfaceStore.board(applicationContext)
        val key = SurfaceStore.clientKey(applicationContext)

        if (board == null || key == null || mode == Mode.NONE) {
            return
        }

        val wake = board.wake

        if (mode == Mode.SLEEP) {
            SurfaceWork.run(applicationContext, getString(R.string.tile_sleeping, board.deviceName)) {
                AgentLink(board.node, key).sleep()
            }
            return
        }

        if (wake != null) {
            SurfaceWork.run(applicationContext, getString(R.string.tile_waking, board.deviceName)) {
                AgentLink(wake.via, key).wake(wake.mac)
            }
        }
    }

    /** Setzt Zustand und Beschriftung. Muss auf dem Hauptstrang laufen. */
    private fun apply(next: Mode, subtitle: String, label: String? = null) {
        mode = next

        val tile = qsTile ?: return

        tile.state = if (next == Mode.NONE) Tile.STATE_UNAVAILABLE else Tile.STATE_INACTIVE
        tile.label = label ?: getString(R.string.tile_label)
        tile.icon = Icon.createWithResource(this, android.R.drawable.ic_lock_power_off)
        tile.contentDescription = subtitle

        // Die zweite Zeile gibt es erst ab Android 10. Darunter bleibt die
        // Begründung in der Beschreibung für den Screenreader stehen — und in
        // der Meldung, die ein Tipp erzeugt.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            tile.subtitle = subtitle
        }

        tile.updateTile()
    }
}
