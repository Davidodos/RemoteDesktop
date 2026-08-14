package app.remotedesktop.client.host

import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/**
 * Jede Verbindung wird am Handy einzeln bestätigt.
 *
 * <p>
 * **Warum das hier steht und nicht in der Kopplung.** Eine Kopplung ist eine
 * Erlaubnis auf Dauer; sie sagt, *wer* fragen darf. Sie sagt nicht, dass jetzt
 * gerade jemand zusehen darf. Ein Handy ist kein Rechner auf dem Schreibtisch —
 * es liegt auf dem Tisch, in der Tasche, neben dem Bett. Wer es fernsteuern
 * will, hat es ohnehin in der Hand.
 * </p>
 *
 * <p>
 * **Ablehnung ist die Vorgabe.** Antwortet niemand, gilt das als Nein — nicht
 * als Ja. Und ist gar keine Oberfläche da, die fragen könnte, ist es ebenfalls
 * ein Nein: der Host läuft nur, solange die App offen ist, also ist „niemand
 * da" ein Zustand, der nicht vorkommen soll und im Zweifel verschlossen bleibt.
 * </p>
 *
 * <p>
 * Nebenbei rückt damit auch der Systemdialog der Bildschirmaufnahme an die
 * richtige Stelle: er kommt beim ersten Verbinden und nicht Tage vorher.
 * </p>
 */
class ConnectionRequests(private val timeoutMs: Long = TIMEOUT_MS) {

    companion object {
        /**
         * So lange wird gewartet. Kurz genug, dass die Gegenseite nicht vor
         * einer hängenden Anmeldung sitzt, lang genug, um das Handy aus der
         * Tasche zu holen.
         */
        const val TIMEOUT_MS = 30_000L
    }

    /** Eine Frage, die gerade auf dem Bildschirm steht. */
    private class Pending(val latch: CountDownLatch = CountDownLatch(1)) {
        @Volatile
        var allowed = false
    }

    private val gate = Any()
    private val open = HashMap<String, Pending>()

    /**
     * Wer gefragt wird. Die App meldet sich hier an, sobald ihre Oberfläche
     * steht; `null` heißt, dass niemand zuhört.
     */
    @Volatile
    var listener: ((id: String, label: String) -> Unit)? = null

    /** Die Frage wieder vom Bildschirm nehmen — beantwortet oder abgelaufen. */
    @Volatile
    var onSettled: ((id: String) -> Unit)? = null

    /**
     * Fragt und wartet. Läuft im Thread der Verbindung — der darf hier stehen
     * bleiben, denn genau darauf wartet die Gegenseite.
     *
     * @return `false` bei Ablehnung, Zeitablauf und fehlender Oberfläche.
     */
    fun ask(label: String): Boolean {
        val notify = listener ?: return false

        val id = UUID.randomUUID().toString()
        val pending = Pending()

        synchronized(gate) { open[id] = pending }

        return try {
            notify(id, label)

            pending.latch.await(timeoutMs, TimeUnit.MILLISECONDS) && pending.allowed
        } catch (interrupted: InterruptedException) {
            Thread.currentThread().interrupt()
            false
        } finally {
            synchronized(gate) { open.remove(id) }
            onSettled?.invoke(id)
        }
    }

    /**
     * Die Antwort vom Bildschirm. Eine unbekannte Kennung ist kein Fehler: die
     * Frage kann abgelaufen sein, während jemand noch zielte.
     */
    fun answer(id: String, allow: Boolean) {
        val pending = synchronized(gate) { open[id] } ?: return

        pending.allowed = allow
        pending.latch.countDown()
    }

    /** Wie viele Fragen gerade offen sind. Für die Tests. */
    val openCount: Int get() = synchronized(gate) { open.size }
}
