using System.Diagnostics;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Services;

public enum PowerAction
{
    Sleep,
    Shutdown,
    Restart,
    Lock
}

/// <summary>
/// Power-Aktionen auf dem Windows-Host.
///
/// Shutdown und Restart laufen über <c>shutdown.exe</c> statt über
/// ExitWindowsEx: das Tool erledigt die Privilege-Anhebung
/// (SeShutdownPrivilege) selbst und verhält sich bei blockierenden
/// Anwendungen vorhersehbar.
/// </summary>
public sealed class PowerService(ILogger<PowerService> logger)
{
    /// <summary>Kulanzzeit, damit die HTTP-Antwort das Handy noch erreicht.</summary>
    private const int ShutdownDelaySeconds = 3;

    public void Execute(PowerAction action)
    {
        logger.LogWarning("Power-Aktion angefordert: {Action}", action);

        switch (action)
        {
            case PowerAction.Lock:
                if (!Win32.LockWorkStation())
                {
                    throw new InvalidOperationException("LockWorkStation ist fehlgeschlagen.");
                }
                break;

            case PowerAction.Sleep:
                // Erst antworten, dann schlafen legen — SetSuspendState kehrt
                // sonst erst beim Aufwachen zurück und die App läuft in einen
                // Timeout.
                RunDetached(() =>
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    Win32.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                });
                break;

            case PowerAction.Shutdown:
                RunShutdownTool($"/s /t {ShutdownDelaySeconds}");
                break;

            case PowerAction.Restart:
                RunShutdownTool($"/r /t {ShutdownDelaySeconds}");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unbekannte Power-Aktion.");
        }
    }

    private void RunShutdownTool(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("shutdown.exe konnte nicht gestartet werden.");

        process.WaitForExit(TimeSpan.FromSeconds(10));

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException(
                $"shutdown.exe {arguments} endete mit Code {process.ExitCode}: {error}");
        }
    }

    private void RunDetached(Action work)
    {
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Power-Aktion im Hintergrund fehlgeschlagen.");
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
    }
}
