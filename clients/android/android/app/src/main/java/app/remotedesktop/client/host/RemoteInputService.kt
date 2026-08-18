package app.remotedesktop.client.host

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ComponentName
import android.content.Context
import android.graphics.Path
import android.os.Bundle
import android.provider.Settings
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo

/**
 * Die einzige Tür, durch die eine App ohne Root Berührungen in fremde Apps
 * schicken kann.
 *
 * **Das ist ein sehr großes Recht.** Eine Bedienungshilfe darf mitlesen, was auf
 * dem Bildschirm steht, und darf überall hintippen. Genau deshalb schaltet sie
 * niemand nebenbei ein: Android verlangt dafür den Gang in die
 * Systemeinstellungen und eine ausdrückliche Bestätigung. Die App kann das
 * weder abkürzen noch heimlich tun, und das ist richtig so.
 *
 * **Was hier nicht geht:** echte Tastendrücke. `dispatchGesture` kennt
 * Berührungen, keine Tasten; ein Strg+C gibt es auf Android für eine fremde App
 * schlicht nicht. Text kommt deshalb über den Knoten, der gerade den Fokus hat,
 * und die wenigen sinnvollen Tasten werden zu den Systemaktionen Zurück, Home
 * und Übersicht. Deswegen meldet das Handy die Fähigkeit `keys` nicht.
 */
class RemoteInputService : AccessibilityService() {

    companion object {
        @Volatile
        private var instance: RemoteInputService? = null

        /** Die laufende Bedienungshilfe, oder `null`, wenn sie aus ist. */
        fun current(): RemoteInputService? = instance

        /**
         * Ob der Nutzer sie eingeschaltet hat.
         *
         * Gefragt wird die Systemeinstellung und nicht [current]: der Dienst
         * wird erst gebunden, wenn Android soweit ist, und in der Zwischenzeit
         * sähe die Freigabeseite so aus, als hätte das Einschalten nicht
         * geklappt.
         */
        fun isEnabled(context: Context): Boolean {
            val enabled = runCatching {
                Settings.Secure.getString(
                    context.contentResolver,
                    Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                )
            }.getOrNull().orEmpty()

            val wanted = ComponentName(context, RemoteInputService::class.java)

            return enabled.split(':').any {
                it.equals(wanted.flattenToString(), ignoreCase = true) ||
                    it.equals(wanted.flattenToShortString(), ignoreCase = true)
            }
        }

        /** Wie lange ein Tippen dauert. Kürzer erkennen manche Apps nicht. */
        private const val TAP_MS = 60L

        /** Ab hier gilt ein Druck als lang — das ist der Rechtsklick des Handys. */
        private const val LONG_PRESS_MS = 600L

        /**
         * Länger als das dauert keine Geste, egal wie lange jemand die Taste
         * hält. Android bricht sehr lange Gesten ab, und eine Sekunde ist für
         * jedes Wischen und jedes lange Drücken mehr als genug.
         */
        private const val MAX_GESTURE_MS = 2000L

        /** Ab dieser Strecke war es ein Wischen und kein Tippen. */
        private const val DRAG_SLOP_PX = 8

        /**
         * Wie weit die beiden Finger einer Zoomgeste zu Beginn auseinander
         * stehen, gemessen an der kürzeren Bildschirmseite. Zu eng, und Android
         * hält es für einen Finger; zu weit, und einer der beiden landet
         * außerhalb.
         */
        private const val PINCH_SPREAD = 0.15f

        private const val PINCH_MS = 300L

        /** Wie weit ein Rasterschritt des Mausrads wischt. */
        private const val SCROLL_STEP_PX = 220

        private const val SCROLL_MS = 180L
    }

    /**
     * Wo der Zeiger gerade steht.
     *
     * Android kennt keinen Mauszeiger — es gibt nur Berührungen, die anfangen
     * und aufhören. Die App schickt aber Bewegungen und Klicks getrennt, weil
     * sie mit einem PC redet. Also wird hier eine Position geführt, und ein
     * Klick tippt dorthin. Damit funktionieren das Zeiger-Overlay und der
     * direkte Tipp aufs Bild unverändert.
     */
    private var pointerX = 0f
    private var pointerY = 0f

    /** Ob gerade eine Taste gehalten wird — daraus wird ein Ziehen. */
    private var dragging = false
    private var dragFromX = 0f
    private var dragFromY = 0f

    /**
     * Wann die Taste gedrückt wurde.
     *
     * <p>
     * **Der Befund dahinter:** die Geste dauerte immer 250 Millisekunden, egal
     * wie lange jemand hielt. Damit war jedes Wischen gleich schnell — ein
     * langsam gezogener Regler sprang, und ein langer Druck ohne Bewegung wurde
     * zu einem kurzen Tippen. Wie lange ein Finger liegt, ist aber Teil der
     * Geste und nicht ihre Verpackung: dieselbe Strecke in 200 statt 1200
     * Millisekunden bedeutet in vielen Apps etwas anderes.
     * </p>
     */
    private var dragSince = 0L

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this

