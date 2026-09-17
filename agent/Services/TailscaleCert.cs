using System.Diagnostics;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// <c>tailscale cert</c> aus dem Agent heraus — für die Erneuerung.
///
/// Der Agent läuft erhöht und darf in <c>data\secret</c> schreiben; das
/// Fenster holt das Zertifikat nur einmal in der Einrichtung. Derselbe Aufruf
/// wie dort (<c>desktop/Elevation.cs</c>, <c>FetchCertificate</c>), nur ohne
/// Rückfrage von Windows.
/// </summary>
public static class TailscaleCert
{
    /// <summary>Der volle Pfad — siehe <c>desktop/WindowsSetup.cs</c>, <c>Tailscale.Executable</c>.</summary>
    public static string Executable { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Tailscale",
        "tailscale.exe");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    /// <returns><c>null</c> bei Erfolg, sonst ein Satz für das Log.</returns>
    public static async Task<string?> RenewAsync(
        string name, string certificatePath, string keyPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(Executable))
        {
            return "tailscale.exe liegt nicht unter Programme\\Tailscale.";
        }

        var info = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in new[] { "cert", "--cert-file", certificatePath, "--key-file", keyPath, name })
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);

            if (process is null)
            {
                return "tailscale.exe ließ sich nicht starten.";
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(Timeout);

            var error = process.StandardError.ReadToEndAsync(limit.Token);
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);

            await process.WaitForExitAsync(limit.Token);

            if (process.ExitCode != 0)
            {
                var message = (await error).Trim();

                return message.Length > 0 ? message : $"tailscale cert endete mit {process.ExitCode}.";
            }

            _ = await output;

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "tailscale cert antwortet nicht und wurde abgebrochen.";
        }
        catch (Exception failure) when (failure is IOException or System.ComponentModel.Win32Exception)
        {
            return failure.Message;
        }
    }
}
