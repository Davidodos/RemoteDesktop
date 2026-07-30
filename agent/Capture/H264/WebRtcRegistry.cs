using System.Collections.Concurrent;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Hält die laufenden WebRTC-Sitzungen und räumt sie auf.
///
/// Jede Sitzung hängt an einem eigenen ffmpeg-Prozess. Bleibt eine liegen,
/// läuft dieser Prozess weiter und belegt den Hardware-Encoder — deshalb
/// begrenzt der Registry ihre Zahl und wirft beendete regelmäßig weg.
/// </summary>
public sealed class WebRtcRegistry(
    MonitorEnumerator monitors, ILoggerFactory loggerFactory, IConfiguration configuration)
    : IAsyncDisposable
{
    /// <summary>Mehr gleichzeitige Ströme braucht ein Handy nicht, und die GPU dankt es.</summary>
    private const int MaxSessions = 2;

    private readonly ConcurrentDictionary<string, WebRtcSession> _sessions = new();
    private readonly ILogger _logger = loggerFactory.CreateLogger<WebRtcRegistry>();

    /// <summary>Baut eine Sitzung auf. Null heißt: kein H.264 möglich, App bleibt bei JPEG.</summary>
    public async Task<WebRtcSession?> CreateAsync(
        string offerSdp, int monitor, int framerate, CancellationToken cancellationToken)
    {
        await RemoveDeadAsync();

        if (_sessions.Count >= MaxSessions)
        {
            _logger.LogWarning("Zu viele WebRTC-Sitzungen, Angebot abgelehnt.");
            return null;
        }

        var ffmpegPath = configuration["Agent:FfmpegPath"] ?? "ffmpeg";
        var source = new FfmpegVideoSource(ffmpegPath, loggerFactory.CreateLogger<FfmpegVideoSource>());
        var session = new WebRtcSession(
            source, monitors, framerate, loggerFactory.CreateLogger<WebRtcSession>());

        var answer = await session.AcceptOfferAsync(offerSdp, monitor, cancellationToken);

        if (answer is null)
        {
            await session.DisposeAsync();
            return null;
        }

        _sessions[session.Id] = session;

        return session;
    }

    public WebRtcSession? Find(string id) => _sessions.GetValueOrDefault(id);

    public async Task<bool> CloseAsync(string id)
    {
        if (!_sessions.TryRemove(id, out var session))
        {
            return false;
        }

        await session.DisposeAsync();

        return true;
    }

    /// <summary>
    /// Wirft weg, was der Browser nicht mehr braucht. Ein Handy, das ins
    /// Funkloch fährt, meldet sich nicht ab — die Sitzung fällt dann von selbst
    /// auf <c>failed</c>.
    /// </summary>
    private async Task RemoveDeadAsync()
    {
        foreach (var (id, session) in _sessions)
        {
            if (session.State is SIPSorcery.Net.RTCPeerConnectionState.failed
                or SIPSorcery.Net.RTCPeerConnectionState.closed)
            {
                _sessions.TryRemove(id, out _);
                await session.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (id, session) in _sessions)
        {
            _sessions.TryRemove(id, out _);
            await session.DisposeAsync();
        }
    }
}
