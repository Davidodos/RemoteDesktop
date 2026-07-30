using RemoteDesktopAgent.Native;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Eine WebRTC-Verbindung zur App: ffmpeg encodiert, SIPSorcery paketiert und
/// verschickt, der Browser dekodiert in Hardware.
///
/// Kein STUN und kein TURN: beide Enden hängen im selben Tailnet, die direkte
/// Verbindung steht also immer. Ein öffentlicher STUN-Server würde die Adressen
/// des Rechners nur unnötig nach außen tragen.
/// </summary>
public sealed class WebRtcSession : IAsyncDisposable
{
    /// <summary>Takt der RTP-Zeitstempel bei Video — durch H.264 fest vorgegeben.</summary>
    private const int ClockRate = 90_000;

    /// <summary>Nutzlast-Kennung für H.264, wie sie im SDP steht.</summary>
    private const int PayloadType = 96;

    private readonly RTCPeerConnection _peer;
    private readonly FfmpegVideoSource _source;
    private readonly MonitorEnumerator _monitors;
    private readonly ILogger _logger;
    private readonly uint _frameDuration;
    private readonly int _framerate;

    private bool _sending;

    public WebRtcSession(
        FfmpegVideoSource source, MonitorEnumerator monitors, int framerate, ILogger logger)
    {
        _source = source;
        _monitors = monitors;
        _framerate = framerate;
        _frameDuration = (uint)(ClockRate / framerate);
        _logger = logger;

        _peer = new RTCPeerConnection(new RTCConfiguration { iceServers = [] });

        var format = new VideoFormat(VideoCodecsEnum.H264, PayloadType, ClockRate);
        var track = new MediaStreamTrack(format, MediaStreamStatusEnum.SendOnly);
        _peer.addTrack(track);

        _peer.onconnectionstatechange += OnConnectionStateChange;
        _source.FrameReady += OnFrameReady;
    }

    public string Id { get; } = Guid.NewGuid().ToString("n");

    public int Monitor { get; private set; }

    /// <summary>Die Antwort auf das Angebot der App, erst nach <see cref="AcceptOfferAsync"/> gesetzt.</summary>
    public string? AnswerSdp { get; private set; }

    /// <summary>Der Encoder, über den gerade gesendet wird — für die Anzeige in der App.</summary>
    public string? Encoder => _source.ActiveEncoder?.Name;

    public RTCPeerConnectionState State => _peer.connectionState;

    /// <summary>
    /// Nimmt das Angebot der App an und liefert die Antwort. Läuft ffmpeg nicht
    /// an, kommt <c>null</c> zurück und die App bleibt beim JPEG-Stream.
    /// </summary>
    public async Task<string?> AcceptOfferAsync(
        string offerSdp, int monitor, CancellationToken cancellationToken)
    {
        var target = ResolveMonitor(monitor);

        if (target is null)
        {
            return null;
        }

        Monitor = monitor;

        if (!await _source.StartAsync(target.DeviceName, _framerate, cancellationToken))
        {
            return null;
        }

        var result = _peer.setRemoteDescription(
            new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offerSdp });

        if (result != SetDescriptionResultEnum.OK)
        {
            _logger.LogWarning("Angebot der App abgelehnt: {Result}", result);
            return null;
        }

        var answer = _peer.createAnswer(null);
        await _peer.setLocalDescription(answer);

        AnswerSdp = answer.sdp;
        _sending = true;

        return answer.sdp;
    }

    /// <summary>
    /// Wechselt den Monitor, ohne die Verbindung anzufassen: nur ffmpeg startet
    /// neu. Die App behält ihren Videostrom und sieht nach einem Augenblick den
    /// anderen Bildschirm.
    /// </summary>
    public async Task<bool> SwitchMonitorAsync(int monitor, CancellationToken cancellationToken)
    {
        var target = ResolveMonitor(monitor);

        if (target is null)
        {
            return false;
        }

        _sending = false;

        var started = await _source.StartAsync(target.DeviceName, _framerate, cancellationToken);

        if (started)
        {
            Monitor = monitor;
        }

        _sending = started;

        return started;
    }

    private MonitorInfo? ResolveMonitor(int index)
    {
        var all = _monitors.Enumerate();

        return index >= 0 && index < all.Count ? all[index] : null;
    }

    private void OnFrameReady(byte[] frame)
    {
        if (!_sending || _peer.connectionState != RTCPeerConnectionState.connected)
        {
            return;
        }

        try
        {
            _peer.SendVideo(_frameDuration, frame);
        }
        catch (Exception ex)
        {
            // Ein einzelnes verlorenes Bild ist kein Grund, die Sitzung zu beenden.
            _logger.LogDebug(ex, "Bild konnte nicht gesendet werden.");
        }
    }

    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        _logger.LogInformation("WebRTC {Id}: {State}", Id, state);

        if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed
            or RTCPeerConnectionState.disconnected)
        {
            _sending = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sending = false;
        _source.FrameReady -= OnFrameReady;
        _peer.onconnectionstatechange -= OnConnectionStateChange;

        await _source.DisposeAsync();
        _peer.Close("Sitzung beendet.");
        _peer.Dispose();
    }
}
