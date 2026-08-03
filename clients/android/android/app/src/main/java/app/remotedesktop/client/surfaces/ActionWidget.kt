package app.remotedesktop.client.surfaces

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.view.View
import android.widget.RemoteViews
import app.remotedesktop.client.MainActivity
import app.remotedesktop.client.R

/**
 * Das Aktionsraster auf dem Startbildschirm.
 *
 * Es zeigt, was der zuletzt benutzte Rechner anbietet, und löst mit einem Tipp
 * aus — ohne dass die App dafür geöffnet werden muss. Das ist der Sinn der
 * ganzen Übung: die häufigen Handgriffe sollen einen Tipp kosten, nicht fünf.
 *
 * Woher die Knöpfe kommen, entscheidet nicht dieses Widget: sie stehen in der
 * `actions.json` auf jenem Rechner, und die App hat sie zuletzt als Steckbrief
 * hinterlegt (siehe [SurfaceStore]). Ausgelöst wird auch hier nur über die
 * Kennung — eine Kommandozeile bekommt das Handy nie zu sehen.
 */
class ActionWidget : AppWidgetProvider() {

    override fun onUpdate(
        context: Context,
        manager: AppWidgetManager,
        widgetIds: IntArray,
    ) {
        widgetIds.forEach { render(context, manager, it) }
    }

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != ACTION_INVOKE) {
            super.onReceive(context, intent)
            return
        }

        // Ein Rundruf endet, sobald onReceive zurückkehrt — und dann darf das
        // System den Prozess beenden. goAsync() hält ihn, bis die Antwort des
        // Agents da ist und die Meldung stehen konnte.
        val pending = goAsync()

        SurfaceWork.invoke(context, intent.getStringExtra(EXTRA_ACTION).orEmpty()) {
            pending.finish()
        }
    }

    companion object {

        private const val ACTION_INVOKE = "app.remotedesktop.client.surfaces.INVOKE"
        private const val EXTRA_ACTION = "action"

        /** Zwei Spalten; mehr wird auf einem Handy zur Briefmarke. */
        private const val COLUMNS = 2

        /** Was ein Widget in üblicher Größe fasst, ohne dass gescrollt werden muss. */
        private const val MAX_ACTIONS = 6

        /** Zeichnet alle liegenden Widgets neu — nach jedem neuen Steckbrief. */
        fun refresh(context: Context) {
            val manager = AppWidgetManager.getInstance(context) ?: return
            val widgets = manager.getAppWidgetIds(
                ComponentName(context, ActionWidget::class.java),
            )

            widgets.forEach { render(context, manager, it) }
        }

        private fun render(context: Context, manager: AppWidgetManager, widgetId: Int) {
            val board = SurfaceStore.board(context)
            val views = RemoteViews(context.packageName, R.layout.widget_actions)
            val actions = board?.actions.orEmpty().take(MAX_ACTIONS)

            views.setTextViewText(
                R.id.widget_title,
                board?.deviceName ?: context.getString(R.string.app_name),
            )
            views.setOnClickPendingIntent(R.id.widget_title, openApp(context))

            // Ohne das stünden nach einem Wechsel des Rechners die Knöpfe von
            // vorhin noch mit darunter: RemoteViews hängt an, es ersetzt nicht.
            views.removeAllViews(R.id.widget_grid)

            views.setViewVisibility(
                R.id.widget_empty,
                if (actions.isEmpty()) View.VISIBLE else View.GONE,
            )
            views.setTextViewText(R.id.widget_empty, hint(context, board))

            actions.chunked(COLUMNS).forEach { row ->
                views.addView(R.id.widget_grid, buildRow(context, row))
            }

            manager.updateAppWidget(widgetId, views)
        }

        private fun buildRow(
            context: Context,
            row: List<SurfaceBoard.Action>,
        ): RemoteViews {
            val views = RemoteViews(context.packageName, R.layout.widget_row)

            row.forEach { action ->
                val cell = RemoteViews(context.packageName, R.layout.widget_action)

                cell.setTextViewText(R.id.widget_action_label, action.label)
                cell.setOnClickPendingIntent(R.id.widget_action_label, invoke(context, action.id))
                views.addView(R.id.widget_row, cell)
            }

            return views
        }

        /**
         * Warum gerade nichts dasteht. Ein leeres Widget sähe nach einem Fehler
         * aus, obwohl es meistens nur heißt: einmal verbinden, dann steht es da.
         */
        private fun hint(context: Context, board: SurfaceBoard?): String = when {
            board == null -> context.getString(R.string.widget_empty_unpaired)
            else -> context.getString(R.string.widget_empty_actions, board.deviceName)
        }

        private fun openApp(context: Context): PendingIntent = PendingIntent.getActivity(
            context,
            0,
            Intent(context, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        /**
         * Der Auftrag zeigt ausdrücklich auf diese Klasse und ist unveränderlich:
         * ein Auftrag ohne festen Empfänger könnte von einer fremden App mit
         * einer anderen Kennung gefüllt werden, und die läge dann in unserem
         * Namen beim Agent.
         */
        private fun invoke(context: Context, actionId: String): PendingIntent {
            val intent = Intent(context, ActionWidget::class.java)
                .setAction(ACTION_INVOKE)
                .putExtra(EXTRA_ACTION, actionId)

            return PendingIntent.getBroadcast(
                context,
                // Ohne eigenen Code je Aktion gäbe es nur einen Auftrag, und
                // jeder Knopf löste den zuerst angelegten aus.
                actionId.hashCode(),
                intent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
        }
    }
}
