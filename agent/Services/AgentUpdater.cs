using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace RemoteDesktopAgent.Services;

/// <summary>Wie eine Update-Prüfung ausgegangen ist.</summary>
public enum UpdateOutcome
{
    /// <summary>Kein Release-Schlüssel einkompiliert — Updates sind aus.</summary>
    Disabled,
    UpToDate,
    /// <summary>Kein Release oder keine Manifestdatei darin gefunden.</summary>
    NotFound,
    /// <summary>Unterschrift oder Prüfsumme passten nicht. Es wird nichts installiert.</summary>
    Rejected,
    /// <summary>Dieselbe Fassung ist eben erst erfolglos installiert worden.</summary>
    Skipped,
    /// <summary>Der Tausch läuft, der Agent beendet sich gleich.</summary>
    Installing,
    Failed
}

public sealed record UpdateResult(UpdateOutcome Outcome, string? Version = null);

/// <summary>
/// Holt neue Fassungen des Agents aus den GitHub-Releases und tauscht sich
/// selbst aus.
///
/// Der Agent läuft auf zwei Rechnern, die beide nicht in Reichweite sind, wenn
/// man gerade unterwegs ist — ohne Selbst-Update bedeutet jede Änderung einen
/// Gang zum PC. Sicherheitsnetz sind drei Dinge: das Manifest ist mit einem
/// Schlüssel unterschrieben, der nicht im Repo liegt; die geladene Datei wird
/// gegen die Prüfsumme aus dem unterschriebenen Manifest gehalten; und ein
/// Merkzettel verhindert die Startschleife, falls der Tausch nicht greift.
///
/// Getauscht wird von einem winzigen Batch-Skript: eine laufende .exe kann sich
/// unter Windows nicht selbst überschreiben.
/// </summary>
public sealed class AgentUpdater(
    IHttpClientFactory clients,
    ManifestVerifier verifier,
    string repository,
    ILogger<AgentUpdater> logger)
{
    /// <summary>
    /// So lange wird dieselbe Fassung nicht noch einmal installiert.
    ///
    /// Schutz gegen eine Startschleife: schlüge der Tausch fehl (etwa weil die
    /// Datei gesperrt ist), fände der nächste Start dieselbe neue Fassung, würde
    /// wieder tauschen und neu starten — endlos.
    /// </summary>
    private static readonly TimeSpan RetryBlock = TimeSpan.FromMinutes(30);

    /// <summary>Wartezeit im Skript, bis der alte Prozess wirklich weg ist.</summary>
    private const int ShutdownWaitSeconds = 5;

    public bool IsEnabled => verifier.IsConfigured;

    public async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!verifier.IsConfigured)
        {
            logger.LogInformation(
                "Kein Release-Schlüssel einkompiliert (ReleaseKeys.PublicKey) — Selbst-Update ist aus.");

            return new UpdateResult(UpdateOutcome.Disabled);
        }

        var client = clients.CreateClient();

        // Ohne User-Agent antwortet die GitHub-API mit 403. Unauthentifiziert
        // sind 60 Anfragen pro Stunde erlaubt — für eine Prüfung beim Start und
        // eine auf Knopfdruck reicht das um Größenordnungen.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "RemoteDesktopAgent", AgentVersion.Current));

        var release = GitHubRelease.Parse(await client.GetStringAsync(
            $"https://api.github.com/repos/{repository}/releases/latest", cancellationToken));

        var manifestUrl = release?.Download(GitHubRelease.ManifestAsset);
        var signatureUrl = release?.Download(GitHubRelease.SignatureAsset);

        if (manifestUrl is null || signatureUrl is null)
        {
            logger.LogInformation("Kein Release mit Manifest und Unterschrift gefunden.");
            return new UpdateResult(UpdateOutcome.NotFound);
        }

        var manifestBytes = await client.GetByteArrayAsync(manifestUrl, cancellationToken);
        var signature = (await client.GetStringAsync(signatureUrl, cancellationToken)).Trim();

        var manifest = verifier.Verify(manifestBytes, signature);

        if (manifest is null)
        {
            // Das ist der Fall, für den die Signatur da ist. Er verdient eine
            // Warnung und keinen Debug-Eintrag.
            logger.LogWarning("Release-Manifest ist nicht gültig unterschrieben — verworfen.");
            return new UpdateResult(UpdateOutcome.Rejected);
        }

        var current = Environment.ProcessPath;

        if (current is null)
        {
            logger.LogDebug("Eigener Pfad unbekannt — Update übersprungen.");
            return new UpdateResult(UpdateOutcome.Failed);
        }

        if (await HashFileAsync(current, cancellationToken) == manifest.Sha256)
        {
            logger.LogDebug("Agent ist aktuell ({Version}).", manifest.Version);
            return new UpdateResult(UpdateOutcome.UpToDate, manifest.Version);
        }

        if (WasTriedRecently(current, manifest.Sha256))
        {
            logger.LogWarning(
                "Fassung {Hash} wurde eben erst erfolglos installiert — überspringe sie.",
                manifest.Sha256[..8]);

            return new UpdateResult(UpdateOutcome.Skipped, manifest.Version);
        }

        var assetUrl = release!.Download(manifest.File);

        if (assetUrl is null)
        {
            logger.LogWarning("Das Manifest nennt '{File}', das Release hat sie nicht.", manifest.File);
            return new UpdateResult(UpdateOutcome.NotFound, manifest.Version);
        }

        logger.LogInformation("Neue Agent-Fassung {Version} gefunden, lade herunter.", manifest.Version);

        var staged = Path.Combine(
            Path.GetDirectoryName(current)!, $"{Path.GetFileName(current)}.new");

        await DownloadAsync(client, assetUrl, staged, cancellationToken);

        if (await HashFileAsync(staged, cancellationToken) != manifest.Sha256)
        {
            // Abgebrochener Download oder etwas Schlimmeres — auf keinen Fall
            // installieren.
            logger.LogWarning("Prüfsumme der geladenen Datei passt nicht zum Manifest, verwerfe sie.");
            File.Delete(staged);

            return new UpdateResult(UpdateOutcome.Rejected, manifest.Version);
        }

        RememberAttempt(current, manifest.Sha256);
        Install(current, staged);

        return new UpdateResult(UpdateOutcome.Installing, manifest.Version);
    }

    private static string AttemptMarkerPath(string current) => $"{current}.update";

    /// <summary>
    /// Wurde genau diese Fassung gerade schon einmal installiert? Dann hat der
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
