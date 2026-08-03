package app.remotedesktop.client.surfaces

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * Der Steckbrief entsteht in TypeScript und wird in Kotlin gelesen — die
 * Nahtstelle zwischen den beiden Sprachen dieser App.
 *
 * Der Text unten ist deshalb kein ausgedachter, sondern genau das, was
 * `app/src/lib/surfaceBoard.ts` erzeugt. Weicht eine der beiden Seiten ab, fällt
 * es hier auf und nicht erst als Widget, das nichts anzeigt.
 */
class SurfaceBoardTest {

    private val vollstaendig = """
        {
          "deviceId": "pc",
          "deviceName": "PC",
          "node": { "host": "pc.example.ts.net", "port": 8443, "clientId": "handy-1" },
          "actions": [
            { "id": "spotify", "label": "Spotify" },
            { "id": "vscode", "label": "VS Code" }
          ],
          "wake": {
            "mac": "aa:bb:cc:dd:ee:ff",
            "via": { "host": "nas.example.ts.net", "port": 3080, "clientId": "handy-1" }
          }
        }
    """.trimIndent()

    @Test
    fun `liest was die App geschrieben hat`() {
        val board = SurfaceBoard.parse(vollstaendig)!!

        assertEquals("PC", board.deviceName)
        assertEquals(SurfaceBoard.Node("pc.example.ts.net", 8443, "handy-1"), board.node)
        assertEquals(listOf("spotify", "vscode"), board.actions.map { it.id })
        assertEquals("VS Code", board.actions[1].label)
        assertEquals("aa:bb:cc:dd:ee:ff", board.wake?.mac)
        assertEquals(3080, board.wake?.via?.port)
    }

    @Test
    fun `ohne Weckteil bleibt der Weckteil leer`() {
        // Der Normalfall bei einem Rechner, in dessen Netz sonst niemand steht.
        val ohne = """
            {
              "deviceId": "pc",
              "deviceName": "PC",
              "node": { "host": "pc.example.ts.net", "port": 8443, "clientId": "handy-1" },
              "actions": []
            }
        """.trimIndent()

        val board = SurfaceBoard.parse(ohne)!!

        assertNull(board.wake)
        assertEquals(0, board.actions.size)
    }

    @Test
    fun `der leere Text raeumt die Flaechen ab`() {
        // So sagt die App „dieses Gerät hat keine Flächen mehr".
        assertNull(SurfaceBoard.parse(""))
        assertNull(SurfaceBoard.parse(null))
    }

    @Test
    fun `kaputtes bleibt folgenlos`() {
        // Hier steht, was eine frühere Fassung der App hinterlassen hat. Ein
        // Widget, das daran abstürzt, wäre die schlechteste aller Antworten.
        assertNull(SurfaceBoard.parse("kein JSON"))
        assertNull(SurfaceBoard.parse("""{ "deviceId": "pc" }"""))
        assertNull(SurfaceBoard.parse("""{ "deviceName": "PC", "node": {} }"""))
    }
}
