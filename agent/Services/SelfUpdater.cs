using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteDesktopAgent.Services;

/// <summary>Was der Hub über die bereitgestellte Agent-Datei sagt.</summary>
internal sealed record ReleaseManifest(string File, long Size, string Sha256);

/// <summary>
/// Holt neue Versionen des Agents vom Hub und tauscht sich selbst aus.
///
/// Der Agent läuft auf zwei Rechnern, die beide nicht in Reichweite sind, wenn
/// man gerade unterwegs ist — ohne Selbst-Update bedeutet jede Änderung einen
/// Gang zum PC. Sicherheitsnetz sind drei Dinge: der Download läuft über HTTPS
/// gegen den bekannten Hub, er ist mit demselben Token geschützt wie alles
/// andere, und die Datei wird vor dem Tausch gegen die Prüfsumme aus dem
/// Manifest gehalten.
///
/// Getauscht wird von einem winzigen Batch-Skript: eine laufende .exe kann sich
/// unter Windows nicht selbst überschreiben.
///
/// Geprüft wird <b>einmal beim Start</b>. Ein laufender Agent soll sich nicht
/// mitten in einer Sitzung unter den Händen wegtauschen und neu starten —
/// dabei bricht das Bild ab. Beim Start kostet derselbe Neustart nichts, und
/// der Weg zu einer neuen Version ist ohnehin ein Neustart des Agents.
/// </summary>
public sealed class SelfUpdater(
    IHttpClientFactory clients, IConfiguration configuration, ILogger<SelfUpdater> logger)
    : BackgroundService
{
    /// <summary>
    /// Erst nach dieser Zeit prüfen — der Agent soll zuerst erreichbar sein.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// So lange wird dieselbe Version nicht noch einmal installiert.
    ///
    /// Schutz gegen eine Startschleife: schlüge der Tausch fehl (etwa weil die
    /// Datei gesperrt ist), fände der nächste Start dieselbe neue Version, würde
    /// wieder tauschen und neu starten — endlos.
    /// </summary>
    private static readonly TimeSpan RetryBlock = TimeSpan.FromMinutes(30);

    /// <summary>Wartezeit im Skript, bis der alte Prozess wirklich weg ist.</summary>
    private const int ShutdownWaitSeconds = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = configuration["Agent:HubUrl"];
        var hubToken = configuration["Agent:HubToken"];

        if (string.IsNullOrWhiteSpace(hubUrl) || string.IsNullOrWhiteSpace(hubToken))
        {
            logger.LogInformation(
                "Kein Hub konfiguriert (Agent:HubUrl / Agent:HubToken) — Selbst-Update ist aus.");

            return;
        }

        await Task.Delay(StartupDelay, stoppingToken);

        try
        {
            await CheckOnceAsync(hubUrl, hubToken, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ein fehlgeschlagenes Update darf nie den laufenden Agent
            // beeinträchtigen — im Zweifel bleibt eben die alte Version.
            logger.LogWarning(ex, "Update-Prüfung fehlgeschlagen.");
        }
    }

    private async Task CheckOnceAsync(string hubUrl, string hubToken, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", hubToken);

        var manifestJson = await client.GetStringAsync(
            $"{hubUrl.TrimEnd('/')}/api/agent/manifest", cancellationToken);

        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(
            manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return;
        }

        var current = Environment.ProcessPath;

        if (current is null)
        {
            logger.LogDebug("Eigener Pfad unbekannt — Update übersprungen.");
            return;
        }

        if (await HashFileAsync(current, cancellationToken) == manifest.Sha256)
        {
            logger.LogDebug("Agent ist aktuell.");
            return;
        }

        if (WasTriedRecently(current, manifest.Sha256))
        {
            logger.LogWarning(
                "Version {Hash} wurde eben erst erfolglos installiert — überspringe sie.",
                manifest.Sha256[..8]);

            return;
        }

        logger.LogInformation("Neue Agent-Version auf dem Hub gefunden, lade herunter.");

        var staged = Path.Combine(
            Path.GetDirectoryName(current)!, $"{Path.GetFileName(current)}.new");

        await DownloadAsync(client, $"{hubUrl.TrimEnd('/')}/api/agent/download", staged, cancellationToken);

        if (await HashFileAsync(staged, cancellationToken) != manifest.Sha256)
        {
            // Abgebrochener Download oder etwas Schlimmeres — auf keinen Fall
            // installieren.
            logger.LogWarning("Prüfsumme der geladenen Datei passt nicht, verwerfe sie.");
            File.Delete(staged);
            return;
        }

        RememberAttempt(current, manifest.Sha256);
        Install(current, staged);
    }

    private static string AttemptMarkerPath(string current) => $"{current}.update";

    /// <summary>
    /// Wurde genau diese Version gerade schon einmal installiert? Dann hat der
    /// Tausch offenbar nicht gegriffen, und ein zweiter Versuch endete in einer
    /// Neustartschleife.
    /// </summary>
    private bool WasTriedRecently(string current, string hash)
    {
        var marker = AttemptMarkerPath(current);

        try
        {
            if (!File.Exists(marker))
            {
                return false;
            }

            return File.ReadAllText(marker).Trim() == hash
                   && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < RetryBlock;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne Merkzettel lieber weitermachen als gar nicht aktualisieren.
            logger.LogDebug(ex, "Update-Merkzettel nicht lesbar.");
            return false;
        }
    }

    private void RememberAttempt(string current, string hash)
    {
        try
        {
            File.WriteAllText(AttemptMarkerPath(current), hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Update-Merkzettel nicht schreibbar.");
        }
    }

    private static async Task DownloadAsync(
        HttpClient client, string url, string target, CancellationToken cancellationToken)
    {
        await using var source = await client.GetStreamAsync(url, cancellationToken);
        await using var file = File.Create(target);

        await source.CopyToAsync(file, cancellationToken);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Startet das Tausch-Skript und beendet sich. Wer den Agent gestartet hat —
    /// die Aufgabenplanung oder der Dienst — startet ihn danach wieder; das
    /// Skript hilft mit einem eigenen Startversuch nach.
    /// </summary>
    private void Install(string current, string staged)
    {
        var script = Path.Combine(Path.GetTempPath(), "remotedesktop-update.cmd");
        var directory = Path.GetDirectoryName(current)!;
        var backup = $"{current}.old";

        File.WriteAllText(script,
            $"""
            @echo off
            rem Wartet, bis der alte Agent beendet ist, tauscht die Datei und startet neu.
            timeout /t {ShutdownWaitSeconds} /nobreak > nul

            rem Das Arbeitsverzeichnis muss stimmen: der Agent liest seine
            rem appsettings.json von dort. Ohne das startet er und stirbt sofort.
            cd /d "{directory}"

            rem Die alte Fassung bleibt als .old liegen — wenn die neue nicht
            rem laufen sollte, ist der Weg zurück ein Umbenennen.
            if exist "{backup}" del "{backup}"
            move /y "{current}" "{backup}"
            move /y "{staged}" "{current}"

            start "" /d "{directory}" "{current}"
            del "%~f0"
            """);

        logger.LogInformation("Starte Tausch und beende mich.");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });

        Environment.Exit(0);
    }
}
