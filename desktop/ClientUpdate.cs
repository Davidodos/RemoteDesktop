using System.Diagnostics;
using System.Net.Http.Headers;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Hält Agent und Client auf dem neuesten Stand, ohne dass jemand Dateien
/// kopiert.
///
/// Aktualisiert wird über **den Installer**, nicht über einzelne Dateien: der
/// Client ist ein Ordner mit Abhängigkeiten, der Agent ein Dienst, der erst
/// gestoppt werden muss. Beides kann der Installer, und er merkt sich an seiner
/// AppId, welche Komponenten beim letzten Mal gewählt waren — ein Update
/// installiert also nichts nach, was jemand bewusst weggelassen hat.
///
/// Das Selbst-Update des Agents (<c>POST /api/update</c>) bleibt daneben
/// bestehen. Es tauscht nur seine eigene <c>.exe</c> und ist der schnellere Weg,
/// wenn man ohnehin gerade in der App steht; dieser hier ist der, der auch das
/// Fenster erneuert.
/// </summary>
public sealed class ClientUpdate : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public ClientUpdate()
    {
        // Ohne User-Agent antwortet die GitHub-API mit 403. Das ist keine
        // Höflichkeit, sondern Bedingung.
        //
        // Über `TryParseAdd` und nicht über `Add`: die Fassung kommt aus dem
        // Kopf einer Datei auf der Platte, und was dort steht, bestimmt der
        // Build. Ein Zeichen darin, das ein HTTP-Kopf nicht verträgt, würde
        // sonst hier eine Ausnahme werfen — im Konstruktor, beim Öffnen des
        // Fensters, also weit weg von jeder Updatesuche.
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(
                $"RemoteDesktopClient/{InstalledVersion()}"))
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("RemoteDesktopClient", "0.0.0"));
        }
    }

    /// <summary>
    /// Die Fassung, die gerade läuft. Sie kommt beim Release aus dem Git-Tag
    /// (siehe <c>.github/workflows/release.yml</c>); in einem selbst gebauten
    /// Stand steht dort die Vorgabe aus der Projektdatei.
    ///
    /// <para>
    /// Durch <see cref="ReleaseCheck.Normalize"/>, weil im Dateikopf seit
    /// .NET&#160;8 die Commit-Kennung mit drinsteht: dort steht nicht
    /// <c>1.1.0</c>, sondern <c>1.1.0+435992d47c60…</c>. Das gehört weder in den
    /// Vergleich mit dem Release noch ins Fenster.
    /// </para>
    /// </summary>
    public static string InstalledVersion()
    {
        var path = Environment.ProcessPath;

        if (path is null)
        {
            return "0.0.0";
        }

        var info = FileVersionInfo.GetVersionInfo(path);

        return ReleaseCheck.Normalize(info.ProductVersion ?? info.FileVersion) is { Length: > 0 } version
            ? version
            : "0.0.0";
    }

    /// <summary><c>null</c> heißt: es gibt nichts Neues. Kein Fehler.</summary>
    public async Task<ReleaseOffer?> CheckAsync(CancellationToken cancellationToken)
    {
        string json;

        try
        {
            json = await _http.GetStringAsync(ReleaseCheck.LatestReleaseUrl, cancellationToken);
        }
        catch (Exception)
        {
            // Kein Netz, kein Release, ein Fehler von GitHub — für „gibt es
            // etwas Neues?" ist das alles dasselbe.
            return null;
        }

        var offer = ReleaseCheck.Find(json);

        return ReleaseCheck.IsWorthInstalling(offer, InstalledVersion()) ? offer : null;
    }

    /// <summary>
    /// Lädt den Installer und startet ihn. Danach beendet sich dieses Programm:
    /// eine laufende <c>.exe</c> lässt sich nicht ersetzen, und ein Installer,
    /// der auf ein Fenster wartet, das nie zugeht, wäre ein hängender Vorgang
    /// ohne jede Erklärung.
    /// </summary>
    public async Task InstallAsync(ReleaseOffer offer, CancellationToken cancellationToken)
    {
        var target = Path.Combine(
            Path.GetTempPath(),
            $"RemoteDesktop-Setup-{offer.Version}.exe");

        await using (var source = await _http.GetStreamAsync(offer.Url, cancellationToken))
        await using (var file = File.Create(target))
        {
            await source.CopyToAsync(file, cancellationToken);
        }

        // Über die Shell und nicht direkt: der Installer verlangt in seinem
        // Manifest Administratorrechte. Mit `UseShellExecute = false` gibt es
        // keine Rückfrage von Windows, sondern den Fehler „Elevation required" —
        // und das Update endete, bevor es anfing.
        var info = new ProcessStartInfo(target) { UseShellExecute = true, Verb = "runas" };

        foreach (var argument in ReleaseCheck.InstallArguments())
        {
            info.ArgumentList.Add(argument);
        }

        Process.Start(info)?.Dispose();
    }

    public void Dispose() => _http.Dispose();
}
