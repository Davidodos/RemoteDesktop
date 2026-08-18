package app.remotedesktop.client.host

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/**
 * Die Zustimmung des Menschen am Gerät — einmal je Sitzung.
 *
 * <p>
 * Sie hing bis zum 18.08.2026 an der Anmeldung. Das war die falsche Stelle
 * gleich zweimal: eine Anmeldung sieht nichts und steuert nichts, und sie ist
 * zugleich der Weg, auf dem die Gegenseite die Fassung dieses Geräts abliest —
 * am Handy stand deshalb bei jedem Start der App drüben eine Karte.
 * </p>
 */
class HostSessionTest {

    private fun session() = HostSession("client-1", listOf("screen", "input"))

    @Test
    fun `gefragt wird einmal, danach gilt die Antwort`() {
        val gefragt = AtomicInteger()
        val session = session()

        assertTrue(session.confirmOnce { gefragt.incrementAndGet(); true })
        assertTrue(session.confirmOnce { gefragt.incrementAndGet(); true })

        // Bild und Eingabe gehen fast gleichzeitig auf. Zwei Karten für eine
        // Verbindung wären eine Frage zu viel.
        assertEquals(1, gefragt.get())
    }

    /**
     * Ein „nein" wird nicht gemerkt: wer beim nächsten Versuch zustimmen will,
     * soll gefragt werden. Alles andere hieße, dass ein versehentliches
     * Ablehnen die Sitzung für zwölf Stunden unbrauchbar macht.
     */
    @Test
    fun `ein Nein wird nicht gemerkt`() {
        val gefragt = AtomicInteger()
        val session = session()

        assertFalse(session.confirmOnce { gefragt.incrementAndGet(); false })
        assertTrue(session.confirmOnce { gefragt.incrementAndGet(); true })

        assertEquals(2, gefragt.get())
    }

    /**
     * Der zweite Socket wartet auf die Antwort des ersten, statt eine zweite
     * Karte auszulösen. Ohne die Sperre kämen beide gleichzeitig an der Frage
     * an und beide würden fragen.
     */
    @Test
    fun `zwei Sockets gleichzeitig ergeben eine Frage`() {
        val gefragt = AtomicInteger()
        val session = session()
        val los = CountDownLatch(1)
        val fertig = CountDownLatch(2)

        repeat(2) {
            Thread {
                los.await()

                session.confirmOnce {
                    gefragt.incrementAndGet()

                    // Ein Mensch braucht Sekunden. Hier genügt eine Spanne, in
                    // der der zweite Thread sicher ankommt.
                    Thread.sleep(200)

                    true
                }

                fertig.countDown()
            }.start()
        }

        los.countDown()

        assertTrue(fertig.await(5, TimeUnit.SECONDS))
        assertEquals(1, gefragt.get())
    }
}