        val display = resources.displayMetrics

        pointerX = display.widthPixels / 2f
        pointerY = display.heightPixels / 2f
    }

    override fun onDestroy() {
        instance = null
        super.onDestroy()
    }

    // Nichts zu tun: dieser Dienst hört nicht zu, er handelt nur. Angemeldet
    // sind deshalb auch keine Ereignistypen (siehe res/xml/remote_input.xml).
    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit

    override fun onInterrupt() = Unit

    /**
     * Führt einen Befehl aus.
     *
     * @return Eine Meldung, wenn es nicht geht — sie landet über den
     *   Eingabe-Socket in der Statuszeile der App. `null` heißt: erledigt.
     *   Stillschweigend nichts zu tun wäre der schlimmste Ausgang, weil aus der
     *   Ferne ein hängendes Gerät genauso aussieht.
     */
    fun execute(command: InputCommand): String? {
        val display = resources.displayMetrics

        return when (command) {
            is InputCommand.MoveAbsolute -> {
                pointerX = (command.x * display.widthPixels).toFloat()
                pointerY = (command.y * display.heightPixels).toFloat()
                null
            }

            is InputCommand.MoveRelative -> {
                pointerX = (pointerX + command.dx).coerceIn(0f, display.widthPixels - 1f)
                pointerY = (pointerY + command.dy).coerceIn(0f, display.heightPixels - 1f)
                null
            }

            is InputCommand.Click -> when (command.button) {
                // Der Rechtsklick des Handys ist das lange Drücken. Etwas
                // anderes gibt es dort nicht, und ein Klick, der gar nichts
                // tut, wäre schlechter als die naheliegende Entsprechung.
                "right" -> tap(LONG_PRESS_MS)
                "middle" -> "Ein Handy kennt keine mittlere Maustaste."
                else -> tap(TAP_MS)
            }

            is InputCommand.ButtonDown -> {
                dragging = true
                dragFromX = pointerX
                dragFromY = pointerY
                dragSince = android.os.SystemClock.uptimeMillis()
                null
            }

            is InputCommand.ButtonUp -> {
                if (!dragging) {
                    return null
                }

                dragging = false

                // So lange, wie die Taste wirklich unten war. Damit wird aus
                // einem kurzen Klick ein Tippen, aus einem gehaltenen ein
                // langer Druck und aus einem gezogenen ein Wischen in genau dem
                // Tempo, in dem gezogen wurde.
                val held = (android.os.SystemClock.uptimeMillis() - dragSince)
                    .coerceIn(TAP_MS, MAX_GESTURE_MS)

                // Ist der Finger stehen geblieben, war es ein Tippen und kein
                // Ziehen — sonst käme bei jedem Klick eine Wischgeste über null
                // Pixel heraus, und die verwirft Android.
                if (kotlin.math.hypot(pointerX - dragFromX, pointerY - dragFromY) < DRAG_SLOP_PX) {
                    tap(held)
                } else {
                    swipe(dragFromX, dragFromY, pointerX, pointerY, held)
                }
            }

            is InputCommand.Pinch -> pinch(
                (command.x * display.widthPixels).toFloat(),
                (command.y * display.heightPixels).toFloat(),
                command.scale.toFloat(),
            )

            is InputCommand.Scroll -> {
                val dy = -command.vertical * SCROLL_STEP_PX
                val dx = -command.horizontal * SCROLL_STEP_PX

                swipe(
                    pointerX,
                    pointerY,
                    (pointerX + dx).coerceIn(0f, display.widthPixels - 1f),
                    (pointerY + dy).coerceIn(0f, display.heightPixels - 1f),
                    SCROLL_MS,
                )
            }

            // Gehaltene Tasten ergeben auf Android nichts: es gibt keinen
            // Zustand „Umschalt liegt an". Ausgelöst wird beim Loslassen.
            is InputCommand.KeyDown -> null
            is InputCommand.KeyUp -> pressKey(command.key)
            is InputCommand.KeyCombo -> pressKey(command.key)

            is InputCommand.TypeText -> type(command.text)
        }
    }

    // ---- Gesten -----------------------------------------------------------

    private fun tap(durationMs: Long): String? = swipe(pointerX, pointerY, pointerX, pointerY, durationMs)

    private fun swipe(fromX: Float, fromY: Float, toX: Float, toY: Float, durationMs: Long): String? {
        val path = Path().apply {
            moveTo(fromX, fromY)

            if (fromX != toX || fromY != toY) {
                lineTo(toX, toY)
            }
        }

        val gesture = GestureDescription.Builder()
            .addStroke(GestureDescription.StrokeDescription(path, 0, durationMs))
            .build()

        return if (dispatchGesture(gesture, null, null)) {
            null
        } else {
            // Das passiert, wenn gerade eine andere Geste läuft oder der
            // Bildschirm aus ist. Kein Grund für einen Abbruch, aber einer für
            // eine Zeile in der Statuszeile.
            "Die Geste wurde nicht angenommen — läuft der Bildschirm?"
        }
    }

    /**
     * Zwei Finger, die auseinander- oder zusammengehen.
     *
     * <p>
     * Beide Striche laufen in **einer** Geste: zwei nacheinander abgeschickte
     * wären zwei einzelne Finger, und daraus wird kein Zoom, sondern zweimal
     * Wischen. Sie liegen auf einer Diagonalen um den Mittelpunkt — welche
     * Richtung, ist gleichgültig, es zählt der Abstand.
     * </p>
     */
    private fun pinch(centerX: Float, centerY: Float, scale: Float): String? {
        val display = resources.displayMetrics
        val base = minOf(display.widthPixels, display.heightPixels) * PINCH_SPREAD

        // Der Mittelpunkt muss so weit vom Rand weg sein, dass beide Finger auf
        // dem Bildschirm bleiben — auch der weitere von beiden.
        val reach = base * maxOf(1f, scale)
        val x = centerX.coerceIn(reach, display.widthPixels - 1f - reach)
        val y = centerY.coerceIn(reach, display.heightPixels - 1f - reach)

        val from = base / 1.4142f
        val to = from * scale

        val gesture = GestureDescription.Builder()
            .addStroke(stroke(x - from, y - from, x - to, y - to))
            .addStroke(stroke(x + from, y + from, x + to, y + to))
            .build()

        return if (dispatchGesture(gesture, null, null)) {
            null
        } else {
            "Die Zoomgeste wurde nicht angenommen — läuft der Bildschirm?"
        }
    }

    private fun stroke(
        fromX: Float,
        fromY: Float,
        toX: Float,
        toY: Float,
    ): GestureDescription.StrokeDescription {
        val path = Path().apply {
            moveTo(fromX, fromY)
            lineTo(toX, toY)
        }

        return GestureDescription.StrokeDescription(path, 0, PINCH_MS)
    }

    // ---- Tasten und Text --------------------------------------------------

    /**
     * Die wenigen Tasten, für die es auf Android eine Entsprechung gibt.
     *
     * <p>
     * **Ein einzelnes Zeichen ist keine Taste, sondern Text.** Der Rechner
     * schickt es seit 31j von sich aus als Text (siehe `lib/touchTyping.ts`);
     * hier steht der Fall trotzdem, weil eine ältere Gegenstelle es weiterhin
     * als Anschlag schickt — und dann stand bei jedem Buchstaben „„e" gibt es
     * auf einem Handy nicht", obwohl es die Taste offensichtlich gibt. Sie gibt
     * es. Sie ist nur nichts, was `dispatchGesture` drücken könnte.
     * </p>
     *
     * <p>
     * Alles andere wird abgelehnt statt still verschluckt: wer F5 schickt, soll
     * erfahren, dass ein Handy das nicht kennt.
     * </p>
     */
    private fun pressKey(key: String): String? = when (key.lowercase()) {
        "escape", "browserback" -> global(GLOBAL_ACTION_BACK, "Zurück")
        "home", "browserhome" -> global(GLOBAL_ACTION_HOME, "Home")
        "f5" -> global(GLOBAL_ACTION_RECENTS, "Übersicht")
        "enter", "numpadenter" -> enter()
        "backspace" -> backspace()
        "space" -> type(" ")
        "tab" -> type("\t")
        else -> if (key.codePointCount(0, key.length) == 1) type(key) else
            "„$key\" gibt es auf einem Handy nicht."
    }

    private fun global(action: Int, name: String): String? =
        if (performGlobalAction(action)) null else "$name ließ sich nicht auslösen."

    private fun enter(): String? {
        val node = focused() ?: return "Kein Eingabefeld im Vordergrund."

        return if (node.performAction(AccessibilityNodeInfo.ACTION_CLICK)) {
            null
        } else {
            "Die Eingabe ließ sich nicht abschließen."
        }
    }

    /**
     * Text kommt nicht als Anschlagfolge an, sondern ersetzt den Inhalt des
     * Feldes.
     *
     * Die App schickt jeden Anschlag einzeln (siehe `lib/softKeyboard.ts`).
     * Deshalb wird hier angehängt statt ersetzt — sonst bliebe von einem Wort
     * immer nur der letzte Buchstabe stehen.
     */
    private fun type(text: String): String? {
        val node = focused() ?: return "Kein Eingabefeld im Vordergrund."

        return set(node, contentOf(node) + text)
    }

    private fun backspace(): String? {
        val node = focused() ?: return "Kein Eingabefeld im Vordergrund."

        val existing = contentOf(node)

        return if (existing.isEmpty()) null else set(node, existing.dropLast(1))
    }

    /**
     * Was wirklich im Feld steht — **ohne den grauen Vorschlagstext**.
     *
     * <p>
     * **Der Befund dahinter (18.08.2026):** wer vom Rechner aus in ein leeres
     * Feld tippte, bekam den Platzhalter mitgeschrieben — aus „Suchen" und einem
     * getippten `a` wurde „Suchena". Löschte man das weg, war das Feld leer,
     * zeigte wieder seinen Platzhalter, und der nächste Buchstabe holte ihn
     * erneut herein. Der Grund steht in Androids Schnittstelle: ein leeres Feld
     * gibt unter `getText()` seinen Hinweistext zurück, weil das nun einmal das
     * ist, was dort zu lesen steht. Ob es ein Hinweis ist, sagt erst
     * `isShowingHintText()` — und danach hat vorher niemand gefragt.
     * </p>
     */
    private fun contentOf(node: AccessibilityNodeInfo): String {
        if (node.isShowingHintText) {
            return ""
        }

        val text = node.text?.toString().orEmpty()
        val hint = node.hintText?.toString()

        // Zweiter Riegel für Felder, die `isShowingHintText` nicht pflegen: ein
        // Inhalt, der Zeichen für Zeichen der Hinweis ist, ist der Hinweis.
        return if (hint != null && text == hint) "" else text
    }

    /**
     * Setzt den Inhalt eines Feldes — und hat dafür zwei Wege.
     *
     * <p>
     * **Warum zwei.** `ACTION_SET_TEXT` ist der gerade Weg und der einzige, der
     * ohne Nebenwirkung auskommt. Er scheitert aber an einer ganzen Klasse von
     * Feldern: die Suchfelder von YouTube und Spotify gehören dazu, und mit
     * ihnen alles, was seinen Text nicht in einem gewöhnlichen `EditText` hält.
     * Was dort stand, war „Dieses Feld nimmt keinen Text von außen an" — richtig
     * beschrieben und trotzdem die falsche Auskunft, denn einfügen lässt sich
     * dort sehr wohl etwas. Also wird eingefügt.
     * </p>
     *
     * <p>
     * **Der Preis ist ausgesprochen:** der Weg über die Zwischenablage
     * überschreibt, was dort lag. Das ist unschön und trotzdem die bessere
     * Hälfte der Wahl — die andere wäre ein Feld, in das sich nichts schreiben
     * lässt. Gegangen wird er nur, wenn der gerade Weg vorher gescheitert ist.
     * </p>
     */
    private fun set(node: AccessibilityNodeInfo, value: String): String? {
        val arguments = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, value)
        }

        if (node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, arguments)) {
            // Der Cursor ans Ende. Ohne das steht er bei manchen Feldern nach
            // dem Setzen wieder vorn, und der nächste Buchstabe landet dort.
            moveCaretToEnd(node, value.length)

            return null
        }

        return paste(node, value)
    }

    /**
     * Der zweite Weg: alles markieren und den neuen Inhalt darüber einfügen.
     *
     * Markiert wird ausdrücklich der ganze bestehende Inhalt — sonst käme der
     * neue Text zum alten hinzu, und aus jedem Buchstaben würde ein Wort.
     */
    private fun paste(node: AccessibilityNodeInfo, value: String): String? {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
            ?: return "Dieses Feld nimmt keinen Text von außen an."

        val stored = runCatching {
            clipboard.setPrimaryClip(ClipData.newPlainText("RemoteDesktop", value))
        }.isSuccess

        if (!stored) {
            return "Dieses Feld nimmt keinen Text von außen an."
        }

        selectAll(node)

        return if (node.performAction(AccessibilityNodeInfo.ACTION_PASTE)) {
            null
        } else {
            "Dieses Feld nimmt keinen Text von außen an."
        }
    }

    private fun selectAll(node: AccessibilityNodeInfo) {
        val arguments = Bundle().apply {
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, 0)
            putInt(
                AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT,
                node.text?.length ?: 0,
            )
        }

        node.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, arguments)
    }

    private fun moveCaretToEnd(node: AccessibilityNodeInfo, position: Int) {
        val arguments = Bundle().apply {
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, position)
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT, position)
        }

        node.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, arguments)
    }

    private fun focused(): AccessibilityNodeInfo? =
        runCatching { findFocus(AccessibilityNodeInfo.FOCUS_INPUT) }.getOrNull()
}
