using RemoteDesktopAgent.Services;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Update auf Knopfdruck, statt auf den nächsten Start zu warten — in zwei
/// Größen.
///
/// <para>
/// <c>POST /api/update</c> tauscht die Programmdatei des Agents. Das ist der
/// schnelle Weg, wenn sich nur der Agent geändert hat.
/// </para>
///
/// <para>
/// <c>POST /api/update/app</c> lässt den Installer laufen und erneuert damit
/// alles: Agent, Fenster und Oberfläche. Das ist der Weg, den ein gekoppeltes
/// Gerät nimmt — vor dem Rechner sitzt dabei niemand, der eine Rückfrage von
/// Windows bestätigen könnte, und deshalb muss es auch ohne eine gehen (siehe
/// <see cref="InstallerUpdate"/>).
/// </para>
///
/// <para>
/// <b>Die Antwort geht in beiden Fällen zuerst hinaus.</b> Vorher beendete sich
/// der Agent noch im Aufruf, also bevor irgendetwas geschrieben war: die App
/// bekam keine Auskunft, sondern eine abgebrochene Verbindung — und stand danach
/// vor der Frage, ob gerade aktualisiert wird oder der Rechner abgestürzt ist.
/// Jetzt wird geantwortet und erst danach beendet.
/// </para>
/// </summary>
public static class UpdateEndpoints
{
    /// <summary>
    /// So lange bleibt der Agent nach der Antwort noch stehen.
    ///
    /// Er muss lange genug leben, dass die Antwort wirklich über die Leitung
    /// ist — TLS puffert, und ein Prozess, der sofort endet, nimmt den letzten
    /// Block mit. Zwei Sekunden sind reichlich für ein paar hundert Byte und
    /// kurz genug, dass niemand darauf wartet: der Installer wartet ohnehin
    /// fünf, bevor er anfängt.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

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

            if (result.Outcome == UpdateOutcome.Installing)
            {
                ExitAfterGrace(app.Logger);
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

        app.MapPost("/api/update/app", async (
            InstallerUpdate installer, CancellationToken cancellationToken) =>
        {
            InstallerResult result;

            try
            {
                result = await installer.CheckAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                app.Logger.LogWarning(ex, "Voll-Update auf Anforderung fehlgeschlagen.");
                result = new InstallerResult(InstallerOutcome.Failed);
            }

            if (result.Outcome == InstallerOutcome.Installing)
            {
                // Der Installer beendet den Agent ohnehin. Ihn von allein gehen
                // zu lassen ist trotzdem wichtig: freiwillig beendet nimmt er
                // das gestartete Skript nicht mit in seine Job-Zuordnung.
                ExitAfterGrace(app.Logger);
            }

            var status = result.Outcome switch
            {
                InstallerOutcome.Failed => StatusCodes.Status502BadGateway,
                InstallerOutcome.Rejected => StatusCodes.Status502BadGateway,
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

    /// <summary>
    /// Beendet den Agent, nachdem die Antwort draußen ist.
    ///
    /// Nebenläufig und ausdrücklich ohne <c>await</c>: dieser Aufruf soll
    /// zurückkehren, damit ASP.NET die Antwort schreiben kann. Genau darauf
    /// wartet die Verzögerung.
    /// </summary>
    private static void ExitAfterGrace(ILogger logger)
    {
        logger.LogInformation("Update läuft — der Agent beendet sich gleich.");

        _ = Task.Run(async () =>
        {
            await Task.Delay(Grace);
            Environment.Exit(0);
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

    private static string Describe(InstallerOutcome outcome) => outcome switch
    {
        InstallerOutcome.Disabled => "Updates sind auf diesem Rechner nicht eingerichtet.",
        InstallerOutcome.UpToDate => "Dieser Rechner ist bereits aktuell.",
        InstallerOutcome.NotFound => "Es liegt kein vollständiges Release vor.",
        InstallerOutcome.Rejected => "Die angebotene Fassung ist nicht gültig unterschrieben.",
        InstallerOutcome.Installing =>
            "Der Rechner wird aktualisiert. Er ist etwa eine Minute lang nicht erreichbar.",
        _ => "Das Update ist fehlgeschlagen."
    };
}
