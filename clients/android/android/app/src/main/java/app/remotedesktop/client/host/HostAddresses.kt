package app.remotedesktop.client.host

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.NetworkCapabilities
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
 * anderen führen ins Nichts.
 * </p>
 *
 * <p>
 * **Der zweite Befund:** danach zählte, was das System als *aktives* Netz
 * meldet. Das ist die Antwort auf eine andere Frage. Aktiv ist das Netz, über
 * das dieses Handy ins Internet geht — bei einem WLAN ohne Internet ist das der
 * Mobilfunk. Dessen Adresse stand dann vorn, und über sie erreicht dieses Gerät
 * niemand: sie liegt hinter dem CGNAT des Anbieters, der eingehende
 * Verbindungen gar nicht erst durchlässt. Angezeigt wurde also verlässlich die
 * eine Adresse, die nicht funktioniert.
 * </p>
 *
 * <p>
 * Gefragt wird deshalb nicht mehr, worüber das Handy hinausgeht, sondern worüber
 * es hereingelassen wird — und das sagt der Transport des Netzes, nicht seine
 * Rolle: ein VPN zuerst, weil seine Adresse bleibt, dann WLAN und Ethernet,
 * Mobilfunk zuletzt und nur, wenn es sonst nichts gibt.
 * </p>
 */
object HostAddresses {

    /**
     * Schnittstellen, die nie eine brauchbare Adresse tragen: Attrappen des
     * Systems, der IMS-Kanal des Mobilfunks und die Rückkopplung.
     */
    private val IGNORED = listOf("dummy", "rmnet_ims", "lo", "p2p", "ap")

    /**
     * Woran ein anderes Gerät hier ankommt — in der Reihenfolge, in der es
     * gelingt.
     *
     * Die Reihenfolge ist die ganze Aussage: `ordinal` entscheidet, was in den
     * Steckbrief und in die Anzeige kommt.
     */
    enum class Reach {
        /**
         * Tailscale, WireGuard und alles andere mit `TRANSPORT_VPN`. Zuerst,
         * weil diese Adresse bleibt: sie hängt nicht am Netz, in dem das Handy
         * gerade steht, und überlebt den Weg von zuhause ins Büro.
         */
        VPN,

        /** WLAN und Ethernet — im selben Netz erreichbar, solange man dort ist. */
        LOCAL,

        /**
         * Mobilfunk. Steht nur hier, damit die Anzeige nicht leer bleibt; als
         * Ziel taugt die Adresse fast nie, siehe [isRoutable].
         */
        CELLULAR,
    }

    /** Eine Adresse mit der Auskunft, was sie wert ist. */
    data class Candidate(val address: String, val reach: Reach)

    /**
     * Die Adresse, unter der ein anderes Gerät dieses hier erreicht — oder
     * `null`, wenn es gerade in keinem Netz hängt.
     */
    fun best(context: Context): String? = all(context).firstOrNull()

    /**
     * Alle Adressen, die in Frage kommen, die brauchbarste zuerst.
     *
     * Mehr als eine gibt es, wenn WLAN und ein VPN nebeneinander laufen. Dann
     * ist die Reihenfolge eine Auskunft und keine Aufzählung.
     */
    fun all(context: Context): List<String> = rank(candidates(context))

    /**
     * Nur die Adressen, unter denen dieses Gerät wirklich erreichbar ist.
     *
     * Das ist die Liste für den Steckbrief und für das Zertifikat. Eine
     * Mobilfunkadresse gehört in keins von beidem: sie steht im Zertifikat als
     * Versprechen, das niemand einlösen kann, und im Steckbrief als Adresse, an
     * der die Gegenseite hängen bleibt.
     */
    fun routable(context: Context): List<String> =
        rank(candidates(context).filter { isRoutable(it.reach) })

    /**
     * Ob über diese Art Netz überhaupt jemand hereinkommt.
     *
     * Mobilfunk nicht: die Adresse liegt beim Anbieter hinter einem NAT, das
     * eingehende Verbindungen nicht durchlässt. Sie anzuzeigen ist eine
     * Auskunft, sie zu benutzen eine Sackgasse.
     */
    fun isRoutable(reach: Reach): Boolean = reach != Reach.CELLULAR

    /**
     * Bringt die Kandidaten in die Reihenfolge, in der sie gelingen — und wirft
     * Doppelte weg.
     *
     * Getrennt vom Einsammeln, weil das `Context` braucht und diese Regel nicht:
     * so steht der Teil unter Test, in dem die Entscheidung fällt.
     */
    internal fun rank(candidates: List<Candidate>): List<String> = candidates
        .sortedBy { it.reach.ordinal }
        .map { it.address }
        .distinct()

    /**
     * Alle Netze, die dieses Gerät gerade hat — nicht nur das aktive.
     *
     * Über `allNetworks`, weil ein Handy mehrere gleichzeitig führt und das
     * aktive das falsche ist: siehe oben. Gibt das nichts her, bleibt der Weg
     * über die Schnittstellen; dort fehlt die Auskunft über den Transport, und
     * alles gilt als lokal.
     */
    private fun candidates(context: Context): List<Candidate> {
        val fromNetworks = fromNetworks(context)

        return fromNetworks.ifEmpty { fromInterfaces().map { Candidate(it, Reach.LOCAL) } }
    }

    private fun fromNetworks(context: Context): List<Candidate> {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) {
            return emptyList()
        }

        return runCatching {
            val manager = context.getSystemService(Context.CONNECTIVITY_SERVICE)
                as ConnectivityManager

            manager.allNetworks.flatMap { network ->
                val capabilities = manager.getNetworkCapabilities(network)
                val properties: LinkProperties? = manager.getLinkProperties(network)

                if (capabilities == null || properties == null) {
                    return@flatMap emptyList()
                }

                val reach = reachOf(capabilities) ?: return@flatMap emptyList()

                properties.linkAddresses
                    .map { it.address }
                    .filterIsInstance<Inet4Address>()
                    .filter(::isUsable)
                    .mapNotNull { it.hostAddress }
                    .map { Candidate(it, reach) }
            }
        }.getOrDefault(emptyList())
    }

    /**
     * Was für ein Netz das ist. `null` bei allem, was kein Weg zu diesem Gerät
     * ist — Bluetooth-Kopplungen und was Android sonst noch führt.
     *
     * Auf VPN wird zuerst geprüft: ein Tailscale-Netz trägt daneben oft noch
     * den Transport des Netzes, auf dem es aufsitzt.
     */
    private fun reachOf(capabilities: NetworkCapabilities): Reach? = when {
        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN) -> Reach.VPN

        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
            capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) -> Reach.LOCAL

        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> Reach.CELLULAR

        else -> null
    }

    private fun fromInterfaces(): List<String> = runCatching {
        NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback }
            .filter { candidate -> IGNORED.none { candidate.name.startsWith(it) } }
            .flatMap { candidate -> candidate.inetAddresses.toList() }
            .filter { address -> address is Inet4Address && isUsable(address) }
            .mapNotNull { it.hostAddress }
    }.getOrDefault(emptyList())

    /**
     * Eine Adresse aus dem Bereich 169.254.x.x hat sich das Gerät selbst
     * gegeben, weil kein DHCP antwortete. Über sie erreicht es niemand.
     */
    private fun isUsable(address: Inet4Address): Boolean =
        !address.isLoopbackAddress && !address.isLinkLocalAddress && !address.isAnyLocalAddress
}
