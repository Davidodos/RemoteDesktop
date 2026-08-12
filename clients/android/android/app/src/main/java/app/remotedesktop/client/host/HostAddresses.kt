package app.remotedesktop.client.host

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.os.Build
import java.net.Inet4Address
import java.net.NetworkInterface

/**
 * Unter welcher Adresse dieses Handy wirklich erreichbar ist.
 *
 * <p>
 * **Der Befund dahinter:** vorher wurden alle Adressen aller Schnittstellen
 * aufgezählt, die „oben" sind. Auf einem Handy sind das drei bis fünf — WLAN,
 * Mobilfunk, dazu Tunnel und Attrappen, die Android für sich selbst führt. In
 * den Einstellungen standen sie alle nebeneinander, und **keine** davon
 * funktionierte zum Abtippen: die richtige ging in den anderen unter, und die
 * anderen führen ins Nichts. Eine Liste, aus der man raten muss, ist schlechter
 * als eine Angabe.
 * </p>
 *
 * <p>
 * Gefragt wird deshalb das System, welches Netz gerade das aktive ist, und
 * dessen Adresse zählt. Nur wenn das nichts hergibt, wird noch einmal über die
 * Schnittstellen gegangen — dann aber ohne alles, was ohnehin nicht in Frage
 * kommt.
 * </p>
 */
object HostAddresses {

    /**
     * Schnittstellen, die nie eine brauchbare Adresse tragen: Attrappen des
     * Systems, der IMS-Kanal des Mobilfunks und die Rückkopplung.
     */
    private val IGNORED = listOf("dummy", "rmnet_ims", "lo", "p2p", "ap")

    /**
     * Die Adresse, unter der ein anderes Gerät dieses hier erreicht — oder
     * `null`, wenn es gerade in keinem Netz hängt.
     */
    fun best(context: Context): String? = all(context).firstOrNull()

    /**
     * Alle Adressen, die in Frage kommen, die brauchbarste zuerst.
     *
     * Mehr als eine gibt es, wenn WLAN und ein VPN nebeneinander laufen. Dann
     * ist die Reihenfolge eine Auskunft und keine Aufzählung: vorn steht, was
     * das System gerade benutzt.
     */
    fun all(context: Context): List<String> {
        val active = fromActiveNetwork(context)

        // Was das System als aktives Netz meldet, kommt zuerst — der Rest
        // dahinter, falls jemand zwei Wege hat und den anderen braucht.
        return (active + fromInterfaces()).distinct()
    }

    private fun fromActiveNetwork(context: Context): List<String> {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) {
            return emptyList()
        }

        return runCatching {
            val manager = context.getSystemService(Context.CONNECTIVITY_SERVICE)
                as ConnectivityManager

            val network = manager.activeNetwork ?: return emptyList()
            val properties: LinkProperties = manager.getLinkProperties(network)
                ?: return emptyList()

            properties.linkAddresses
                .map { it.address }
                .filterIsInstance<Inet4Address>()
                .filter(::isUsable)
                .mapNotNull { it.hostAddress }
        }.getOrDefault(emptyList())
    }

    private fun fromInterfaces(): List<String> = runCatching {
        NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback }
            .filter { candidate -> IGNORED.none { candidate.name.startsWith(it) } }
            .flatMap { candidate -> candidate.inetAddresses.toList().map { candidate to it } }
            .filter { (_, address) -> address is Inet4Address && isUsable(address) }
            .mapNotNull { (_, address) -> address.hostAddress }
    }.getOrDefault(emptyList())

    /**
     * Eine Adresse aus dem Bereich 169.254.x.x hat sich das Gerät selbst
     * gegeben, weil kein DHCP antwortete. Über sie erreicht es niemand.
     */
    private fun isUsable(address: Inet4Address): Boolean =
        !address.isLoopbackAddress && !address.isLinkLocalAddress && !address.isAnyLocalAddress
}
