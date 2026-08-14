package app.remotedesktop.client.host

import kotlin.concurrent.thread
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Jede Verbindung wird am Handy einzeln bestätigt. Der Teil, auf den es
 * ankommt, ist nicht die Zustimmung, sondern das Gegenteil: alles, was keine
 * ausdrückliche Zustimmung ist, muss ein Nein sein.
 */
class ConnectionRequestsTest {

    @Test
    fun `ohne Oberflaeche wird abgelehnt`() {
        // Der Host läuft nur, solange die App offen ist — „niemand da" ist ein
        // Zustand, der nicht vorkommen soll und im Zweifel verschlossen bleibt.
        assertFalse(ConnectionRequests().ask("Arbeitsrechner"))
    }

    @Test
    fun `wer zustimmt, kommt herein`() {
        val requests = ConnectionRequests()

        requests.listener = { id, _ -> thread { requests.answer(id, true) } }

        assertTrue(requests.ask("Arbeitsrechner"))
    }

    @Test
    fun `wer ablehnt, kommt nicht herein`() {
        val requests = ConnectionRequests()

        requests.listener = { id, _ -> thread { requests.answer(id, false) } }

        assertFalse(requests.ask("Arbeitsrechner"))
    }

    @Test
    fun `keine Antwort ist ein Nein`() {
        val requests = ConnectionRequests(timeoutMs = 50)

        // Zuhören, aber nicht antworten: genau der Fall, in dem das Handy in
        // der Tasche liegt. Ein Zeitablauf darf nicht durchgehen.
        requests.listener = { _, _ -> }

        assertFalse(requests.ask("Arbeitsrechner"))
    }

    @Test
    fun `die Frage verschwindet, sobald sie erledigt ist`() {
        val requests = ConnectionRequests(timeoutMs = 50)
        val settled = ArrayList<String>()

        requests.listener = { _, _ -> }
        requests.onSettled = { id -> settled.add(id) }

        requests.ask("Arbeitsrechner")

        // Sonst bliebe die Karte auf dem Bildschirm stehen, obwohl die
        // Gegenseite längst aufgegeben hat.
        assertEquals(1, settled.size)
        assertEquals(0, requests.openCount)
    }

    @Test
    fun `eine Antwort auf eine abgelaufene Frage ist folgenlos`() {
        val requests = ConnectionRequests(timeoutMs = 50)

        requests.listener = { _, _ -> }
        requests.ask("Arbeitsrechner")

        // Wer eine Sekunde zu spät tippt, soll keine Ausnahme auslösen.
        requests.answer("gibt-es-nicht", true)
    }

    @Test
    fun `der Name der Gegenseite steht in der Frage`() {
        val requests = ConnectionRequests()
        var seen: String? = null

        requests.listener = { id, label ->
            seen = label
            thread { requests.answer(id, true) }
        }

        requests.ask("Arbeitsrechner")

        // Ohne ihn stünde auf der Karte „ein Gerät möchte verbinden" — und die
        // einzige sinnvolle Antwort darauf wäre Nein.
        assertEquals("Arbeitsrechner", seen)
    }
}
