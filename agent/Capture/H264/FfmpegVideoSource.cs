using System.Diagnostics;

namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Liefert H.264-Bilder eines Monitors, erzeugt von einem ffmpeg-Prozess.
///
/// ffmpeg statt eigener Encoder-Anbindung: die Hardware-Encoder von NVIDIA,
/// Intel und AMD haben jeweils eigene SDKs, und ffmpeg spricht alle drei schon.
/// Der Preis ist eine externe Abhängigkeit — fehlt ffmpeg, fällt der Agent auf
/// den JPEG-Stream zurück.
/// </summary>
public sealed class FfmpegVideoSource : IAsyncDisposable
{
    /// <summary>So lange warten wir auf das erste Bild, bevor ein Encoder als untauglich gilt.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);

    private const int ReadBufferSize = 64 * 1024;

    private readonly string _ffmpegPath;
    private readonly ILogger _logger;

    private Process? _process;
    private CancellationTokenSource? _pumping;
    private Task? _pump;

    public FfmpegVideoSource(string ffmpegPath, ILogger logger)
    {
        _ffmpegPath = ffmpegPath;
        _logger = logger;
    }

    /// <summary>Ein fertiges Bild in Annex-B-Form.</summary>
    public event Action<byte[]>? FrameReady;

    /// <summary>Der Encoder, der es geschafft hat. Null, solange nichts läuft.</summary>
    public EncoderProfile? ActiveEncoder { get; private set; }

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Startet die Aufnahme und probiert dabei die Encoder der Reihe nach durch.
    /// Gibt zurück, ob einer davon Bilder geliefert hat.
    /// </summary>
    public async Task<bool> StartAsync(string deviceName, int framerate, CancellationToken cancellationToken)
    {
        await StopAsync();

        var (adapter, output) = DesktopDuplicator.LocateOutput(deviceName);

        foreach (var encoder in EncoderProfiles.All)
        {
            if (await TryStartAsync(encoder, adapter, output, framerate, cancellationToken))
            {
                ActiveEncoder = encoder;
                _logger.LogInformation(
                    "H.264-Stream läuft über {Encoder} via {Filter} (Adapter {Adapter}, Ausgang {Output}).",
                    encoder.Name, encoder.Filter, adapter, output);

                return true;
            }

            _logger.LogInformation(
                "{Encoder} via {Filter} liefert nichts, nächster Kandidat.",
                encoder.Name, encoder.Filter);
            await StopAsync();
        }

        return false;
    }

    private async Task<bool> TryStartAsync(
        EncoderProfile encoder, int adapter, int output, int framerate,
        CancellationToken cancellationToken)
    {
        var arguments = FfmpegCommand.Build(encoder, adapter, output, framerate);
        var info = new ProcessStartInfo(_ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        _logger.LogDebug("Starte ffmpeg: {Arguments}", FfmpegCommand.Describe(arguments));

        try
        {
            _process = Process.Start(info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffmpeg lässt sich nicht starten ({Path}).", _ffmpegPath);
            return false;
        }

        if (_process is null)
        {
            return false;
        }

        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _pumping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = Task.Run(() => PumpAsync(_process, firstFrame, _pumping.Token), CancellationToken.None);

        _ = Task.Run(() => LogErrorsAsync(_process), CancellationToken.None);

        await Task.WhenAny(firstFrame.Task, Task.Delay(StartupTimeout, cancellationToken));

        // Nur ein tatsächlich angekommenes Bild zählt. Endet ffmpeg vorzeitig,
        // wird derselbe Task abgebrochen — er ist dann zwar abgeschlossen, aber
        // eben nicht erfolgreich. Diese Unterscheidung ist der Unterschied
        // zwischen „läuft" und „meldet, dass es läuft, und liefert Schwarz".
        return firstFrame.Task.IsCompletedSuccessfully;
    }

    /// <summary>Liest die Pipe leer und meldet jedes fertige Bild.</summary>
    private async Task PumpAsync(
        Process process, TaskCompletionSource firstFrame, CancellationToken cancellationToken)
    {
        var splitter = new AnnexBSplitter();
        var buffer = new byte[ReadBufferSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await process.StandardOutput.BaseStream
                    .ReadAsync(buffer, cancellationToken);

                if (read == 0)
                {
                    break;
                }

                foreach (var frame in splitter.Push(buffer.AsSpan(0, read)))
                {
                    firstFrame.TrySetResult();
                    FrameReady?.Invoke(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stream wird beendet.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lesen des H.264-Stroms abgebrochen.");
        }
        finally
        {
            firstFrame.TrySetCanceled(CancellationToken.None);
        }
    }

    private async Task LogErrorsAsync(Process process)
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            // ffmpeg schreibt auch Fortschritt auf stderr; auf Warnstufe
            // gestartet bleibt hier nur übrig, was wirklich interessiert.
            _logger.LogWarning("ffmpeg: {Line}", line);
        }
    }

    public async Task StopAsync()
    {
        if (_pumping is not null)
        {
            await _pumping.CancelAsync();
        }

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // War schon beendet.
            }
        }

        if (_pump is not null)
        {
            await _pump;
        }

        _process?.Dispose();
        _process = null;
        _pumping?.Dispose();
        _pumping = null;
        _pump = null;
        ActiveEncoder = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
