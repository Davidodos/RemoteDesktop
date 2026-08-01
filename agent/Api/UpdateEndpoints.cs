using RemoteDesktopAgent.Services;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Update auf Knopfdruck, statt auf den nächsten Start zu warten.
///
/// Die Antwort kommt, bevor getauscht wird — der Tausch beendet den Prozess,
/// und eine Antwort danach gäbe es nicht mehr. Die App erfährt daran, dass sie
/// gleich die Verbindung verliert und sich neu anmelden muss.
/// </summary>
public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        app.MapPost("/api/update", async (AgentUpdater updater, CancellationToken cancellationToken) =>
        {
            UpdateResult result;

            try
            {
                result = await updater.CheckAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Die Rohmeldung nennt Adressen und Pfade; nach außen geht nur,
                // dass die Prüfung nicht durchlief.
                app.Logger.LogWarning(ex, "Update-Prüfung auf Anforderung fehlgeschlagen.");
                result = new UpdateResult(UpdateOutcome.Failed);
            }

            var status = result.Outcome switch
            {
                UpdateOutcome.Failed => StatusCodes.Status502BadGateway,
                UpdateOutcome.Rejected => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status200OK
            };

            return Results.Json(
                new
                {
                    status = result.Outcome.ToString().ToLowerInvariant(),
                    version = result.Version,
                    message = Describe(result.Outcome)
                },
                statusCode: status);
        });
    }

    private static string Describe(UpdateOutcome outcome) => outcome switch
    {
        UpdateOutcome.Disabled => "Selbst-Update ist auf diesem Agent nicht eingerichtet.",
        UpdateOutcome.UpToDate => "Der Agent ist bereits aktuell.",
        UpdateOutcome.NotFound => "Es liegt kein vollständiges Release vor.",
        UpdateOutcome.Rejected => "Die angebotene Fassung ist nicht gültig unterschrieben.",
        UpdateOutcome.Skipped => "Diese Fassung ließ sich eben erst nicht installieren.",
        UpdateOutcome.Installing => "Der Agent tauscht sich aus und startet neu.",
        _ => "Die Update-Prüfung ist fehlgeschlagen."
    };
}
