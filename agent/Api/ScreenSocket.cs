using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RemoteDesktopAgent.Capture;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Api;

/// <summary>Was die App beim Verbinden am Query-String mitgibt.</summary>
public sealed record ScreenStreamOptions(int Monitor, int Fps)
{
    private const int MinFps = 1;
    private const int MaxFps = 60;
    private const int DefaultFps = 30;

    public static ScreenStreamOptions FromQuery(IQueryCollection query)
    {
        var monitor = int.TryParse(query["monitor"], out var m) ? Math.Max(m, 0) : 0;

        var fps = int.TryParse(query["fps"], out var f)
            ? Math.Clamp(f, MinFps, MaxFps)
            : DefaultFps;

        return new ScreenStreamOptions(monitor, fps);
    }

    public TimeSpan FrameBudget => TimeSpan.FromSeconds(1.0 / Fps);
}

/// <summary>
/// Der Bild-WebSocket. Bewusst getrennt vom Eingabe-Socket: ein volles Bild im
/// Sendepuffer darf einen Mausklick nicht aufhalten.
///
/// Pro Bild gehen ein oder mehrere Binärnachrichten raus — je ein Ausschnitt,
/// bestehend aus <see cref="FrameHeader"/> und JPEG. Dazwischen liegen
/// Textnachrichten mit Metadaten und Statistik.
/// </summary>
public sealed class ScreenSocket(
    MonitorEnumerator monitors, DesktopBinder binder, ILogger<ScreenSocket> logger)
{
    /// <summary>Steuerbefehle der App sind klein; alles darüber ist kein gültiger Befehl.</summary>
    private const int ReceiveBufferSize = 1024;

    /// <summary>Nach so vielen misslungenen Anläufen erfährt die App, dass gerade kein Bild kommt.</summary>
    private const int LostFramesBeforeNotice = 10;

    /// <summary>Pause zwischen zwei Anläufen, wenn Windows die Aufnahme verweigert.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(1);

    public async Task HandleAsync(
        WebSocket socket, ScreenStreamOptions options, CancellationToken cancellationToken)
    {
        var all = monitors.Enumerate();
        var monitor = options.Monitor < all.Count ? all[options.Monitor] : null;

        if (monitor is null)
        {
            await SendTextAsync(socket, new
            {
                t = "error",
                message = $"Monitor {options.Monitor} gibt es nicht."
            }, cancellationToken);

            return;
        }

        DesktopDuplicator duplicator;

        try
        {
            duplicator = new DesktopDuplicator(monitor.DeviceName, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bildschirmaufnahme für {Monitor} nicht möglich.", monitor.DeviceName);

            await SendTextAsync(socket, new
            {
                t = "error",
                message = $"Bildschirmaufnahme nicht möglich: {ex.Message}"
            }, cancellationToken);

            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var control = new StreamControl();
        var receiving = ReceiveLoopAsync(socket, control, linked.Token);

        try
        {
            using (duplicator)
            {
                await StreamAsync(socket, duplicator, options, all.Count, control, linked.Token);
            }
        }
        finally
        {
            linked.Cancel();
            await receiving;
        }
    }

    private async Task StreamAsync(
        WebSocket socket,
        DesktopDuplicator duplicator,
        ScreenStreamOptions options,
        int monitorCount,
        StreamControl control,
        CancellationToken cancellationToken)
    {
        using var encoder = new JpegEncoder();
        var quality = new StreamQuality();
        var stats = new StreamStats();

        await SendTextAsync(socket, new
        {
            t = "meta",
            monitor = options.Monitor,
            width = duplicator.Width,
            height = duplicator.Height,
            fps = options.Fps,
            // Damit die App ihre Monitor-Tabs auch dann bauen kann, wenn
            // /api/info nicht durchkommt.
            count = monitorCount
        }, cancellationToken);

        var frames = new List<(CaptureRegion Region, byte[] Jpeg)>(DirtyRegionMerger.MaxRegions);
        var sendFullFrame = true;
        var lostInARow = 0;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            if (control.Paused)
            {
                // Die App pausiert, wenn sie in den Hintergrund geht. Ohne das
                // liefe der Stream in der Hosentasche weiter und würde Akku auf
                // beiden Seiten verbrennen.
                // Auch in der Pause weiterhin Statistik schicken: die App
                // erkennt eine tote Verbindung daran, dass gar nichts mehr
                // kommt — Stille wäre hier ein Fehlalarm.
                await SendStatsIfDueAsync(socket, stats, quality, cancellationToken);
                await Task.Delay(RetryDelay, cancellationToken);
                sendFullFrame = true;
                continue;
            }

            if (control.TakeRefreshRequest())
            {
                sendFullFrame = true;
            }

            quality.SetMode(control.Mode);

            var started = Stopwatch.GetTimestamp();
            var level = quality.Current;
            var full = new CaptureRegion(0, 0, duplicator.Width, duplicator.Height);

            frames.Clear();

            // Kodiert wird innerhalb des Callbacks: nur solange läuft der Zugriff
            // auf den Bildspeicher, und länger als nötig soll er nicht laufen.
            var status = duplicator.TryCapture((int)options.FrameBudget.TotalMilliseconds,
                (frame, dirty) =>
                {
                    IReadOnlyList<CaptureRegion> regions = sendFullFrame
                        ? [full]
                        : DirtyRegionMerger.Merge(dirty, frame.Width, frame.Height);

                    foreach (var region in regions)
                    {
                        frames.Add((region, encoder.Encode(frame, region, level)));
                    }
                });

            if (status == CaptureStatus.Lost)
            {
                // Nach einem Desktop-Wechsel (Sperrbildschirm, UAC) hängt der
                // Thread noch am alten. Der Aufruf steht bewusst direkt vor dem
                // nächsten Versuch, ohne await dazwischen — sonst läge die
                // Bindung womöglich auf einem anderen Threadpool-Thread.
                binder.BindToInputDesktop();

                // Erstes Bild danach muss vollständig sein — die App hat nach
                // einem Auflösungswechsel sonst ein halbes altes Bild stehen.
                sendFullFrame = true;

                if (++lostInARow == LostFramesBeforeNotice)
                {
                    await SendTextAsync(socket, new
                    {
                        t = "unavailable",
                        message = "Windows gibt den Bildschirm gerade nicht her " +
                                  "(Sperrbildschirm, UAC-Dialog oder Vollbildspiel)."
                    }, cancellationToken);
                }

                await SendStatsIfDueAsync(socket, stats, quality, cancellationToken);
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }

            if (lostInARow >= LostFramesBeforeNotice)
            {
                await SendTextAsync(socket, new { t = "available" }, cancellationToken);
            }

            lostInARow = 0;

            if (status == CaptureStatus.Timeout)
            {
                // Nichts geändert. Kein Frame senden — genau dafür gibt es die
                // Änderungsrechtecke.
                await SendStatsIfDueAsync(socket, stats, quality, cancellationToken);
                continue;
            }

            foreach (var (region, jpeg) in frames)
            {
                await SendFrameAsync(socket, region, jpeg, cancellationToken);
                stats.CountBytes(FrameHeader.Size + jpeg.Length);
            }

            sendFullFrame = false;
            stats.CountFrame();

            var cost = Stopwatch.GetElapsedTime(started);
            quality.Report(cost, options.FrameBudget);

            await SendStatsIfDueAsync(socket, stats, quality, cancellationToken);

            var remaining = options.FrameBudget - cost;

            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Header und Bild gehen als zwei Fragmente einer Nachricht raus. Das spart
    /// das Zusammenkopieren in einen dritten Puffer — der Client sieht trotzdem
    /// eine einzige zusammenhängende Nachricht.
    /// </summary>
    private static async Task SendFrameAsync(
        WebSocket socket, CaptureRegion region, byte[] jpeg, CancellationToken cancellationToken)
    {
        var header = new byte[FrameHeader.Size];
        FrameHeader.Write(header, region);

        await socket.SendAsync(
            header, WebSocketMessageType.Binary, endOfMessage: false, cancellationToken);

        await socket.SendAsync(
            jpeg, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
    }

    private static async Task SendStatsIfDueAsync(
        WebSocket socket, StreamStats stats, StreamQuality quality, CancellationToken cancellationToken)
    {
        if (!stats.TryTakeSnapshot(StatsInterval, out var fps, out var kilobitsPerSecond))
        {
            return;
        }

        await SendTextAsync(socket, new
        {
            t = "stats",
            fps = Math.Round(fps, 1),
            kbps = (int)Math.Round(kilobitsPerSecond),
            quality = quality.Current.Quality,
            scale = quality.Current.Scale,
            mode = quality.Mode.ToString().ToLowerInvariant()
        }, cancellationToken);
    }

    private static async Task SendTextAsync(
        WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        await socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Nimmt die Steuerbefehle der App entgegen. Läuft neben der Bildschleife,
    /// schreibt aber nur Flags — gesendet wird ausschließlich dort, damit sich
    /// zwei Sender nie ins Gehege kommen.
    /// </summary>
    private async Task ReceiveLoopAsync(
        WebSocket socket, StreamControl control, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, cancellationToken);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                control.Apply(Encoding.UTF8.GetString(buffer, 0, received.Count), logger);
            }
        }
        catch (OperationCanceledException)
        {
            // Bildschleife ist fertig.
        }
        catch (WebSocketException ex)
        {
            logger.LogInformation("Bild-Socket getrennt: {Message}", ex.Message);
        }
    }
}

/// <summary>Von der App gesetzte Wünsche, gelesen von der Bildschleife.</summary>
internal sealed class StreamControl
{
    private volatile bool _paused;
    private volatile bool _refresh;
    private volatile int _mode = (int)QualityMode.Auto;

    public bool Paused => _paused;

    public QualityMode Mode => (QualityMode)_mode;

    /// <summary>Holt eine angeforderte Vollbild-Auffrischung ab und setzt sie zurück.</summary>
    public bool TakeRefreshRequest()
    {
        if (!_refresh)
        {
            return false;
        }

        _refresh = false;
        return true;
    }

    public void Apply(string message, ILogger logger)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            switch (root.GetProperty("t").GetString())
            {
                case "pause":
                    _paused = true;
                    break;

                case "resume":
                    _paused = false;
                    _refresh = true;
                    break;

                case "refresh":
                    _refresh = true;
                    break;

                case "quality" when root.TryGetProperty("value", out var value) &&
                                    Enum.TryParse<QualityMode>(
                                        value.GetString(), ignoreCase: true, out var mode):
                    _mode = (int)mode;
                    _refresh = true;
                    break;

                default:
                    logger.LogDebug("Unbekannter Bild-Befehl: {Message}", message);
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Ein kaputter Steuerbefehl darf den laufenden Stream nicht kosten.
            logger.LogDebug("Ungültiger Bild-Befehl: {Message}", message);
        }
    }
}

/// <summary>Zählt Bilder und Bytes für die Anzeige in der App.</summary>
internal sealed class StreamStats
{
    private long _windowStart = Stopwatch.GetTimestamp();
    private int _frames;
    private long _bytes;

    public void CountFrame() => _frames++;

    public void CountBytes(int count) => _bytes += count;

    /// <summary>Liefert Werte, sobald das Zeitfenster voll ist, und beginnt dann ein neues.</summary>
    public bool TryTakeSnapshot(TimeSpan interval, out double fps, out double kilobitsPerSecond)
    {
        var elapsed = Stopwatch.GetElapsedTime(_windowStart);

        if (elapsed < interval)
        {
            fps = 0;
            kilobitsPerSecond = 0;
            return false;
        }

        fps = _frames / elapsed.TotalSeconds;
        kilobitsPerSecond = _bytes * 8 / 1000.0 / elapsed.TotalSeconds;

        _windowStart = Stopwatch.GetTimestamp();
        _frames = 0;
        _bytes = 0;

        return true;
    }
}
