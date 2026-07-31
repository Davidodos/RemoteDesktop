using System.Security.Cryptography;
using System.Text;

namespace RemoteDesktopAgent.Auth;

public enum AuthOutcome
{
    Ok,

    /// <summary>Gar keine Berechtigung vorgelegt → 401.</summary>
    NoCredential,

    /// <summary>Vorgelegt, aber nicht (mehr) gültig → 401.</summary>
    UnknownCredential,

    /// <summary>Angemeldet, aber ohne das nötige Recht → 403.</summary>
    MissingScope,

    /// <summary>Pfad steht in keiner Zuordnung → 403, nicht durchlassen.</summary>
    UnknownPath
}

public sealed record AuthResult(AuthOutcome Outcome, string? ClientId, string? RequiredScope)
{
    public bool IsAllowed => Outcome == AuthOutcome.Ok;
}

/// <summary>
/// Die Zugangsprüfung des Agents: mehrere zugelassene Clients statt eines
/// geteilten Tokens.
///
/// Der Agent hat volle Kontrolle über den Rechner. Deshalb wird hier zweimal
/// gefragt — wer bist du, und darfst du das? Ein Client, der nur das Widget
/// bedient, kommt damit nicht an das Herunterfahren heran.
/// </summary>
public sealed class ClientAuth
{
    /// <summary>
    /// Browser können bei WebSocket-Verbindungen und bei <c>&lt;img&gt;</c>
    /// keine eigenen Header setzen. Deshalb ist das Sitzungstoken dort im
    /// Query-String erlaubt — die Verbindung ist TLS-verschlüsselt, und der
    /// Agent loggt keine Query-Strings.
    /// </summary>
    public const string QueryParameter = "token";

    private readonly SessionStore _sessions;
    private readonly byte[]? _legacyToken;

    /// <param name="legacyToken">
    /// Das alte geteilte Token aus der <c>appsettings.json</c>. Es bleibt bis
    /// Phase 12 gültig — wer es abschaltet, bevor alle Clients gekoppelt sind,
    /// sperrt sich von dem Rechner aus, an dem er gerade nicht sitzt.
    /// </param>
    public ClientAuth(SessionStore sessions, string? legacyToken)
    {
        _sessions = sessions;

        if (legacyToken is null)
        {
            return;
        }

        if (legacyToken.Length < 32)
        {
            throw new ArgumentException(
                "Agent-Token ist kürzer als 32 Zeichen. Der Agent hat volle Kontrolle " +
                "über den PC — ein schwaches Token ist keine Option.",
                nameof(legacyToken));
        }

        _legacyToken = Encoding.UTF8.GetBytes(legacyToken);
    }

    /// <summary>Ob der alte Token-Weg überhaupt noch offensteht.</summary>
    public bool HasLegacyToken => _legacyToken is not null;

    public AuthResult Authorize(string? presented, string path)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return new AuthResult(AuthOutcome.NoCredential, null, null);
        }

        if (!AgentScopes.TryResolve(path, out var required))
        {
            return new AuthResult(AuthOutcome.UnknownPath, null, null);
        }

        // Das alte Token gilt für alles. Es kennt keine Rechte — genau deshalb
        // wird es abgelöst.
        if (MatchesLegacyToken(presented))
        {
            return new AuthResult(AuthOutcome.Ok, null, required);
        }

        var session = _sessions.Find(presented);

        if (session is null)
        {
            return new AuthResult(AuthOutcome.UnknownCredential, null, null);
        }

        return session.Allows(required)
            ? new AuthResult(AuthOutcome.Ok, session.ClientId, required)
            : new AuthResult(AuthOutcome.MissingScope, session.ClientId, required);
    }

    private bool MatchesLegacyToken(string presented)
    {
        if (_legacyToken is null)
        {
            return false;
        }

        // Fixed-time-Vergleich: ein früher Abbruch würde die Token-Länge und
        // stellenweise den Inhalt verraten.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented), _legacyToken);
    }
}
