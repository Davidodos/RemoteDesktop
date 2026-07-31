using System.Security.Cryptography;

namespace RemoteDesktopAgent.Auth;

public enum PairOutcome
{
    Ok,
    BadCode,
    BadPublicKey,
    BadScope,
    BadLabel
}

public enum SessionOutcome
{
    Ok,
    UnknownClient,
    BadChallenge,
    BadSignature
}

public sealed record PairResult(PairOutcome Outcome, PairedClient? Client);

public sealed record SessionResult(SessionOutcome Outcome, string? Token, PairedClient? Client);

/// <summary>
/// Der Ablauf der Kopplung und der Anmeldung, ohne HTTP.
///
/// Bewusst getrennt von den Endpoints: hier steht, was gilt, dort nur, welcher
/// Statuscode dabei herauskommt. Sonst ließe sich die Kopplung — der
/// empfindlichste Teil des Agents — nur mit einem laufenden Webserver prüfen.
/// </summary>
public sealed class PairingService
{
    private const int MaxLabelLength = 64;

    private readonly ClientStore _clients;
    private readonly PairingCodes _codes;
    private readonly ChallengeStore _challenges;
    private readonly SessionStore _sessions;
    private readonly TimeProvider _time;

    public PairingService(
        ClientStore clients,
        PairingCodes codes,
        ChallengeStore challenges,
        SessionStore sessions,
        TimeProvider time)
    {
        _clients = clients;
        _codes = codes;
        _challenges = challenges;
        _sessions = sessions;
        _time = time;
    }

    /// <summary>
    /// Nimmt einen Client auf, der den angezeigten Code richtig eingetippt hat.
    ///
    /// Der Code wird zuerst geprüft und dabei verbraucht. Wer ihn errät, soll
    /// nicht dadurch einen zweiten Versuch bekommen, dass sein Schlüssel
    /// unbrauchbar war.
    /// </summary>
    public PairResult Pair(string code, string label, string publicKey, IReadOnlyList<string>? scopes)
    {
        if (!_codes.TryRedeem(code))
        {
            return new PairResult(PairOutcome.BadCode, null);
        }

        var trimmed = label.Trim();

        if (trimmed.Length == 0 || trimmed.Length > MaxLabelLength)
        {
            return new PairResult(PairOutcome.BadLabel, null);
        }

        if (!IsUsablePublicKey(publicKey))
        {
            return new PairResult(PairOutcome.BadPublicKey, null);
        }

        var granted = scopes is null || scopes.Count == 0 ? AgentScopes.All : scopes;

        if (granted.Any(scope => !AgentScopes.IsKnown(scope)))
        {
            return new PairResult(PairOutcome.BadScope, null);
        }

        var now = _time.GetUtcNow();

        // Die Kennung kommt aus dem Schlüssel selbst. Koppelt dasselbe Gerät
        // erneut — etwa nach einer Neuinstallation der App —, ersetzt der neue
        // Eintrag den alten, statt die Liste mit Karteileichen zu füllen.
        var client = new PairedClient(
            FingerprintOf(publicKey),
            trimmed,
            publicKey,
            [.. granted],
            now,
            now);

        _clients.Add(client);

        return new PairResult(PairOutcome.Ok, client);
    }

    /// <returns>Die Challenge, oder <c>null</c> bei unbekanntem Client.</returns>
    public string? Challenge(string clientId) =>
        _clients.Find(clientId) is null ? null : _challenges.Issue(clientId);

    /// <summary>
    /// Prüft die Unterschrift über die Challenge und öffnet bei Erfolg eine
    /// Sitzung.
    /// </summary>
    public SessionResult OpenSession(string clientId, string nonce, string signature)
    {
        var client = _clients.Find(clientId);

        if (client is null)
        {
            return new SessionResult(SessionOutcome.UnknownClient, null, null);
        }

        if (!_challenges.TryConsume(clientId, nonce, out var data))
        {
            return new SessionResult(SessionOutcome.BadChallenge, null, null);
        }

        if (!AgentIdentity.VerifyClientSignature(client.PublicKey, data, signature))
        {
            return new SessionResult(SessionOutcome.BadSignature, null, null);
        }

        _clients.Touch(client.Id, _time.GetUtcNow());

        return new SessionResult(SessionOutcome.Ok, _sessions.Open(client), client);
    }

    /// <summary>
    /// Widerruft einen Client und wirft ihn zugleich aus seinen laufenden
    /// Sitzungen. Beides gehört zusammen — der Eintrag allein zu löschen
    /// verschöbe die Wirkung um bis zu zwölf Stunden.
    /// </summary>
    public bool Revoke(string clientId)
    {
        _sessions.CloseAll(clientId);

        return _clients.Revoke(clientId);
    }

    public IReadOnlyList<PairedClient> ListClients() => _clients.List();

    private static bool IsUsablePublicKey(string publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);

            // Nur P-256 wird angenommen. Eine andere Kurve wäre kein Angriff,
            // aber ein Fall, den nie jemand getestet hat.
            return key.ExportParameters(false).Curve.Oid.Value ==
                   ECCurve.NamedCurves.nistP256.Oid.Value;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static string FingerprintOf(string publicKey) =>
        Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKey)))[..16]
            .ToLowerInvariant();
}
