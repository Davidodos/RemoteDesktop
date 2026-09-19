using RemoteDesktopAgent.Auth;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Der Agent, gestartet nur zum Koppeln.
///
/// <para>
/// **Wozu:** ein Rechner, der nur andere steuert, hat keinen laufenden Agent —
/// oft gar keinen eingerichteten. Einen Code einlösen kann aber nur, wer
/// lauscht, und das Zertifikat dafür darf nur ein erhöhter Prozess lesen. Also
/// startet das Fenster den Agent für die Dauer eines Codes (mit einer Rückfrage
/// von Windows) und in diesem Modus: Kopplung ja, Bild, Eingabe und alles
/// andere nein. Danach beendet er sich von selbst — er geht nie von allein an
/// und bleibt nie von allein an.
/// </para>
/// </summary>
public static class PairOnlyMode
{
    /// <summary>Der Schalter auf der Kommandozeile: <c>--Agent:PairOnly=true</c>.</summary>
    public const string Setting = "Agent:PairOnly";

    /// <summary>
    /// So lange bleibt er nach dem letzten Code noch da. Das Fenster holt in
    /// dieser Zeit den Steckbrief der Gegenseite ab — danach wäre er nur noch
    /// über einen laufenden Agent zu haben.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    /// <summary>Was in diesem Modus erreichbar bleibt — alles andere nicht.</summary>
    private static readonly string[] Open =
    [
        "/health",
        "/api/info",
        "/api/pair",
        "/api/clients",
        "/api/quit"
    ];

    /// <summary>Sperrt alles außer der Kopplung. Steht vor der Zugangsprüfung.</summary>
    public static IApplicationBuilder UsePairOnly(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (Open.Any(entry => path.Equals(entry, StringComparison.OrdinalIgnoreCase)
                                  || path.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new { error = "Dieser Rechner ist nicht freigegeben." });
        });

    /// <summary>
    /// Beendet den Agent, sobald kein Code mehr offen ist — eingelöst,
    /// abgelaufen oder verworfen — und <see cref="Grace"/> verstrichen ist.
    /// </summary>
    public sealed class Lifetime(
        PairingCodes codes,
        IHostApplicationLifetime lifetime,
        ILogger<Lifetime> logger) : BackgroundService
    {
        private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Der erste Code kommt erst, wenn das Fenster den Agent erreicht —
            // bis dahin zählt die Frist ab dem Start.
            var idleSince = DateTimeOffset.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(Poll, stoppingToken);

                if (codes.RemainingLifetime() is not null)
                {
                    idleSince = DateTimeOffset.UtcNow;
                    continue;
                }

                if (DateTimeOffset.UtcNow - idleSince >= Grace)
                {
                    logger.LogInformation("Nur zum Koppeln gestartet — kein Code mehr offen, Ende.");
                    lifetime.StopApplication();
                    return;
                }
            }
        }
    }
}
