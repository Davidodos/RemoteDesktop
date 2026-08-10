package app.remotedesktop.client.host

import org.json.JSONObject

/**
 * Ein Eingabe-Befehl, wie ihn die App über `/ws/input` schickt.
 *
 * Dieselben Nachrichten wie an den Windows-Agent (`agent/Api/InputCommand.cs`)
 * — die App weiß nicht, wer am anderen Ende sitzt, und soll es auch nicht
 * wissen müssen. Was ein Handy daraus macht, ist etwas anderes: es gibt dort
 * keinen Mauszeiger und keine Tastatur.
 */
sealed interface InputCommand {

    /** Zeiger auf einen Punkt setzen; beide Werte sind Anteile von 0 bis 1. */
    data class MoveAbsolute(val x: Double, val y: Double) : InputCommand

    /** Zeiger um so viele Bildpunkte verschieben — vom Touchpad. */
    data class MoveRelative(val dx: Int, val dy: Int) : InputCommand

    data class Click(val button: String) : InputCommand

    data class ButtonDown(val button: String) : InputCommand

    data class ButtonUp(val button: String) : InputCommand

    /** Rasterschritte; positiv ist hoch beziehungsweise rechts. */
    data class Scroll(val vertical: Int, val horizontal: Int) : InputCommand

    data class KeyDown(val key: String) : InputCommand

    data class KeyUp(val key: String) : InputCommand

    data class KeyCombo(val modifiers: List<String>, val key: String) : InputCommand

    data class TypeText(val text: String) : InputCommand
}

/**
 * Übersetzt die JSON-Nachrichten des Eingabe-Sockets.
 *
 * Rein funktional und ohne Android-Bezug, damit das Protokoll unter Test steht:
 * hier falsch abzubiegen bedeutet Berührungen an der falschen Stelle, und das
 * sieht man einem Gerät aus der Ferne nicht an.
 */
object InputCommands {

    /** Mehr ist kein Tippen mehr, sondern ein Fehler. */
    private const val MAX_TEXT_LENGTH = 4096

    private const val MAX_SCROLL_NOTCHES = 100

    private val BUTTONS = setOf("left", "right", "middle")

    /** @return `null`, wenn die Nachricht nichts Bekanntes enthält. */
    fun parse(message: String): InputCommand? {
        val json = runCatching { JSONObject(message) }.getOrNull() ?: return null

        return when (json.optString("t")) {
            "move" -> InputCommand.MoveAbsolute(
                json.optDouble("x", -1.0).coerceIn(0.0, 1.0),
                json.optDouble("y", -1.0).coerceIn(0.0, 1.0),
            ).takeIf { json.has("x") && json.has("y") }

            "moverel" -> InputCommand.MoveRelative(json.optInt("dx"), json.optInt("dy"))

            "click" -> button(json)?.let(InputCommand::Click)
            "down" -> button(json)?.let(InputCommand::ButtonDown)
            "up" -> button(json)?.let(InputCommand::ButtonUp)

            "scroll" -> InputCommand.Scroll(
                json.optInt("dy").coerceIn(-MAX_SCROLL_NOTCHES, MAX_SCROLL_NOTCHES),
                json.optInt("dx").coerceIn(-MAX_SCROLL_NOTCHES, MAX_SCROLL_NOTCHES),
            )

            "keydown" -> key(json)?.let(InputCommand::KeyDown)
            "keyup" -> key(json)?.let(InputCommand::KeyUp)

            "key" -> key(json)?.let { name ->
                val array = json.optJSONArray("mods")
                val mods = (0 until (array?.length() ?: 0)).map { array!!.getString(it) }

                InputCommand.KeyCombo(mods, name)
            }

            "text" -> json.optString("text")
                .takeIf { it.isNotEmpty() && it.length <= MAX_TEXT_LENGTH }
                ?.let(InputCommand::TypeText)

            else -> null
        }
    }

    private fun button(json: JSONObject): String? =
        json.optString("button", "left").takeIf { BUTTONS.contains(it) }

    private fun key(json: JSONObject): String? = json.optString("key").takeIf { it.isNotEmpty() }
}
