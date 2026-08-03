package app.remotedesktop.client.surfaces

import android.app.Activity
import android.os.Bundle

/**
 * Die Weiche hinter einem Kürzel.
 *
 * Ein Kürzel kann nur eine Aktivität starten, keinen Rundruf — deshalb diese
 * hier. Sie zeigt nichts an und ist sofort wieder fort; was der Nutzer sieht,
 * ist die Meldung über den Ausgang. Die eigentliche Arbeit läuft am
 * Anwendungskontext und nicht an dieser Aktivität, sonst wäre sie mit dem
 * `finish()` unter sich zusammengefallen.
 *
 * Nicht nach außen freigegeben: eine Aktivität, die auf Zuruf Programme auf dem
 * PC startet, hat für fremde Apps nichts zu bieten. Das Startprogramm ruft sie
 * über die Kürzel-Schnittstelle des Systems auf, und das reicht ihr.
 */
class ShortcutRelay : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        SurfaceWork.invoke(applicationContext, intent?.getStringExtra(EXTRA_ACTION).orEmpty())

        finish()
    }

    companion object {
        const val EXTRA_ACTION = "action"
    }
}
