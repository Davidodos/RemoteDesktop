namespace RemoteDesktopAgent.Services;

/// <summary>Was beim Weckversuch herauskam.</summary>
public enum WakeOutcome
{
    Sent,
    BadMac,
    /// <summary>Zu viele Versuche in kurzer Zeit — siehe <see cref="WakeService"/>.</summary>
    TooMany,
    Failed
}

/// <summary>
/// Das Aussenden gekapselt, damit sich prüfen lässt, <b>was</b> hinausgeht,
/// ohne dass dabei tatsächlich ein Broadcast im Netz landet.
/// </summary>
public interface IMagicPacketSender
{
    Task SendAsync(string mac, CancellationToken cancellationToken);
}

/// <summary>Der echte Weg: UDP-Broadcast über <see cref="MagicPacket"/>.</summary>
public sealed class BroadcastSender(string broadcastAddress) : IMagicPacketSender
{
    public Task SendAsync(string mac, CancellationToken cancellationToken) =>
        MagicPacket.SendAsync(mac, broadcastAddress, cancellationToken);
}

/// <summary>
/// Weckt einen Rechner im eigenen Netz.
///
/// Der Schaden wäre gering — WOL kann nur einschalten, und nur im eigenen LAN —,
/// aber ein offener Broadcast-Sender ist trotzdem nichts, was man stehen lässt:
/// ohne Begrenzung taugte der Agent als Paket-Verstärker. Deshalb eine feste
/// Obergrenze pro Minute. Sie liegt hoch genug, dass ein ungeduldiger Nutzer
/// sie nie erreicht.
/// </summary>
public sealed class WakeService(IMagicPacketSender sender, TimeProvider time, ILogger<WakeService> logger)
{
    private const int MaxPerWindow = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly List<DateTimeOffset> _recent = [];
    private readonly object _gate = new();

    public async Task<WakeOutcome> WakeAsync(string? mac, CancellationToken cancellationToken)
    {
        var normalized = SiteIdentity.NormalizeMac(mac);

        if (normalized is null)
        {
            return WakeOutcome.BadMac;
        }

        if (!TryReserve())
        {
            logger.LogWarning("Weckversuche zu häufig — abgelehnt.");
            return WakeOutcome.TooMany;
        }

        try
        {
            await sender.SendAsync(normalized, cancellationToken);
            logger.LogInformation("Magic Packet an {Mac} gesendet.", normalized);

            return WakeOutcome.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Die Rohmeldung nennt Adressen und Schnittstellen; nach außen geht
            // nur, dass es nicht ging.
            logger.LogError(ex, "Magic Packet an {Mac} fehlgeschlagen.", normalized);
            return WakeOutcome.Failed;
        }
    }

    private bool TryReserve()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();

            _recent.RemoveAll(moment => now - moment > Window);

            if (_recent.Count >= MaxPerWindow)
            {
                return false;
            }

            _recent.Add(now);
            return true;
        }
    }
}
