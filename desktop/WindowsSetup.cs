using System.Diagnostics;
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
    /// </summary>
    public void SetServiceStart(ServiceStart start)
    {
        var value = start == ServiceStart.Automatic ? "auto" : "demand";

        Run("sc.exe", ["config", Autostart.ServiceName, $"start={value}"]);
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

    /// <summary>
    /// Argumente einzeln, nie als eine Zeile — dieselbe Regel wie bei den
    /// Aktionen aus Phase 13. Es gibt hier keine Stelle, an der Windows
    /// entscheidet, was eine Zeichenkette bedeutet.
    /// </summary>
    internal static void Run(string file, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info);
        process?.WaitForExit(TimeSpan.FromSeconds(30));
    }
}

/// <summary>
/// Was auf diesem Rechner schon steht. Alles hier sind Abfragen — Dateien,
/// Registry, ein Aufruf von <c>tailscale status</c>. Ohne Windows sagt davon
/// nichts etwas aus, deshalb liegt hier auch keine Entscheidung.
/// </summary>
public sealed class WindowsProbe : ISetupProbe
{
    private const string TailscalePath = @"C:\Program Files\Tailscale\tailscale.exe";

    private static readonly string CertificateDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RemoteDesktopAgent");

    public bool HasTailscale => File.Exists(TailscalePath);

    public bool IsConnected => TailnetName.Length > 0;

    public string TailnetName => _name ??= ReadTailnetName();

    public bool HasCertificate => File.Exists(Path.Combine(CertificateDirectory, "cert.crt"))
                                  && File.Exists(Path.Combine(CertificateDirectory, "cert.key"));

    public bool HasService
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{Autostart.ServiceName}");

            return key is not null;
        }
    }

    private string? _name;

    /// <summary>
    /// Der eigene Name im Netz, den auch das Handy sieht. Er kommt von
    /// <c>tailscale status</c>, weil der Rechnername ein anderer sein kann.
    /// </summary>
    private string ReadTailnetName()
    {
        if (!HasTailscale)
        {
            return string.Empty;
        }

        try
        {
            var info = new ProcessStartInfo(TailscalePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            info.ArgumentList.Add("status");
            info.ArgumentList.Add("--json");

            using var process = Process.Start(info);

            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(10));

            using var document = System.Text.Json.JsonDocument.Parse(output);

            return document.RootElement.TryGetProperty("Self", out var self)
                   && self.TryGetProperty("DNSName", out var dns)
                ? dns.GetString()?.TrimEnd('.') ?? string.Empty
                : string.Empty;
        }
        catch (Exception)
        {
            // Tailscale nicht gestartet, kein Netz, unerwartetes JSON — für die
            // Frage „gehört dieser Rechner schon dazu?" ist das alles ein Nein.
            return string.Empty;
        }
    }
}
