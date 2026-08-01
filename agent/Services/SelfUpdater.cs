namespace RemoteDesktopAgent.Services;

/// <summary>
/// Löst die Update-Prüfung <b>einmal beim Start</b> aus.
///
/// Ein laufender Agent soll sich nicht mitten in einer Sitzung unter den Händen
/// wegtauschen und neu starten — dabei bricht das Bild ab. Beim Start kostet
/// derselbe Neustart nichts, und der Weg zu einer neuen Fassung ist ohnehin ein
/// Neustart des Agents. Wer nicht warten will, drückt in der App auf den Knopf;
/// das ist <c>POST /api/update</c> und läuft durch dieselbe
/// <see cref="AgentUpdater"/>-Instanz.
/// </summary>
public sealed class SelfUpdater(AgentUpdater updater, ILogger<SelfUpdater> logger) : BackgroundService
{
    /// <summary>
    /// Erst nach dieser Zeit prüfen — der Agent soll zuerst erreichbar sein.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!updater.IsEnabled)
        {
            logger.LogInformation(
                "Selbst-Update ist aus: kein Release-Schlüssel in ReleaseKeys.PublicKey.");

            return;
        }

        await Task.Delay(StartupDelay, stoppingToken);

        try
        {
            await updater.CheckAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ein fehlgeschlagenes Update darf nie den laufenden Agent
            // beeinträchtigen — im Zweifel bleibt eben die alte Fassung.
            logger.LogWarning(ex, "Update-Prüfung fehlgeschlagen.");
        }
    }
}
