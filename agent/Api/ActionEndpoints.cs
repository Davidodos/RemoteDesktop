using RemoteDesktopAgent.Actions;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Aktionen auflisten und auslösen.
///
/// <para>
/// Es gibt hier bewusst <b>keinen schreibenden Endpunkt</b>. Ein solcher wäre
/// gleichbedeutend mit „jeder gültige Ausweis darf beliebigen Code auf diesem
/// Rechner hinterlegen" — und machte die ganze Regel wertlos, dass am Agent
/// deklariert und nur per Kennung aufgerufen wird. Bearbeitet wird
/// <c>actions.json</c> am Rechner selbst.
/// </para>
///
/// <para>
/// Beide Pfade verlangen das Recht <c>actions</c> (siehe
/// <c>Auth/AgentScopes.cs</c>).
/// </para>
/// </summary>
public static class ActionEndpoints
{
    public static void MapActionEndpoints(this WebApplication app)
    {
        // Ohne Pfade und Argumente — siehe ActionSummary. Ein Client braucht
        // nur Kennung, Beschriftung und Symbol, um einen Knopf zu bauen.
        app.MapGet("/api/actions", (ActionCatalog catalog) =>
            Results.Ok(new { actions = catalog.Summaries() }));

        app.MapPost("/api/actions/{id}/invoke", async (
            string id,
            ActionCatalog catalog,
            ActionRunner runner,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var action = catalog.Find(id);

            if (action is null)
            {
                // 404 und nicht 500: eine Kennung, die es nicht gibt, ist eine
                // veraltete App und kein Fehler dieses Rechners.
                return Results.NotFound(new { error = $"Unbekannte Aktion '{id}'." });
            }

            // Jede Ausführung steht im Log. Wer später wissen will, wer wann was
            // gestartet hat, findet es nur hier — der Client führt kein Buch.
            logger.LogInformation("Aktion {Id} ({Type}) wird ausgeführt.", action.Id, action.Type);

            try
            {
                await runner.RunAsync(action, catalog, cancellationToken);

                return Results.Ok(new { status = "ok", id = action.Id });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Aktion {Id} fehlgeschlagen.", action.Id);

                // Die Rohmeldung gehört ins Log, nicht in die Antwort: sie nennt
                // Pfade, und wer auslösen darf, muss nicht auch erfahren, wie
                // dieser Rechner eingerichtet ist.
                return Results.Problem($"Die Aktion '{action.Id}' ließ sich nicht ausführen.");
            }
        });
    }
}
