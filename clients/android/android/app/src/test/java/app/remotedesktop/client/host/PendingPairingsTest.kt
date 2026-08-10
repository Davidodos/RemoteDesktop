package app.remotedesktop.client.host

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * Das Angebot zur Gegenkopplung: einmal abzuholen, kurz gültig, und was nicht
 * vollständig ist, wird gar nicht erst aufgehoben. Dieselben Fälle wie in
 * `agent.Tests/PendingPairingsTests` — ein Handy und ein PC sollen sich hier
 * nicht unterscheiden.
 */
class PendingPairingsTest {

    private var clock = 1_000_000L

    private fun store() = PendingPairings { clock }

    private fun offer(host: String = "192.168.178.31", code: String = "123456") =
        PendingPairings.sanitize(
            JSONObject()
                .put("host", host)
                .put("port", 8443)
                .put("code", code)
                .put("caFingerprint", "a".repeat(64))
                .put("name", "Pixel"),
        )!!

    @Test
    fun `ein Angebot laesst sich genau einmal abholen`() {
        val store = store()
        store.offer(offer())

        assertEquals("192.168.178.31", store.take()?.host)
        assertNull(store.take())
    }

    @Test
    fun `ohne Angebot kommt nichts`() {
        assertNull(store().take())
    }

    @Test
    fun `ein zweites Angebot ersetzt das erste`() {
        val store = store()

        store.offer(offer())
        store.offer(offer(host = "192.168.178.44", code = "654321"))

        assertEquals("192.168.178.44", store.take()?.host)
    }

    @Test
    fun `ein abgelaufenes Angebot wird nicht mehr herausgegeben`() {
        val store = store()
        store.offer(offer())

        clock += PendingPairings.LIFETIME_MS + 1

        assertNull(store.take())
    }

    @Test
    fun `Unvollstaendiges wird verworfen`() {
        assertNull(PendingPairings.sanitize(null))
        assertNull(PendingPairings.sanitize(JSONObject()))
        assertNull(PendingPairings.sanitize(JSONObject().put("host", "x").put("port", 0).put("code", "123456")))
        assertNull(PendingPairings.sanitize(JSONObject().put("host", "x").put("port", 8443).put("code", "12345")))
        assertNull(PendingPairings.sanitize(JSONObject().put("host", "x").put("port", 8443).put("code", "abcdef")))
    }

    @Test
    fun `ein unbrauchbarer Fingerabdruck kostet nur ihn selbst`() {
        val sanitized = PendingPairings.sanitize(
            JSONObject().put("host", "x").put("port", 8443).put("code", "123456")
                .put("caFingerprint", "kaputt"),
        )

        assertNotNull(sanitized)
        assertNull(sanitized!!.caFingerprint)
    }
}
