package app.remotedesktop.client.host

/**
 * Wer gerade eine Dauerverbindung offen hält.
 *
 * Ohne diese Liste überlebte eine stehende Verbindung ihren eigenen Widerruf:
 * geprüft wird beim Aufbau, und danach nie wieder. Bild- und Eingabe-Socket
 * melden sich hier an; `HostRuntime.revoke` trennt darüber.
 */
class LiveConnections {

    /** Wofür eine Verbindung da ist. Bild und Eingabe laufen getrennt. */
    enum class Kind { SCREEN, INPUT }

    private class Entry(val kind: Kind, val cut: () -> Unit)

    private val gate = Any()
    private val open = HashMap<String, MutableList<Entry>>()

    /**
     * Wer erfahren will, dass sich die Zahl der offenen Verbindungen geändert
     * hat.
     *
     * <p>
     * Gebraucht für die Benachrichtigung des Vordergrunddienstes: sie stand
     * dort mit der eigenen Adresse, unabhängig davon, ob überhaupt jemand
     * verbunden war. Eine Adresse ist keine Nachricht — sie ändert sich nicht,
     * und man kann nichts mit ihr tun. Ob gerade jemand zusieht, schon.
     * </p>
     */
    @Volatile
    var onChange: ((Int) -> Unit)? = null

    /** Wie viele Sockets gerade offen sind — über alle Clients. */
    val count: Int get() = synchronized(gate) { open.values.sumOf { it.size } }

    /** Wie viele davon dieser Art sind — über alle Clients. */
    fun countOf(kind: Kind): Int = synchronized(gate) {
        open.values.sumOf { list -> list.count { it.kind == kind } }
    }

    /** Wie viele Verbindungen dieses eine Gerät offen hat — Bild und Eingabe. */
    fun countFor(clientId: String?): Int = synchronized(gate) {
        if (clientId == null) 0 else open[clientId]?.size ?: 0
    }

    /**
     * Meldet eine Verbindung an — und trennt dabei die vorige derselben Art
     * desselben Geräts: ein Gerät hat genau ein Bild und genau eine Eingabe,
     * und der frische Socket gewinnt, weil an ihm gerade jemand sitzt.
     */
    fun register(clientId: String?, kind: Kind, cut: () -> Unit): () -> Unit {
        if (clientId == null) {
            return {}
        }

        val entry = Entry(kind, cut)

        // Erst herausnehmen, dann trennen: der Rückruf schließt einen Socket,
        // und dessen Thread meldet sich gleich hier zurück, um sich abzumelden.
        // Innerhalb des Schlosses wäre das ein Selbstgespräch mit Nachschlüssel.
        val abgelöst = synchronized(gate) {
            val list = open.getOrPut(clientId) { mutableListOf() }
            val alt = list.filter { it.kind == kind }

            list.removeAll(alt)
            list.add(entry)

            alt
        }

        abgelöst.forEach { runCatching(it.cut) }

        announce()

        return {
            synchronized(gate) {
                open[clientId]?.remove(entry)
            }

            announce()
        }
    }

    /** Außerhalb des Schlosses: der Zuhörer darf hier alles tun, auch fragen. */
    private fun announce() {
        onChange?.invoke(count)
    }

    /** @return Wie viele Verbindungen dabei getrennt wurden. */
    fun close(clientId: String): Int {
        val cuts = synchronized(gate) { open.remove(clientId).orEmpty() }

        cuts.forEach { runCatching(it.cut) }
        announce()

        return cuts.size
    }

    fun closeAll() = synchronized(gate) {
        open.keys.toList().forEach { close(it) }
    }
}
