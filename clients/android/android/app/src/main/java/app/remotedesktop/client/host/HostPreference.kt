package app.remotedesktop.client.host

import android.content.Context

/**
 * Was dieses Handy über sich selbst weiß: sein Name, und ob es ferngesteuert
 * werden darf.
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
    private const val KEY_NAME = "deviceName"
    private const val KEY_ASKED = "firstRunDone"
    private const val KEY_SCREEN = "screenAllowed"

    /** Länger nennt sich kein Gerät — wie `DeviceProfile.MAX_NAME`. */
    const val MAX_NAME = 64

    fun isEnabled(context: Context): Boolean =
        preferences(context).getBoolean(KEY, false)

    fun set(context: Context, enabled: Boolean) {
        preferences(context).edit().putBoolean(KEY, enabled).apply()
    }

    /**
     * Ob dieses Handy sein Bild herausgibt.
     *
     * <p>
     * **Eine Einstellung und keine Aufnahme.** Sie steht in `/api/info` und
     * sagt der Gegenseite, dass es möglich ist. Der Systemdialog der
     * Bildschirmaufnahme kommt erst, wenn wirklich jemand zusehen will — vorher
     * wäre er eine Frage nach einer Erlaubnis für nichts, und Android nimmt sie
     * ohnehin beim nächsten Neustart zurück.
     * </p>
     *
     * <p>
     * **Vorgabe: aus** — wie bei der Freigabe selbst. Was von außen zu sehen
     * ist, soll durch eine Entscheidung sichtbar geworden sein.
     * </p>
     */
    fun isScreenAllowed(context: Context): Boolean =
        preferences(context).getBoolean(KEY_SCREEN, false)

    fun setScreenAllowed(context: Context, allowed: Boolean) {
        preferences(context).edit().putBoolean(KEY_SCREEN, allowed).apply()
    }

    /**
     * Der gewählte Gerätename, oder `null`, solange keiner gewählt wurde.
     *
     * Ein eigener Wert und nicht der aus den Android-Einstellungen: er steht in
     * fremden Gerätelisten, und dort will man „Handy" lesen und nicht „Pixel 8".
     * Der Rückfall auf den Systemnamen steht in `HostRuntime.deviceNameOf`.
     */
    fun deviceName(context: Context): String? =
        sanitize(preferences(context).getString(KEY_NAME, null))

    fun setDeviceName(context: Context, name: String) {
        preferences(context).edit().putString(KEY_NAME, sanitize(name)).apply()
    }

    /**
     * Ob der Erststart schon durch ist. Er fragt nach dem Namen und nach der
     * Freigabe — beides genau einmal, danach führt der Weg über die
     * Einstellungen.
     */
    fun firstRunDone(context: Context): Boolean =
        preferences(context).getBoolean(KEY_ASKED, false)

    fun markFirstRunDone(context: Context) {
        preferences(context).edit().putBoolean(KEY_ASKED, true).apply()
    }

    private fun sanitize(name: String?): String? =
        name?.filterNot { it.isISOControl() }?.trim()?.take(MAX_NAME)?.trim()
            ?.takeIf { it.isNotEmpty() }

    private fun preferences(context: Context) =
        context.applicationContext.getSharedPreferences(FILE, Context.MODE_PRIVATE)
}
