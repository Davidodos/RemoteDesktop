using Microsoft.Win32;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Die Windows-Seite der Einrichtung: hier wird ausgeführt, was
/// <c>setup/</c> entschieden hat.
///
/// Die Trennung ist der Grund, warum sich die Einrichtungslogik überhaupt prüfen
/// lässt — diese Datei ist die dünne Schicht, die es nicht kann, und sie enthält
/// deshalb keine Entscheidung, nur Handgriffe.
/// </summary>
public sealed class WindowsAutostart : IAutostartHost
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _clientPath;

    public WindowsAutostart(string clientPath) => _clientPath = clientPath;

    public AutostartPlan Current() => new(ReadServiceStart(), ReadClientEntry());

    /// <summary>
    /// Der Starttyp geht über <c>sc.exe</c> und nicht über die Registry: der
    /// Dienstmanager merkt sich mehr als einen Schlüssel, und wer daran vorbei
    /// schreibt, bekommt einen Zustand, den die Dienstverwaltung anders sieht als
    /// er ist.
    ///
    /// Er verlangt Adminrechte — deshalb der Sprung. Ist der Dienst gar nicht
    /// eingetragen, gibt es nichts umzustellen; eine Nachfrage nach Rechten für
    /// nichts wäre eine Zumutung.
    /// </summary>
    public void SetServiceStart(ServiceStart start)
    {
        if (!AgentService.Installed)
        {
            return;
        }

        var result = Elevation.Run(
            AdminTask.ServiceStartType, start == ServiceStart.Automatic ? "auto" : "demand");

        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    public void SetClientEntry(bool enabled)
    {
        // HKEY_CURRENT_USER und nicht _LOCAL_MACHINE: das Fenster gehört zu
        // einem angemeldeten Menschen. Für alle Benutzer zu entscheiden wäre
        // eine Anmaßung und bräuchte Adminrechte, die der Client sonst nirgends
        // verlangt.
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(Autostart.ClientEntryName, $"\"{_clientPath}\"");
        }
        else
        {
            key.DeleteValue(Autostart.ClientEntryName, throwOnMissingValue: false);
        }
    }

    private static ServiceStart ReadServiceStart()
    {
        // 2 heißt automatisch, 3 auf Anforderung — so steht es im Dienstmanager.
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{Autostart.ServiceName}");

        return key?.GetValue("Start") is int start && start == 2
            ? ServiceStart.Automatic
            : ServiceStart.Manual;
    }

    private bool ReadClientEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);

        return key?.GetValue(Autostart.ClientEntryName) is not null;
    }
}

/// <summary>Wo <c>tailscale.exe</c> liegt, wenn es liegt.</summary>
public static class Tailscale
{
    public const string Download = "https://tailscale.com/download/windows";

    /// <summary>
    /// Der volle Pfad statt bloß <c>tailscale.exe</c>: ob das Programm im
    /// Suchpfad steht, hängt daran, wann der Rechner zuletzt neu gestartet
    /// wurde. Ein Aufruf, der davon abhängt, schlägt genau nach der Installation
    /// fehl — also dann, wenn ihn jemand zum ersten Mal braucht.
    /// </summary>
    public static string Executable { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Tailscale",
        "tailscale.exe");

    public static bool Installed => File.Exists(Executable);

    /// <summary>
    /// Der eigene Name im Netz, den auch das Handy sieht. Er kommt von
    /// <c>tailscale status</c>, weil der Rechnername ein anderer sein kann.
    /// </summary>
    public static string Name()
    {
        if (!Installed)
        {
            return string.Empty;
        }

        var result = ProcessRunner.Run(
            Executable, ["status", "--json"], TimeSpan.FromSeconds(10));

        if (!result.Ok)
        {
            return string.Empty;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(result.Output);

            return document.RootElement.TryGetProperty("Self", out var self)
                   && self.TryGetProperty("DNSName", out var dns)
                ? dns.GetString()?.TrimEnd('.') ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            // Tailscale nicht gestartet, kein Netz, unerwartetes JSON — für die
            // Frage „gehört dieser Rechner schon dazu?" ist das alles ein Nein.
            return string.Empty;
        }
    }
}

/// <summary>Wo die Programmdatei des Agents liegt.</summary>
public static class AgentBinary
{
    public const string FileName = "RemoteDesktopAgent.exe";

    /// <summary>
    /// Neben dem Fenster oder ein Verzeichnis darüber. Zwei Orte, weil der
    /// Installer beides nebeneinanderlegt, der Entwicklungsbau aber getrennte
    /// Ausgabeordner hat.
    /// </summary>
    public static string? Locate()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, FileName),
            Path.Combine(Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? ".", FileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// Der Dienst — ob er eingetragen ist und ob er antwortet.
/// </summary>
public static class AgentService
{
    /// <summary>Ob Windows ihn kennt.</summary>
    public static bool Installed
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{Autostart.ServiceName}");

            return key is not null;
        }
    }

    /// <summary>
    /// Ob er tatsächlich bedient.
    ///
    /// Gefragt wird der Agent selbst und nicht die Dienstverwaltung: ein Dienst
    /// kann laufen und trotzdem nichts beantworten. Für die Anzeige „läuft"
    /// zählt nur, was ein Handy davon hätte.
    /// </summary>
    public static async Task<bool> RespondsAsync()
    {
        using var handler = new HttpClientHandler
        {
            // Das Zertifikat lautet auf den Netznamen dieses Rechners, nicht auf
            // „localhost". Vertretbar ist das nur, weil die Verbindung den
            // Rechner nicht verlässt.
            ServerCertificateCustomValidationCallback = (message, _, _, _) =>
                message.RequestUri?.IsLoopback == true
        };

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://localhost:{LocalAgent.ConfiguredPort()}"),

            // Kurz: die Antwort steht in Millisekunden da oder gar nicht, und das
            // Fenster wartet darauf.
            Timeout = TimeSpan.FromSeconds(2)
        };

        try
        {
            return (await http.GetAsync("/health")).IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Was auf diesem Rechner schon steht. Alles hier sind Abfragen — Dateien,
/// Registry, ein Aufruf von <c>tailscale status</c>. Ohne Windows sagt davon
/// nichts etwas aus, deshalb liegt hier auch keine Entscheidung.
/// </summary>
public sealed class WindowsProbe : ISetupProbe
{
    private static readonly string CertificateDirectory = Elevation.DataDirectory;

    private string? _name;

    public bool HasTailscale => Tailscale.Installed;

    public bool IsConnected => TailnetName.Length > 0;

    public string TailnetName => _name ??= Tailscale.Name();

    public bool HasCertificate => File.Exists(Path.Combine(CertificateDirectory, "cert.crt"))
                                  && File.Exists(Path.Combine(CertificateDirectory, "cert.key"));

    public bool HasService => AgentService.Installed;

    /// <summary>
    /// Der Zustand aller Teile in einem Zug — die Vorlage für das, was das
    /// Fenster anzeigt.
    /// </summary>
    public async Task<Machine> SnapshotAsync(string? appDirectory) => new(
        AgentBinary: AgentBinary.Locate() is not null,
        AgentService: HasService,
        AgentRunning: HasService && await AgentService.RespondsAsync(),
        ClientFiles: appDirectory is not null,
        WebView2: WebView2Runtime.InstalledVersion() is not null,
        Tailscale: HasTailscale,
        TailscaleConnected: IsConnected,
        Certificate: HasCertificate);

    /// <summary>Nach einem Handgriff neu fragen, statt den alten Stand zu zeigen.</summary>
    public void Forget() => _name = null;
}
