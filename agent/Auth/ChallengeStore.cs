using System.Security.Cryptography;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Die Zufallszahlen, mit denen sich ein gekoppelter Client ausweist.
///
/// Der Agent gibt eine aus, der Client unterschreibt sie mit seinem privaten
/// Schlüssel. Dass die Zahl nur einmal gilt und schnell verfällt, ist der ganze
/// Punkt: eine mitgeschnittene Unterschrift ist danach wertlos.
/// </summary>
public sealed class ChallengeStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private const int NonceBytes = 32;

    /// <summary>
    /// Obergrenze, damit ein Client nicht durch stures Anfordern von Challenges
    /// den Speicher des Agents füllen kann.
    /// </summary>
    private const int MaxOutstanding = 64;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string ClientId, DateTimeOffset ExpiresAt)> _open = [];

    public ChallengeStore(TimeProvider time)
    {
        _time = time;
    }

    /// <returns>Die Challenge als Base64 — so geht sie über JSON.</returns>
    public string Issue(string clientId)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));

        lock (_gate)
        {
            DropExpired();

            if (_open.Count >= MaxOutstanding)
            {
                _open.Clear();
            }

            _open[nonce] = (clientId, _time.GetUtcNow() + Lifetime);
        }

        return nonce;
    }

    /// <summary>
    /// Löst die Challenge ein. Gelingt es, ist sie verbraucht — auch bei einem
    /// späteren Fehlschlag der Signaturprüfung, sonst könnte man dieselbe
    /// Challenge beliebig oft gegen Unterschriften probieren.
    /// </summary>
    public bool TryConsume(string clientId, string nonce, out byte[] bytes)
    {
        bytes = [];

        lock (_gate)
        {
            DropExpired();

            if (!_open.Remove(nonce, out var entry) || entry.ClientId != clientId)
            {
                return false;
            }
        }

        try
        {
            bytes = Convert.FromBase64String(nonce);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void DropExpired()
    {
        var now = _time.GetUtcNow();

        foreach (var expired in _open.Where(entry => entry.Value.ExpiresAt <= now).ToList())
        {
            _open.Remove(expired.Key);
        }
    }
}
