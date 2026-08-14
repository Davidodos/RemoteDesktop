package app.remotedesktop.client.host

import android.content.Context

/**
 * Die eine Einstellung: darf dieses Handy ferngesteuert werden?
 *
 * <p>
 * **Warum sie nativ liegt und nicht in der App.** Sie entscheidet über den
 * Lebenslauf des Hosts, und der beginnt und endet mit der Activity — also an
 * einer Stelle, an der keine Weboberfläche läuft. Läge sie im localStorage,
 * müsste die Seite erst starten, um zu sagen, ob der Server starten soll.
 * </p>
 *
 * <p>
 * **Vorgabe: aus.** Ein Handy, das von außen erreichbar ist, soll das durch eine
 * bewusste Entscheidung geworden sein und nicht durch eine Installation.
 * </p>
 */
object HostPreference {

    private const val FILE = "host"
    private const val KEY = "enabled"

    fun isEnabled(context: Context): Boolean =
        preferences(context).getBoolean(KEY, false)

    fun set(context: Context, enabled: Boolean) {
        preferences(context).edit().putBoolean(KEY, enabled).apply()
    }

    private fun preferences(context: Context) =
        context.applicationContext.getSharedPreferences(FILE, Context.MODE_PRIVATE)
}
