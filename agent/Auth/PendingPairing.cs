namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Das Angebot der Gegenseite, sich auch in die andere Richtung zu koppeln.
/// </summary>
/// <param name="Host">Unter welcher Adresse die Gegenseite selbst erreichbar ist.</param>
/// <param name="Code">
/// Ein frischer Kopplungscode **ihres** Agents. Er ist fünf Minuten gültig und
/// einmal verwendbar — dieselben Regeln wie beim Code auf dem Bildschirm.
/// </param>
/// <param name="CaFingerprint">
/// Womit sich die Gegenseite ausweist. Er kommt hier über eine Verbindung, die
/// bereits beglaubigt ist — deshalb muss ihn niemand mehr ablesen und
/// vergleichen. Das ist der eigentliche Gewinn der Gegenkopplung.
/// </param>
public sealed record BackPairing(string Host, int Port, string Code, string? CaFingerprint, string? Name);

/// <summary>
/// Ein Angebot zur Gegenkopplung, das darauf wartet, eingelöst zu werden.
///
/// <para>
/// **Warum es überhaupt liegen bleibt:** wer koppelt, ist die *Client*-Seite —
/// sie hält den privaten Geräteschlüssel und die Geräteliste. Beim Agent liegt
/// beides nicht. Er kann das Angebot also nicht selbst einlösen; er hebt es
/// auf, bis die Oberfläche dieses Rechners danach fragt. Auf Windows ist das
/// das Fenster, am Handy die App.
/// </para>
///
/// <para>
/// Nur im Arbeitsspeicher und nur ein einziges: ein zweites Angebot ersetzt das
/// erste. Ein Angebot, das einen Neustart überlebt, wäre eine Einladung, die
/// niemand mehr erwartet — und der Code darin ist ohnehin nach fünf Minuten
/// wertlos.
/// </para>
/// </summary>
public sealed class PendingPairings
{
    /// <summary>
    /// So lange wird ein Angebot aufgehoben. Etwas kürzer als der Code darin
    /// gilt: ein Angebot, das noch dasteht, wenn sein Code längst verfallen ist,
    /// führt nur zu einer Fehlermeldung ohne Ursache.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(4);

    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private BackPairing? _offer;
    private DateTimeOffset _expiresAt;

    public PendingPairings(TimeProvider time)
    {
        _time = time;
    }

    public void Offer(BackPairing offer)
    {
        lock (_gate)
        {
            _offer = offer;
            _expiresAt = _time.GetUtcNow() + Lifetime;
        }
    }

    /// <summary>
    /// Holt das Angebot und verbraucht es dabei. Ein zweiter Aufruf liefert
    /// nichts — sonst versuchte die Oberfläche bei jedem Nachsehen erneut, sich
    /// mit einem längst eingelösten Code zu koppeln.
    /// </summary>
    public BackPairing? Take()
    {
        lock (_gate)
        {
            var offer = _offer;

            if (offer is null || _time.GetUtcNow() >= _expiresAt)
            {
                _offer = null;
                return null;
            }

            _offer = null;
            return offer;
        }
    }

    /// <summary>
    /// Prüft ein hereingereichtes Angebot. Unbrauchbares wird verworfen und
    /// nicht aufgehoben: die Kopplung selbst gelingt trotzdem, nur eben in eine
    /// Richtung. Ein halbes Angebot aufzuheben hieße, später an einer Stelle zu
    /// scheitern, an der niemand mehr weiß, woher es kam.
    /// </summary>
    public static BackPairing? Sanitize(
        string? host, int? port, string? code, string? caFingerprint, string? name)
    {
        var address = (host ?? string.Empty).Trim();
        var digits = (code ?? string.Empty).Trim();

        if (address.Length == 0 || address.Length > 255)
        {
            return null;
        }

        if (port is not (> 0 and <= 65535))
        {
            return null;
        }

        if (digits.Length != 6 || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        var fingerprint = (caFingerprint ?? string.Empty).Trim().ToLowerInvariant();

        return new BackPairing(
            address,
            port.Value,
            digits,
            fingerprint.Length == 64 && fingerprint.All(Uri.IsHexDigit) ? fingerprint : null,
            string.IsNullOrWhiteSpace(name) ? null : name.Trim());
    }
}
