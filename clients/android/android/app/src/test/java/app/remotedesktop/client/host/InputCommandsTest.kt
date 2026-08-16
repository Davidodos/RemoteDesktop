package app.remotedesktop.client.host

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * Die Nachrichten des Eingabe-Sockets.
 *
 * Sie kommen unverändert aus `app/src/lib/inputChannel.ts` — dieselbe App
 * schickt sie an einen Windows-Rechner und an ein Handy. Was hier falsch
 * gelesen wird, landet als Berührung an der falschen Stelle, und das sieht man
 * aus der Ferne nicht.
 */
class InputCommandsTest {

    private fun parse(json: JSONObject) = InputCommands.parse(json.toString())

    @Test
    fun `liest eine absolute Bewegung als Anteil`() {
        val command = parse(
            JSONObject().put("t", "move").put("monitor", 0).put("x", 0.25).put("y", 0.5),
        )

        assertEquals(InputCommand.MoveAbsolute(0.25, 0.5), command)
    }

    /** Ein Anteil außerhalb 0..1 wäre eine Berührung neben dem Bildschirm. */
    @Test
    fun `begrenzt Anteile auf die Flaeche`() {
        val command = parse(JSONObject().put("t", "move").put("x", 5.0).put("y", -2.0))

        assertEquals(InputCommand.MoveAbsolute(1.0, 0.0), command)
    }

    @Test
    fun `liest die relative Bewegung vom Touchpad`() {
        assertEquals(
            InputCommand.MoveRelative(12, -4),
            parse(JSONObject().put("t", "moverel").put("dx", 12).put("dy", -4)),
        )
    }

    @Test
    fun `liest Klicks samt Taste`() {
        assertEquals(
            InputCommand.Click("right"),
            parse(JSONObject().put("t", "click").put("button", "right")),
        )

        // Ohne Angabe ist es die linke — so schickt es die App beim Tippen aufs
        // Bild.
        assertEquals(InputCommand.Click("left"), parse(JSONObject().put("t", "click")))
    }

    @Test
    fun `eine erfundene Taste wird nicht angenommen`() {
        assertNull(parse(JSONObject().put("t", "click").put("button", "vierte")))
    }

    @Test
    fun `liest Halten und Loslassen`() {
        assertEquals(
            InputCommand.ButtonDown("left"),
            parse(JSONObject().put("t", "down").put("button", "left")),
        )

        assertEquals(
            InputCommand.ButtonUp("left"),
            parse(JSONObject().put("t", "up").put("button", "left")),
        )
    }

    @Test
    fun `liest das Mausrad in beide Richtungen`() {
        assertEquals(
            InputCommand.Scroll(3, -1),
            parse(JSONObject().put("t", "scroll").put("dy", 3).put("dx", -1)),
        )
    }

    @Test
    fun `begrenzt unsinnige Rasterschritte`() {
        assertEquals(
            InputCommand.Scroll(100, 0),
            parse(JSONObject().put("t", "scroll").put("dy", 99999)),
        )
    }

    @Test
    fun `liest Tasten und Kombinationen`() {
        assertEquals(
            InputCommand.KeyUp("Escape"),
            parse(JSONObject().put("t", "keyup").put("key", "Escape")),
        )

        val combo = parse(
            JSONObject()
                .put("t", "key")
                .put("key", "C")
                .put("mods", org.json.JSONArray(listOf("Control"))),
        )

        assertEquals(InputCommand.KeyCombo(listOf("Control"), "C"), combo)
    }

    @Test
    fun `liest Text`() {
        assertEquals(
            InputCommand.TypeText("hallo"),
            parse(JSONObject().put("t", "text").put("text", "hallo")),
        )
    }

    @Test
    fun `ein zu langer Text wird verworfen statt getippt`() {
        assertNull(parse(JSONObject().put("t", "text").put("text", "x".repeat(4097))))
    }

    @Test
    fun `leerer Text ist kein Befehl`() {
        assertNull(parse(JSONObject().put("t", "text").put("text", "")))
    }

    /**
     * Der Zoom. Er entsteht am Rechner aus einem gezogenen Rechtsklick, weil
     * eine Maus keine zwei Finger hat — hier kommt er als Mittelpunkt und
     * Faktor an.
     */
    @Test
    fun `liest die Zoomgeste`() {
        assertEquals(
            InputCommand.Pinch(0.5, 0.5, 2.0),
            parse(JSONObject().put("t", "pinch").put("x", 0.5).put("y", 0.5).put("scale", 2.0)),
        )
    }

    /** Ein Faktor jenseits jedes Maßes führte beide Finger vom Bildschirm. */
    @Test
    fun `begrenzt den Zoomfaktor`() {
        assertEquals(
            InputCommand.Pinch(0.5, 0.5, 10.0),
            parse(JSONObject().put("t", "pinch").put("x", 0.5).put("y", 0.5).put("scale", 500.0)),
        )
    }

    @Test
    fun `eine Zoomgeste ohne Mittelpunkt ist keine`() {
        assertNull(parse(JSONObject().put("t", "pinch").put("scale", 2.0)))
    }

    @Test
    fun `Unbekanntes und Kaputtes ergibt nichts`() {
        assertNull(InputCommands.parse("kein JSON"))
        assertNull(InputCommands.parse("[]"))
        assertNull(parse(JSONObject().put("t", "beamen")))
        assertNull(parse(JSONObject()))
    }
}
