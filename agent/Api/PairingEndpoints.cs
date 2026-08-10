using RemoteDesktopAgent.Auth;
using RemoteDesktopAgent.Capture.H264;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Kopplung, Anmeldung und Widerruf.
///
/// Die Regeln stehen in <see cref="PairingService"/> — hier wird nur übersetzt,
/// welcher Ausgang welchen Statuscode bedeutet.
/// </summary>
public static class PairingEndpoints
{
    /// <param name="hostName">
    /// Der volle MagicDNS-Name dieses Rechners, gelesen aus seinem Zertifikat
    /// (<see cref="Services.CertificateLoader.DnsName"/>). <c>null</c>, wenn das
    /// Zertifikat keinen führt — dann gibt es keinen QR-Code, und gekoppelt wird
    /// über den abgetippten Code.
    /// </param>
    /// <param name="port">
    /// Der Port, auf dem dieser Agent lauscht. Er steht im QR-Code, damit die
    /// Gegenseite ihn nicht raten muss — bei einem abweichenden Port liefe sie
    /// sonst still in die Vorgabe 8443.
    /// </param>
    /// <param name="caFingerprint">
    /// Der Fingerabdruck der eigenen CA — <c>null</c>, wenn das Zertifikat von
    /// Tailscale kommt und es also nichts zu bestätigen gibt. Er geht bei der
    /// Kopplung mit, weil das der eine Weg ist, auf dem ein Angreifer im Netz
    /// nicht mitliest: der Code steht auf dem Bildschirm des Rechners.
    /// </param>
    public static void MapPairingEndpoints(
        this WebApplication app, string? hostName, int port, string? caFingerprint = null)
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
                //
                // Der Name kommt aus dem Zertifikat und nicht aus
                // Environment.MachineName: Letzterer ist der Windows-Name und
                // hat mit dem Tailnet-Namen nichts zu tun. Ein QR mit dem
                // falschen Namen sieht gültig aus und führt ins Leere.
                pairingUri = hostName is null
                    ? null
                    : PairingUri.Build(hostName, port, code, caFingerprint)
            });
        });

        // Ebenfalls nur vom Rechner selbst: das Angebot der Gegenseite abholen.
        // Einlösen kann es nur, wer den privaten Geräteschlüssel hält — auf
        // Windows das Fenster, am Handy die App. Der Agent hebt es bloß auf.
        app.MapGet("/api/pair/pending", (PendingPairings pending) =>
        {
            var offer = pending.Take();

            return offer is null
                ? Results.Ok(new { pending = (object?)null })
                : Results.Ok(new
                {
                    pending = new
                    {
                        host = offer.Host,
                        port = offer.Port,
                        code = offer.Code,
                        caFingerprint = offer.CaFingerprint,
                        name = offer.Name
                    }
                });
        });

        app.MapPost("/api/pair", (
            PairRequest request,
            PairingService pairing,
            AgentIdentity identity,
            PendingPairings pending,
            ILogger<Program> logger) =>
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

            // Die Gegenkopplung: die andere Seite legt ihre eigene Adresse und
            // einen frischen Code ihres Agents bei. Aufgehoben wird das erst
            // **nach** bestandener Kopplung — vorher wäre es ein Weg, jedem
            // Rechner ein Angebot unterzuschieben, indem man Codes rät.
            var back = request.Back is null
                ? null
                : PendingPairings.Sanitize(
                    request.Back.Host,
                    request.Back.Port,
                    request.Back.Code,
                    request.Back.CaFingerprint,
                    request.Back.Name);

            if (back is not null)
            {
                pending.Offer(back);

                logger.LogInformation(
                    "Gegenkopplung angeboten von {Host}:{Port}.", back.Host, back.Port);
            }

            return Results.Ok(new
            {
                clientId = result.Client.Id,
                scopes = result.Client.Scopes,
                hostname = Environment.MachineName,
                agentPublicKey = identity.PublicKey,
                agentFingerprint = identity.Fingerprint,

                // Womit sich der Rechner beim Verbinden ausweist. Ohne diesen
                // Wert kann ein Client ein selbst ausgestelltes Zertifikat nicht
                // von einem untergeschobenen unterscheiden.
                caFingerprint
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

        // Widerrufen heißt: ab jetzt **und** rückwirkend auf alles, was schon
        // steht.
        //
        // Der Eintrag allein zu löschen genügte nicht. Am echten Gerät bekam ein
        // entferntes Handy sofort die Meldung, es sei entfernt worden — und
        // steuerte trotzdem weiter, bis jemand die App schloss. Bild und Eingabe
        // laufen über WebSockets, das Video zusätzlich über WebRTC, und keine
        // dieser Verbindungen wird nach dem Aufbau noch einmal geprüft. Also
        // werden sie hier abgeschnitten.
        app.MapDelete("/api/clients/{id}", async (
            string id,
            PairingService pairing,
            LiveConnections live,
            WebRtcRegistry streams,
            ILogger<Program> logger) =>
        {
            if (!pairing.Revoke(id))
            {
                return Results.NotFound(new { error = "Unbekannter Client." });
            }

            var closed = live.Close(id) + await streams.CloseOwnedAsync(id);

            logger.LogInformation(
                "Client {Id} widerrufen, {Closed} laufende Verbindungen getrennt.", id, closed);

            return Results.Ok(new { revoked = id, closed });
        });
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

internal sealed record PairRequest(
    string? Code, string? Label, string? PublicKey, string[]? Scopes, BackRequest? Back);

/// <summary>Was die Gegenseite braucht, damit man sich bei ihr melden kann.</summary>
internal sealed record BackRequest(
    string? Host, int? Port, string? Code, string? CaFingerprint, string? Name);

internal sealed record ChallengeRequest(string? ClientId);

internal sealed record SessionRequest(string? ClientId, string? Nonce, string? Signature);
