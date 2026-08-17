package app.remotedesktop.client.host

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * Die Zuordnung Pfad → Recht. Wie beim Agent eine Whitelist: was hier fehlt,
 * wird abgelehnt.
 */
class HostScopesTest {

    @Test
    fun `ordnet die Endpunkte ihrem Recht zu`() {
        assertEquals("screen", HostScopes.resolve("/ws/screen")?.scope)
        assertEquals("screen", HostScopes.resolve("/api/webrtc/offer")?.scope)
        assertEquals("input", HostScopes.resolve("/ws/input")?.scope)
        assertEquals("files", HostScopes.resolve("/api/files")?.scope)
        assertEquals("files", HostScopes.resolve("/api/files/content")?.scope)
    }

    @Test
    fun `die Selbstauskunft braucht kein Recht`() {
        val resolved = HostScopes.resolve("/api/info")

        assertEquals(null, resolved?.scope)
        assertEquals(true, resolved != null)
    }

    @Test
    fun `ein unbekannter Pfad wird abgelehnt, nicht durchgelassen`() {
        assertNull(HostScopes.resolve("/api/power"))
        assertNull(HostScopes.resolve("/api/wol"))
        assertNull(HostScopes.resolve("/api/irgendwas"))
    }

    /** Ein reiner Präfixvergleich ließe `/api/filesystem` als `/api/files` durch. */
    @Test
    fun `vergleicht auf Segmentgrenzen`() {
        assertNull(HostScopes.resolve("/api/filesystem"))
        assertEquals("files", HostScopes.resolve("/api/files/inhalt")?.scope)
    }

    @Test
    fun `ein Handy kennt genau drei Rechte`() {
        assertEquals(listOf("screen", "input", "files"), HostScopes.ALL)
        assertEquals(HostScopes.ALL, HostScopes.capabilities(screenAllowed = true))
    }

    /**
     * Das Recht bleibt, die Fähigkeit fällt weg: wer zusehen dürfte, ändert
     * sich durch eine Einstellung nicht — ob es etwas zu sehen gibt, schon.
     */
    @Test
    fun `ohne Bildfreigabe meldet das Handy kein Bild`() {
        assertEquals(listOf("input", "files"), HostScopes.capabilities(screenAllowed = false))
        assertEquals(listOf("screen", "input", "files"), HostScopes.ALL)
    }
}
