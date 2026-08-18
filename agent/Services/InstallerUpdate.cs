using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace RemoteDesktopAgent.Services;

/// <summary>Wie ein Voll-Update ausgegangen ist.</summary>
public enum InstallerOutcome
{
    /// <summary>Kein Release-Schlüssel einkompiliert — Updates sind aus.</summary>
    Disabled,
    UpToDate,
    /// <summary>Kein Release oder kein Installer-Manifest darin gefunden.</summary>
    NotFound,
    /// <summary>Unterschrift oder Prüfsumme passten nicht. Es wird nichts ausgeführt.</summary>
    Rejected,
    /// <summary>Der Installer läuft, Agent und Fenster gehen gleich aus.</summary>
    Installing,
    Failed
}

public sealed record InstallerResult(InstallerOutcome Outcome, string? Version = null);

/// <summary>
/// Das <b>ganze</b> Update: Agent, Fenster und Oberfläche in einem Zug — und
/// zwar so, dass es von einem gekoppelten Gerät aus angestoßen werden kann.
///
/// <para>
/// **Warum das neben <see cref="AgentUpdater"/> steht.** Der tauscht seine
/// eigene <c>.exe</c> und sonst nichts. Das reicht, solange nur der Agent sich
/// ändert; ändert sich die Oberfläche — und das ist der häufigere Fall —,
/// bleibt sie auf dem Stand von vorher, und niemand sieht, warum. Was beides
/// erneuert, ist der Installer.
/// </para>
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
/// gültige Unterschrift <em>und</em> passende Prüfsumme passiert nichts.
/// </para>
/// </summary>
public sealed class InstallerUpdate(
    IHttpClientFactory clients,
    ManifestVerifier verifier,
    string repository,
    ILogger<InstallerUpdate> logger)
{
    /// <summary>Das Manifest des Installers und seine Unterschrift daneben.</summary>
    public const string ManifestAsset = "installer.json";
    public const string SignatureAsset = "installer.json.sig";

    /// <summary>
    /// Was der Installer mitbekommt.
    ///
    /// <para>
    /// <c>/VERYSILENT</c> und nicht <c>/SILENT</c>: hier sieht niemand hin, und
    /// ein Fortschrittsbalken auf einem fremden Bildschirm ist keine Auskunft,
    /// sondern eine Überraschung. <c>/NOLAUNCH</c> hält das Fenster zu — es
    /// aufgehen zu lassen wäre das einzige Zeichen eines Vorgangs, den jemand
    /// anderes ausgelöst hat.
    /// </para>
    /// </summary>
    private static readonly string[] Arguments =
        ["/VERYSILENT", "/NORESTART", "/SUPPRESSMSGBOXES", "/NOLAUNCH"];

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
    public async Task<InstallerResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!verifier.IsConfigured)
        {
            logger.LogInformation(
                "Kein Release-Schlüssel einkompiliert (ReleaseKeys.PublicKey) — Voll-Update ist aus.");

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

        var assetUrl = release!.Download(manifest.File);

        if (assetUrl is null)
        {
            logger.LogWarning("Das Manifest nennt '{File}', das Release hat sie nicht.", manifest.File);
            return new InstallerResult(InstallerOutcome.NotFound, manifest.Version);
        }

        logger.LogInformation("Neue Fassung {Version} gefunden, lade den Installer.", manifest.Version);

        // In den Temp-Ordner und nicht neben die .exe: der Programmordner wird
        // vom Installer gleich umgebaut, und eine Datei darin, die er nicht
        // kennt, bliebe für immer liegen.
        var staged = Path.Combine(Path.GetTempPath(), manifest.File);

        await DownloadAsync(client, assetUrl, staged, cancellationToken);

        if (await HashFileAsync(staged, cancellationToken) != manifest.Sha256)
        {
            logger.LogWarning("Prüfsumme des Installers passt nicht zum Manifest, verwerfe ihn.");
            TryDelete(staged);

            return new InstallerResult(InstallerOutcome.Rejected, manifest.Version);
        }

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
        var script = Path.Combine(Path.GetTempPath(), "remotedesktop-setup.cmd");

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
            // Liegt im Temp-Ordner; Windows räumt ihn ohnehin auf.
        }
    }
}
