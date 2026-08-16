using RemoteDesktopAgent.Auth;
using RemoteDesktopAgent.Capture.H264;
using RemoteDesktopSetup;

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

        // ---- Die drei Wege der eigenen Oberfläche --------------------------
        //
        // Alle drei nur vom Rechner selbst (siehe ClientAuthMiddleware). Sie
        // sind der Ersatz für das, was bis Phase 31e über das Netz lief: der
        // Steckbrief geht bei der Kopplung mit, und was danach zu tun ist,
        // erledigt jede Seite bei sich zu Hause.

        // Der eigene Steckbrief — er geht mit, wenn dieses Fenster ein anderes
        // Gerät koppelt. `null`, solange keine Adresse feststeht: ein Steckbrief
        // ohne Adresse beschreibt nichts.
        app.MapGet("/api/pair/self", (AgentIdentity identity, LocalClient local) =>
            Results.Ok(new
            {
                profile = hostName is null
                    ? null
                    : new
                    {
                        host = hostName,
                        port,
                        name = Environment.MachineName,
                        caFingerprint,
                        agentFingerprint = identity.Fingerprint,
                        clientKey = local.PublicKey,
                        platform = DevicePlatform.Windows
                    }
            }));

        // Die Steckbriefe abholen, die beim Koppeln hier abgegeben wurden.
        //
        // Lesen leert den Eingang **nicht**. Es tat es einmal, und das war
        // falsch: ging danach irgendetwas schief — kein Vertrauen, kein
        // Speicher, ein Fenster, das gerade schließt —, war der Steckbrief
        // endgültig weg, und am Bildschirm stand „noch kein Gerät gekoppelt"
        // ohne zweiten Versuch. Vergessen wird auf Zuruf, siehe unten.
        app.MapGet("/api/pair/peers", (PeerInbox inbox) => Results.Ok(new
        {
            peers = inbox.List().Select(peer => new
            {
                id = PeerInbox.Key(peer),
                host = peer.Host,
                port = peer.Port,
                name = peer.Name,
                caFingerprint = peer.CaFingerprint,
                agentFingerprint = peer.AgentFingerprint,
                platform = peer.Platform
            })
        }));

        // Was in der Liste steht, braucht hier nicht mehr zu liegen. Erst
        // jetzt — sonst käme ein Gerät, das jemand entfernt hat, von allein
        // zurück.
        app.MapPost("/api/pair/peers/forget", (ForgetRequest request, PeerInbox inbox) =>
        {
            inbox.Forget(request.Ids ?? []);

            return Results.Ok(new { forgotten = request.Ids?.Length ?? 0 });
        });

        // Die Gegenrichtung eintragen: die Oberfläche der Gegenseite darf
        // diesen Rechner steuern. Ohne Code — siehe PairingService.Grant.
        app.MapPost("/api/pair/grant", (
            GrantRequest request, PairingService pairing, ILogger<Program> logger) =>
        {
            if (!pairing.Grant(request.PublicKey ?? string.Empty, request.Label ?? string.Empty))
            {
                return Results.BadRequest(
                    new { error = "Der öffentliche Schlüssel ist kein ECDSA-P-256-Schlüssel." });
            }

            logger.LogInformation("Gegenrichtung eingetragen für {Label}.", request.Label);

            return Results.Ok(new { granted = true });
        });

        app.MapPost("/api/pair", (
            PairRequest request,
            PairingService pairing,
            AgentIdentity identity,
            PeerInbox inbox,
            LocalClient local,
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

            // Der Steckbrief des Anrufers: Adresse, Port, Name, Fingerabdrücke
            // und der Schlüssel seiner Oberfläche. Angenommen wird er erst
            // **nach** bestandener Kopplung — vorher wäre es ein Weg, jedem
            // Rechner ein Gerät in die Liste zu schreiben, indem man Codes rät.
            var peer = request.Self is null
                ? null
                : DeviceProfile.Sanitize(
                    request.Self.Host,
                    request.Self.Port,
                    request.Self.Name,
                    request.Self.CaFingerprint,
                    request.Self.AgentFingerprint,
                    request.Self.ClientKey,
                    request.Self.Platform);

            if (peer is not null)
            {
                // Nur der Steckbrief wandert in den Eingang. Den Schlüssel der
                // Gegenseite hat dieser Agent schon: es ist derselbe, mit dem
                // sie sich gerade gekoppelt hat. Ihn ein zweites Mal aus dem
                // Steckbrief zu nehmen hieße, zwei Quellen für dieselbe Angabe
                // zu führen — und eine davon kommt ungeprüft aus dem Rumpf.
                inbox.Add(peer);

                logger.LogInformation(
                    "Kopplung in beide Richtungen mit {Name} ({Host}:{Port}).",
                    peer.Name, peer.Host, peer.Port);
            }

            return Results.Ok(new
            {
                clientId = result.Client.Id,
                scopes = result.Client.Scopes,
                hostname = Environment.MachineName,
                agentPublicKey = identity.PublicKey,
                agentFingerprint = identity.Fingerprint,

                // Was dieses Gerät ist — für das Symbol in der Geräteliste der
                // Gegenseite. Sie bekommt es hier und nicht erst aus
                // /api/info, weil sie sonst nichts anzeigen könnte, solange
                // dieser Rechner aus ist.
                platform = DevicePlatform.Windows,

                // Womit sich der Rechner beim Verbinden ausweist. Ohne diesen
                // Wert kann ein Client ein selbst ausgestelltes Zertifikat nicht
                // von einem untergeschobenen unterscheiden.
                caFingerprint,

                // Dasselbe zurück: der Schlüssel der Oberfläche dieses Rechners.
                // Damit trägt die Gegenseite die andere Richtung bei sich ein,
                // ohne noch einmal ins Netz zu gehen.
                peer = new
                {
                    name = Environment.MachineName,
                    clientKey = local.PublicKey
                }
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
    string? Code, string? Label, string? PublicKey, string[]? Scopes, ProfileRequest? Self);

/// <summary>
/// Der Steckbrief des Anrufers — alles, was dieser Rechner braucht, um ihn
/// später von sich aus zu erreichen. Siehe <see cref="DeviceProfile"/>.
/// </summary>
internal sealed record ProfileRequest(
    string? Host,
    int? Port,
    string? Name,
    string? CaFingerprint,
    string? AgentFingerprint,
    string? ClientKey,
    string? Platform);

internal sealed record GrantRequest(string? PublicKey, string? Label);

internal sealed record ForgetRequest(string[]? Ids);

internal sealed record ChallengeRequest(string? ClientId);

internal sealed record SessionRequest(string? ClientId, string? Nonce, string? Signature);
