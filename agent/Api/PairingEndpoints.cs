using RemoteDesktopAgent.Auth;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Kopplung, Anmeldung und Widerruf.
///
/// Die Regeln stehen in <see cref="PairingService"/> — hier wird nur übersetzt,
/// welcher Ausgang welchen Statuscode bedeutet.
/// </summary>
public static class PairingEndpoints
{
    /// <param name="port">
    /// Der Port, auf dem dieser Agent lauscht. Er steht im QR-Code, damit die
    /// Gegenseite ihn nicht raten muss — bei einem abweichenden Port liefe sie
    /// sonst still in die Vorgabe 8443.
    /// </param>
    public static void MapPairingEndpoints(this WebApplication app, int port)
    {
        // Nur vom Rechner selbst erreichbar (siehe ClientAuthMiddleware). Ab
        // Phase 11 drückt darauf ein Knopf im Fenster; bis dahin ist es der Weg,
        // überhaupt einen Code zu bekommen.
        app.MapPost("/api/pair/code", (PairingCodes codes, ILogger<Program> logger) =>
        {
            var code = codes.Issue();

            // Der Code steht bewusst auch im Log: ohne Fenster ist das die
            // einzige Stelle, an der ihn jemand ablesen kann, der am Rechner
            // sitzt. Ein Geheimnis ist er nur für fünf Minuten.
            logger.LogInformation("Kopplungscode {Code} erzeugt, gültig 5 Minuten.", code);

            return Results.Ok(new
            {
                code,
                expiresInSeconds = (int)PairingCodes.Lifetime.TotalSeconds,

                // Derselbe Code, nur als Adresse — daraus macht das Fenster den
                // QR-Code. Er wird hier erzeugt und nicht dort, weil das Format
                // damit an einer Stelle steht, die Tests hat.
                pairingUri = PairingUri.Build(Environment.MachineName, port, code)
            });
        });

        app.MapPost("/api/pair", (
            PairRequest request, PairingService pairing, AgentIdentity identity) =>
        {
            var result = pairing.Pair(
                request.Code ?? string.Empty,
                request.Label ?? string.Empty,
                request.PublicKey ?? string.Empty,
                request.Scopes);

            if (result.Outcome != PairOutcome.Ok || result.Client is null)
            {
                return Results.BadRequest(new { error = Describe(result.Outcome) });
            }

            return Results.Ok(new
            {
                clientId = result.Client.Id,
                scopes = result.Client.Scopes,
                hostname = Environment.MachineName,
                agentPublicKey = identity.PublicKey,
                agentFingerprint = identity.Fingerprint
            });
        });

        app.MapPost("/api/session/challenge", (ChallengeRequest request, PairingService pairing) =>
        {
            var nonce = pairing.Challenge(request.ClientId ?? string.Empty);

            // Auch ein unbekannter Client bekommt 401 und nicht 404: dass eine
            // Kennung existiert, ist selbst schon eine Auskunft.
            return nonce is null
                ? Results.Json(new { error = "Nicht gekoppelt." }, statusCode: 401)
                : Results.Ok(new
                {
                    nonce,
                    expiresInSeconds = (int)ChallengeStore.Lifetime.TotalSeconds
                });
        });

        app.MapPost("/api/session", (SessionRequest request, PairingService pairing) =>
        {
            var result = pairing.OpenSession(
                request.ClientId ?? string.Empty,
                request.Nonce ?? string.Empty,
                request.Signature ?? string.Empty);

            if (result.Outcome != SessionOutcome.Ok || result.Token is null || result.Client is null)
            {
                // Alle Fehlschläge sehen gleich aus. Wer probiert, soll nicht
                // erfahren, ob die Kennung stimmte und nur die Unterschrift
                // nicht passte.
                return Results.Json(new { error = "Anmeldung fehlgeschlagen." }, statusCode: 401);
            }

            return Results.Ok(new
            {
                token = result.Token,
                scopes = result.Client.Scopes,
                expiresInSeconds = (int)SessionStore.Lifetime.TotalSeconds
            });
        });

        // Verwaltung — ebenfalls nur am Rechner selbst.
        app.MapGet("/api/clients", (PairingService pairing) => Results.Ok(new
        {
            clients = pairing.ListClients().Select(client => new
            {
                id = client.Id,
                label = client.Label,
                scopes = client.Scopes,
                createdAt = client.CreatedAt,
                lastSeenAt = client.LastSeenAt
            })
        }));

        app.MapDelete("/api/clients/{id}", (string id, PairingService pairing) =>
            pairing.Revoke(id)
                ? Results.Ok(new { revoked = id })
                : Results.NotFound(new { error = "Unbekannter Client." }));
    }

    private static string Describe(PairOutcome outcome) => outcome switch
    {
        PairOutcome.BadCode => "Code falsch oder abgelaufen.",
        PairOutcome.BadLabel => "Der Name des Geräts fehlt oder ist zu lang.",
        PairOutcome.BadPublicKey => "Der öffentliche Schlüssel ist kein ECDSA-P-256-Schlüssel.",
        PairOutcome.BadScope => "Unbekanntes Recht angefordert.",
        _ => "Kopplung fehlgeschlagen."
    };
}

internal sealed record PairRequest(string? Code, string? Label, string? PublicKey, string[]? Scopes);

internal sealed record ChallengeRequest(string? ClientId);

internal sealed record SessionRequest(string? ClientId, string? Nonce, string? Signature);
