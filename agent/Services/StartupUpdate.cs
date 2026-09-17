namespace RemoteDesktopAgent.Services;

/// <summary>
/// Sucht <b>einmal beim Start</b> nach einer neuen Fassung und lässt den
/// Installer laufen, wenn es eine gibt.
///
/// Ein laufender Agent soll sich nicht mitten in einer Sitzung unter den Händen
/// wegtauschen — dabei bricht das Bild ab. Beim Start kostet derselbe Neustart
/// nichts. Wer nicht warten will, drückt in der App auf „Aktualisieren"; das
/// ist <c>POST /api/update/app</c> und läuft durch dieselbe
/// <see cref="InstallerUpdate"/>-Instanz.
/// </summary>
public sealed class StartupUpdate(InstallerUpdate installer, ILogger<StartupUpdate> logger)
    : BackgroundService
{
    /// <summary>Erst nach dieser Zeit prüfen — der Agent soll zuerst erreichbar sein.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!installer.IsEnabled)
        {
            logger.LogInformation("Updates sind aus: kein Release-Schlüssel in ReleaseKeys.PublicKey.");

            return;
        }

        await Task.Delay(StartupDelay, stoppingToken);

        try
        {
            var result = await installer.CheckAsync(automatic: true, stoppingToken);

            if (result.Outcome == InstallerOutcome.Installing)
            {
                logger.LogInformation(
                    "Fassung {Version} wird installiert — der Agent beendet sich.", result.Version);

                Environment.Exit(0);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ein fehlgeschlagenes Update darf nie den laufenden Agent
            // beeinträchtigen — im Zweifel bleibt eben die alte Fassung.
            logger.LogWarning(ex, "Update-Prüfung beim Start fehlgeschlagen.");
        }
    }
}
