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
/// Die Zugangsprüfung des Agents: gekoppelte Clients mit Sitzungstoken, sonst
/// nichts.
///
/// Der Agent hat volle Kontrolle über den Rechner. Deshalb wird hier zweimal
/// gefragt — wer bist du, und darfst du das? Ein Client, der nur das Widget
/// bedient, kommt damit nicht an das Herunterfahren heran.
///
/// Das alte geteilte Token aus der Zeit vor der Kopplung (<c>Agent:Token</c>)
/// gibt es seit v1.4 nicht mehr: ein Geheimnis, das für alles gilt und keine
/// Rechte kennt, hat in einer veröffentlichten Fassung nichts verloren.
/// </summary>
public sealed class ClientAuth(SessionStore sessions)
{
    /// <summary>
    /// Browser können bei WebSocket-Verbindungen und bei <c>&lt;img&gt;</c>
    /// keine eigenen Header setzen. Deshalb ist das Sitzungstoken dort im
    /// Query-String erlaubt — die Verbindung ist TLS-verschlüsselt, und der
    /// Agent loggt keine Query-Strings.
    /// </summary>
    public const string QueryParameter = "token";

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

        var session = sessions.Find(presented);

        if (session is null)
        {
            return new AuthResult(AuthOutcome.UnknownCredential, null, null);
        }

        return session.Allows(required)
            ? new AuthResult(AuthOutcome.Ok, session.ClientId, required)
            : new AuthResult(AuthOutcome.MissingScope, session.ClientId, required);
    }
}
