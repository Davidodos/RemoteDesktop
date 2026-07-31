namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Ein Client, der sich einmal an diesem Rechner angemeldet hat.
///
/// Gespeichert wird nur der öffentliche Schlüssel. Der Agent kann damit prüfen,
/// ob der Client der ist, für den er sich ausgibt — aber er kann sich nicht
/// selbst als dieser Client ausgeben. Wer die <c>clients.json</c> liest, hat
/// deshalb nichts in der Hand.
/// </summary>
/// <param name="PublicKey">
/// Öffentlicher ECDSA-P-256-Schlüssel als Base64 im SPKI-Format.
/// </param>
public sealed record PairedClient(
    string Id,
    string Label,
    string PublicKey,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt)
{
    public bool Allows(string? scope) => scope is null || Scopes.Contains(scope);
}
