using System.Security.Cryptography;
using System.Text;

namespace RemoteDesktopAgent.Auth;

/// <summary>Eine offene Sitzung: wer sie hat und was er damit darf.</summary>
public sealed record AgentSession(string ClientId, IReadOnlyList<string> Scopes)
{
    public bool Allows(string? scope) => scope is null || Scopes.Contains(scope);
}

/// <summary>
/// Die Sitzungstokens, die ein Client nach bestandener Signaturprüfung bekommt.
///
/// Sie liegen ausschließlich im Arbeitsspeicher und überleben keinen Neustart
/// des Agents. Das ist Absicht: nichts, was jemand auf der Platte finden könnte,
/// öffnet einen Zugang. Nach dem Neustart weist sich jeder Client neu aus, was
/// ihn eine Signatur und keine Sekunde kostet.
/// </summary>
public sealed class SessionStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private const int TokenBytes = 32;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly List<Entry> _sessions = [];

    public SessionStore(TimeProvider time)
    {
        _time = time;
    }

    private sealed record Entry(byte[] Token, AgentSession Session, DateTimeOffset ExpiresAt);

    public string Open(PairedClient client)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes));

        lock (_gate)
        {
            DropExpired();

            _sessions.Add(new Entry(
                Encoding.UTF8.GetBytes(token),
                new AgentSession(client.Id, client.Scopes),
                _time.GetUtcNow() + Lifetime));
        }

        return token;
    }

    /// <summary>
    /// Sucht die Sitzung zu einem vorgelegten Token.
    ///
    /// Die Liste wird durchgegangen statt in ein Dictionary geschlagen, damit
    /// jeder Vergleich in fester Zeit läuft. Bei einer Handvoll Sitzungen kostet
    /// das nichts; ein Hash-Zugriff dagegen verrät über die Laufzeit, ob ein
    /// Token überhaupt existiert.
    /// </summary>
    public AgentSession? Find(string presented)
    {
        var candidate = Encoding.UTF8.GetBytes(presented);

        lock (_gate)
        {
            DropExpired();

            AgentSession? found = null;

            foreach (var entry in _sessions)
            {
                if (CryptographicOperations.FixedTimeEquals(candidate, entry.Token))
                {
                    found = entry.Session;
                }
            }

            return found;
        }
    }

    /// <summary>
    /// Schließt alle Sitzungen eines Clients. Ohne das liefe ein widerrufenes
    /// Handy bis zum Ablauf seines Tokens weiter — der Widerruf muss sofort
    /// wirken, sonst ist er keiner.
    /// </summary>
    public void CloseAll(string clientId)
    {
        lock (_gate)
        {
            _sessions.RemoveAll(entry => entry.Session.ClientId == clientId);
        }
    }

    private void DropExpired()
    {
        var now = _time.GetUtcNow();

        _sessions.RemoveAll(entry => entry.ExpiresAt <= now);
    }
}
