using Windows.Media.Control;
using Windows.Storage.Streams;

namespace RemoteDesktopAgent.Services;

/// <summary>Was gerade auf dem Rechner läuft — eine Sitzung je Anwendung.</summary>
public sealed record MediaSessionInfo(
    string Id,
    string App,
    string Title,
    string Artist,
    string Album,
    string Status,
    bool IsCurrent,
    bool HasThumbnail,
    double PositionSeconds,
    double DurationSeconds,
    double PositionAgeSeconds);

/// <summary>
/// Liest die laufenden Medien-Sitzungen von Windows und steuert sie gezielt.
///
/// Dieselbe Quelle, aus der auch die Einblendung beim Drücken der Lautstärke-
/// Tasten ihre Titel bezieht. Gegenüber den Medien-Tasten hat das zwei Vorteile:
/// man sieht, <em>was</em> läuft, und man kann eine bestimmte App ansprechen,
/// statt blind die zu treffen, die Windows gerade für die vorderste hält.
///
/// Läuft nichts, ist die Liste leer — das ist kein Fehler.
/// </summary>
public sealed class MediaSessionReader(ILogger<MediaSessionReader> logger)
{
    /// <summary>Windows braucht für die Abfrage manchmal einen Moment; ewig warten wir nicht.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlyList<MediaSessionInfo>> GetSessionsAsync()
    {
        var manager = await RequestManagerAsync();

        if (manager is null)
        {
            return [];
        }

        var current = manager.GetCurrentSession();
        var sessions = new List<MediaSessionInfo>();

        foreach (var session in manager.GetSessions())
        {
            var info = await DescribeAsync(session, session.SourceAppUserModelId == current?.SourceAppUserModelId);

            if (info is not null)
            {
                sessions.Add(info);
            }
        }

        return sessions;
    }

    /// <summary>Titelbild der Sitzung, oder <c>null</c>, wenn die App keines liefert.</summary>
    public async Task<byte[]?> GetThumbnailAsync(string sessionId)
    {
        var session = await FindAsync(sessionId);

        if (session is null)
        {
            return null;
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(Timeout);

            if (properties.Thumbnail is null)
            {
                return null;
            }

            using var stream = await properties.Thumbnail.OpenReadAsync().AsTask().WaitAsync(Timeout);
            var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size);

            await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None)
                .AsTask().WaitAsync(Timeout);

            var bytes = new byte[buffer.Length];
            DataReader.FromBuffer(buffer).ReadBytes(bytes);

            return bytes;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Titelbild von {Session} nicht lesbar.", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Führt eine Aktion auf einer bestimmten Sitzung aus. Gibt zurück, ob es
    /// geklappt hat — nicht jede App unterstützt jede Aktion.
    /// </summary>
    public async Task<bool> ControlAsync(string sessionId, string action)
    {
        var session = await FindAsync(sessionId);

        if (session is null)
        {
            return false;
        }

        try
        {
            var task = action.ToLowerInvariant() switch
            {
                "playpause" => session.TryTogglePlayPauseAsync().AsTask(),
                "play" => session.TryPlayAsync().AsTask(),
                "pause" => session.TryPauseAsync().AsTask(),
                "next" => session.TrySkipNextAsync().AsTask(),
                "prev" => session.TrySkipPreviousAsync().AsTask(),
                "stop" => session.TryStopAsync().AsTask(),
                _ => null
            };

            return task is not null && await task.WaitAsync(Timeout);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Aktion {Action} auf {Session} fehlgeschlagen.", action, sessionId);
            return false;
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> FindAsync(string sessionId)
    {
        var manager = await RequestManagerAsync();

        return manager?.GetSessions()
            .FirstOrDefault(s => s.SourceAppUserModelId == sessionId);
    }

    private async Task<GlobalSystemMediaTransportControlsSessionManager?> RequestManagerAsync()
    {
        try
        {
            return await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                .AsTask()
                .WaitAsync(Timeout);
        }
        catch (Exception ex)
        {
            // Kommt vor, wenn der Agent in einer Sitzung ohne Desktop läuft.
            logger.LogDebug(ex, "Medien-Sitzungen nicht abfragbar.");
            return null;
        }
    }

    private async Task<MediaSessionInfo?> DescribeAsync(
        GlobalSystemMediaTransportControlsSession session, bool isCurrent)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(Timeout);
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var progress = MediaTimeline.Describe(
                timeline.StartTime,
                timeline.EndTime,
                timeline.Position,
                timeline.LastUpdatedTime,
                DateTimeOffset.UtcNow);

            return new MediaSessionInfo(
                Id: session.SourceAppUserModelId,
                App: MediaAppName.Describe(session.SourceAppUserModelId),
                Title: properties.Title ?? string.Empty,
                Artist: properties.Artist ?? string.Empty,
                Album: properties.AlbumTitle ?? string.Empty,
                Status: playback.PlaybackStatus.ToString().ToLowerInvariant(),
                IsCurrent: isCurrent,
                HasThumbnail: properties.Thumbnail is not null,
                PositionSeconds: progress.Position,
                DurationSeconds: progress.Duration,
                PositionAgeSeconds: progress.Age);
        }
        catch (Exception ex)
        {
            // Eine Sitzung, die gerade verschwindet, darf die Liste nicht kosten.
            logger.LogDebug(ex, "Sitzung {Session} nicht lesbar.", session.SourceAppUserModelId);
            return null;
        }
    }
}
