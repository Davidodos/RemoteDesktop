using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace RemoteDesktopAgent.Services;

/// <summary>Wie ein Update ausgegangen ist.</summary>
public enum InstallerOutcome
{
    /// <summary>Kein Release-Schlüssel einkompiliert — Updates sind aus.</summary>
    Disabled,
    UpToDate,
    /// <summary>Kein Release oder kein Installer-Manifest darin gefunden.</summary>
    NotFound,
    /// <summary>Unterschrift oder Prüfsumme passten nicht. Es wird nichts ausgeführt.</summary>
    Rejected,
    /// <summary>
    /// Diese Fassung wurde beim letzten Start schon einmal versucht und
    /// läuft trotzdem nicht — ein zweiter Anlauf von allein unterbleibt.
    /// </summary>
    Skipped,
    /// <summary>Der Installer läuft, Agent und Fenster gehen gleich aus.</summary>
    Installing,
    Failed
}

public sealed record InstallerResult(InstallerOutcome Outcome, string? Version = null);

/// <summary>
/// Das Update: Agent, Fenster und Oberfläche in einem Zug über den Installer —
/// beim Start von allein und auf Zuruf eines gekoppelten Geräts.
///
/// <para>
/// **Warum ohne Rückfrage von Windows.** Der Agent läuft als geplante Aufgabe
/// mit <c>HighestAvailable</c> (siehe <c>setup/AgentTask.cs</c>). Ein Prozess,
/// den er startet, erbt diesen Token — es gibt also nichts zu bestätigen. Genau
/// das ist die Bedingung dafür, dass sich ein Rechner vom Handy aus
/// aktualisieren lässt: vor ihm sitzt niemand, der auf „Ja" klicken könnte.
/// </para>
///
/// <para>
/// **Und deshalb wird geprüft.** Hier wird eine heruntergeladene Datei mit
/// vollen Rechten ausgeführt, ohne dass ein Mensch zusieht. Der Installer trägt
/// darum sein eigenes unterschriebenes Manifest im Release
/// (<c>installer.json</c>, siehe <c>scripts/sign-manifest.mjs</c>), und ohne
/// gültige Unterschrift <em>und</em> passende Prüfsumme passiert nichts. Und
/// die Datei liegt bis zum Start im admin-only Ordner des Agents, nicht in
/// <c>%TEMP%</c> — dort könnte sie jeder Prozess des Benutzers zwischen
/// Prüfsumme und Start austauschen.
/// </para>
/// </summary>
/// <param name="stagingDirectory">
/// Wohin Installer und Startskript geschrieben werden. Muss ein Ordner sein,
/// in den nur Administratoren und das System kommen.
/// </param>
public sealed class InstallerUpdate(
    IHttpClientFactory clients,
    ManifestVerifier verifier,
    string repository,
    string stagingDirectory,
    ILogger<InstallerUpdate> logger)
{
    /// <summary>Das Manifest des Installers und seine Unterschrift daneben.</summary>
    public const string ManifestAsset = "installer.json";
    public const string SignatureAsset = "installer.json.sig";

    /// <summary>
    /// Merkt sich, welche Fassung zuletzt von allein versucht wurde. Läuft nach
    /// dem Versuch immer noch die alte, kommt kein zweiter — sonst liefe der
    /// Rechner bei einem Installer, der immer scheitert, in einer Schleife aus
    /// Neustarts.
    /// </summary>
    public const string AttemptFile = "attempted.txt";

    /// <summary>
    /// Was der Installer mitbekommt.
    ///
    /// <c>/VERYSILENT</c> und nicht <c>/SILENT</c>: hier sieht niemand hin, und
    /// ein Fortschrittsbalken auf einem fremden Bildschirm ist keine Auskunft,
    /// sondern eine Überraschung. Kein <c>/NOLAUNCH</c>: der Agent läuft in der
    /// Sitzung eines angemeldeten Benutzers, und der hatte vor dem Update ein
    /// Fenster offen.
    /// </summary>
    private static readonly string[] Arguments =
        ["/VERYSILENT", "/NORESTART", "/SUPPRESSMSGBOXES"];

    /// <summary>
    /// Wartezeit im Startskript, bis Agent und Fenster wirklich weg sind.
    /// Der Installer räumt selbst noch einmal auf, aber ein Installer, der als
    /// Erstes auf eine gesperrte Datei stößt, meldet einen Fehler statt zu
    /// warten.
    /// </summary>
    private const int ShutdownWaitSeconds = 5;

    public bool IsEnabled => verifier.IsConfigured;

    /// <summary>
    /// Sucht ein neues Release und startet den Installer.
    ///
    /// <para>
    /// Kommt <see cref="InstallerOutcome.Installing"/> zurück, ist der Aufrufer
    /// dran: er muss die Antwort noch hinausschicken und sich <b>danach</b>
    /// beenden. Hier zu beenden hieße, die Antwort zu verschlucken — und die
    /// Gegenseite wartete auf eine Auskunft, die nie kommt.
    /// </para>
    /// </summary>
    /// <param name="automatic">
    /// Ob der Aufruf vom Start kommt und nicht von einem Menschen. Nur dann
    /// zählt der Merker aus <see cref="AttemptFile"/> — wer auf den Knopf
    /// drückt, will es noch einmal versuchen.
    /// </param>
    public async Task<InstallerResult> CheckAsync(bool automatic, CancellationToken cancellationToken)
    {
        if (!verifier.IsConfigured)
        {
            logger.LogInformation(
                "Kein Release-Schlüssel einkompiliert (ReleaseKeys.PublicKey) — Updates sind aus.");

            return new InstallerResult(InstallerOutcome.Disabled);
        }

        var client = clients.CreateClient();

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "RemoteDesktopAgent", AgentVersion.Current));

        var release = GitHubRelease.Parse(await client.GetStringAsync(
            $"https://api.github.com/repos/{repository}/releases/latest", cancellationToken));

        var manifestUrl = release?.Download(ManifestAsset);
        var signatureUrl = release?.Download(SignatureAsset);

        if (manifestUrl is null || signatureUrl is null)
        {
            logger.LogInformation("Kein Release mit Installer-Manifest gefunden.");
            return new InstallerResult(InstallerOutcome.NotFound);
        }

        var manifestBytes = await client.GetByteArrayAsync(manifestUrl, cancellationToken);
        var signature = (await client.GetStringAsync(signatureUrl, cancellationToken)).Trim();

        var manifest = verifier.Verify(manifestBytes, signature);

        if (manifest is null)
        {
            // Das ist der Fall, für den die Signatur da ist. Er verdient eine
            // Warnung und keinen Debug-Eintrag.
            logger.LogWarning("Installer-Manifest ist nicht gültig unterschrieben — verworfen.");
            return new InstallerResult(InstallerOutcome.Rejected);
        }

        if (IsSameVersion(manifest.Version))
        {
            logger.LogDebug("Installation ist aktuell ({Version}).", manifest.Version);
            return new InstallerResult(InstallerOutcome.UpToDate, manifest.Version);
        }

        if (automatic && WasAttempted(manifest.Version))
        {
            logger.LogWarning(
                "Fassung {Version} wurde beim letzten Start schon versucht und läuft nicht — "
                + "kein zweiter Anlauf von allein.", manifest.Version);

            return new InstallerResult(InstallerOutcome.Skipped, manifest.Version);
        }

        var assetUrl = release!.Download(manifest.File);

        if (assetUrl is null)
        {
            logger.LogWarning("Das Manifest nennt '{File}', das Release hat sie nicht.", manifest.File);
            return new InstallerResult(InstallerOutcome.NotFound, manifest.Version);
        }

        logger.LogInformation("Neue Fassung {Version} gefunden, lade den Installer.", manifest.Version);

        Directory.CreateDirectory(stagingDirectory);

        var staged = Path.Combine(stagingDirectory, Path.GetFileName(manifest.File));

        await DownloadAsync(client, assetUrl, staged, cancellationToken);

        if (await HashFileAsync(staged, cancellationToken) != manifest.Sha256)
        {
            logger.LogWarning("Prüfsumme des Installers passt nicht zum Manifest, verwerfe ihn.");
            TryDelete(staged);

            return new InstallerResult(InstallerOutcome.Rejected, manifest.Version);
        }

        MarkAttempted(manifest.Version);
        Launch(staged);

        return new InstallerResult(InstallerOutcome.Installing, manifest.Version);
    }

    /// <summary>
    /// Ob das angebotene Release schon läuft.
    ///
    /// Verglichen wird die Fassung und nicht die Prüfsumme: der Installer ist
    /// nicht die Datei, die hier läuft — er hat sie nur einmal abgelegt. Über
    /// <see cref="RemoteDesktopSetup.ReleaseCheck.Normalize"/>, weil in der
    /// eigenen Fassung seit .NET&#160;8 die Commit-Kennung mitsteht.
    /// </summary>
    private static bool IsSameVersion(string offered) =>
        string.Equals(
            RemoteDesktopSetup.ReleaseCheck.Normalize(offered),
            RemoteDesktopSetup.ReleaseCheck.Normalize(AgentVersion.Current),
            StringComparison.OrdinalIgnoreCase);

    private string AttemptPath => Path.Combine(stagingDirectory, AttemptFile);

    private bool WasAttempted(string version)
    {
        try
        {
            return File.Exists(AttemptPath)
                   && string.Equals(File.ReadAllText(AttemptPath).Trim(), version, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void MarkAttempted(string version)
    {
        try
        {
            File.WriteAllText(AttemptPath, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Der Update-Merker ließ sich nicht schreiben.");
        }
    }

    /// <summary>
    /// Startet den Installer über ein Zwischenskript und kehrt sofort zurück.
    ///
    /// <para>
    /// **Das Skript ist kein Umweg.** Der Installer beendet als Erstes den
    /// Agent — also den Prozess, der ihn gerade gestartet hat. Ein direkt
    /// gestarteter Kindprozess stünde in derselben Job-Zuordnung wie der Agent
    /// und ginge mit ihm unter, noch bevor er eine Datei kopiert hätte. Die
    /// Wartezeit davor sorgt dafür, dass der Agent von <em>allein</em> zu Ende
    /// ist, wenn der Installer anfängt — freiwillig beendet zieht er nichts mit
    /// sich.
    /// </para>
    /// </summary>
    private void Launch(string installer)
    {
        var script = Path.Combine(stagingDirectory, "remotedesktop-setup.cmd");

        File.WriteAllText(script,
            $"""
            @echo off
            rem Wartet, bis Agent und Fenster von allein beendet sind, und
            rem installiert dann die neue Fassung über die alte.
            timeout /t {ShutdownWaitSeconds} /nobreak > nul

            "{installer}" {string.Join(' ', Arguments)}

            del "{installer}"
            del "%~f0"
            """);

        logger.LogInformation("Starte den Installer und beende mich.");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bleibt liegen, bis der nächste Download sie überschreibt.
        }
    }
}
