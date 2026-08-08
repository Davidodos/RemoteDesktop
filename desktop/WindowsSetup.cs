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
    /// Ob der Agent bei der Anmeldung von allein startet.
    ///
    /// <para>
    /// Umgestellt wird, indem die Aufgabe neu geschrieben wird — mit oder ohne
    /// Auslöser. Einen Auslöser nachträglich zu ändern geht mit
    /// <c>schtasks</c> nicht; die Beschreibung ist ohnehin die eine Quelle
    /// (siehe <see cref="AgentTask"/>).
    /// </para>
    ///
    /// <para>
    /// Es verlangt Adminrechte — deshalb der Sprung. Ist der Agent gar nicht
    /// eingetragen, gibt es nichts umzustellen; eine Nachfrage nach Rechten für
    /// nichts wäre eine Zumutung.
    /// </para>
    /// </summary>
    public void SetServiceStart(ServiceStart start)
    {
        if (!AgentService.Installed)
        {
            return;
        }

        var result = Elevation.Run(
            AdminTask.ServiceStartType,
            AgentTask.Argument(start == ServiceStart.Automatic, AgentService.InteractiveUser));

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

    /// <summary>
    /// Gefragt wird die Aufgabenplanung selbst, nicht die Registry: dort liegen
    /// die Auslöser in einem undokumentierten Binärformat.
    /// </summary>
    private static ServiceStart ReadServiceStart() =>
        AgentTask.StartsAtLogon(AgentService.Definition())
            ? ServiceStart.Automatic
            : ServiceStart.Manual;

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
/// Der Agent auf diesem Rechner — ob er eingetragen ist, ob sein Prozess läuft
/// und ob er antwortet.
///
/// <para>
/// Seit v1.3.0 ist er eine geplante Aufgabe und kein Dienst mehr; warum, steht
/// in <see cref="AgentTask"/>.
/// </para>
/// </summary>
public static class AgentService
{
    /// <summary>Wie der Prozess des Agents heißt, ohne <c>.exe</c>.</summary>
    private const string ProcessName = "RemoteDesktopAgent";

    /// <summary>
    /// Der Benutzer, in dessen Sitzung der Agent laufen soll. Wird beim Anlegen
    /// der Aufgabe mitgegeben — der erhöhte Aufruf kann ihn nicht ermitteln.
    /// </summary>
    public static string InteractiveUser =>
        $@"{Environment.UserDomainName}\{Environment.UserName}";

    /// <summary>
    /// Ob Windows die Aufgabe kennt.
    ///
    /// <para>
    /// Zwei Wege, weil beide für sich unzuverlässig sind: die Registry der
    /// Aufgabenplanung ist lesbar, aber undokumentiert, und die Datei unter
    /// <c>System32\Tasks</c> ist je nach Rechtevergabe nicht lesbar. Findet
    /// einer von beiden sie, ist sie da.
    /// </para>
    /// </summary>
    public static bool Installed
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\"
                + AgentTask.Name);

            return key is not null
                   || File.Exists(Path.Combine(
                       Environment.SystemDirectory, "Tasks", AgentTask.Name));
        }
    }

    /// <summary>
    /// Ob überhaupt ein Agent-Prozess läuft.
    ///
    /// <para>
    /// **Der Befund dahinter:** das Fenster fragte allein <c>/health</c> und
    /// meldete „gestoppt", sobald die Antwort ausblieb — auch dann, wenn der
    /// Agent lief und nur nicht bedienen konnte. Das ist ein Unterschied, der
    /// jemandem beim Suchen hilft: „läuft nicht" schickt zum Startknopf,
    /// „antwortet nicht" zum Port und zum Zertifikat.
    /// </para>
    /// </summary>
    public static bool ProcessRunning
    {
        get
        {
            try
            {
                var found = System.Diagnostics.Process.GetProcessesByName(ProcessName);

                foreach (var process in found)
                {
                    process.Dispose();
                }

                return found.Length > 0;
            }
            catch (Exception)
            {
                // Die Prozessliste darf gesperrt sein. Dann zählt eben nur die
                // Antwort auf /health.
                return false;
            }
        }
    }

    /// <summary>
    /// Ob der Dienst einer älteren Installation noch eingetragen ist.
    ///
    /// <para>
    /// Er ist nicht bloß ein Überbleibsel: solange er läuft, hält er Port 8443
    /// belegt und antwortet auch auf <c>/health</c> — von außen sieht alles in
    /// Ordnung aus. Er kann nur eben nicht das, wofür er da ist, weil er in
    /// Sitzung 0 keinen Bildschirm hat. Deshalb wird eigens darauf hingewiesen.
    /// </para>
    /// </summary>
    public static bool LegacyService
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{Autostart.ServiceName}");

            return key is not null;
        }
    }

    /// <summary>Die Beschreibung der Aufgabe, als XML — oder <c>null</c>.</summary>
    public static string? Definition()
    {
        var result = ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            ["/Query", "/TN", AgentTask.Name, "/XML", "ONE"],
            TimeSpan.FromSeconds(10));

        return result.Ok ? result.Output : null;
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
        AgentRunning: (HasService || AgentService.LegacyService)
                      && await AgentService.RespondsAsync(),
        AgentProcess: (HasService || AgentService.LegacyService) && AgentService.ProcessRunning,
        LegacyService: AgentService.LegacyService,
        ClientFiles: appDirectory is not null,
        WebView2: WebView2Runtime.InstalledVersion() is not null,
        Tailscale: HasTailscale,
        TailscaleConnected: IsConnected,
        Certificate: HasCertificate);

    /// <summary>Nach einem Handgriff neu fragen, statt den alten Stand zu zeigen.</summary>
    public void Forget() => _name = null;
}
