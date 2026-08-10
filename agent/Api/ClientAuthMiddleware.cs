using System.Net;
using RemoteDesktopAgent.Auth;

namespace RemoteDesktopAgent.Api;

public static class ClientAuthMiddleware
{
    /// <summary>
    /// Unter diesem Schlüssel steht das Gerät, dem die Anfrage gehört. Es wird
    /// hier hinterlegt und nicht später noch einmal ermittelt: die Prüfung ist
    /// bereits gelaufen, und ein zweiter Weg zu derselben Auskunft wäre ein
    /// zweiter Weg, sie falsch zu beantworten.
    /// </summary>
    private const string ClientIdItem = "RemoteDesktop.ClientId";

    /// <summary>
    /// Wem diese Anfrage gehört — <c>null</c> beim alten Sammel-Token, das kein
    /// einzelnes Gerät kennt.
    /// </summary>
    public static string? ClientId(HttpContext context) =>
        context.Items.TryGetValue(ClientIdItem, out var value) ? value as string : null;

    /// <summary>
    /// Endpunkte, die von außen gar nicht erreichbar sein dürfen: den
    /// Kopplungscode anzeigen und Clients widerrufen. Beides setzt voraus, dass
    /// jemand am Rechner sitzt — wer das kann, könnte den Agent ohnehin
    /// beenden. Über das Netz wäre es dagegen genau der Weg, den die Kopplung
    /// verhindern soll.
    /// </summary>
    /// <remarks>
    /// <c>/api/pair/pending</c> steht hier und nicht bloß unter den freien
    /// Pfaden: es liegt unterhalb von <c>/api/pair</c>, und das ist absichtlich
    /// ohne Ausweis erreichbar — der Kopplungsaufruf erzeugt die Berechtigung ja
    /// erst. Ohne diesen Eintrag käme jeder im Netz an ein Angebot heran, in dem
    /// ein gültiger Kopplungscode der Gegenseite steht.
    /// </remarks>
    private static readonly string[] LocalOnly =
        ["/api/pair/code", "/api/pair/pending", "/api/clients"];

    /// <summary>
    /// Endpunkte, die ohne Berechtigung auskommen, weil sie selbst die
    /// Berechtigung erzeugen. Sie sind einzeln aufgezählt und nicht per Präfix
    /// freigegeben — ein neuer Endpoint unter <c>/api/pair/…</c> soll nicht
    /// versehentlich mit offenstehen.
    /// </summary>
    private static readonly string[] WithoutCredential =
        ["/health", "/api/pair", "/api/session/challenge", "/api/session"];

    /// <summary>
    /// Blockt alles, was nicht ausdrücklich freigegeben ist. Absichtlich als
    /// Sperre für den gesamten Baum statt pro Endpoint — ein vergessenes
    /// Attribut an einem neuen Endpoint wäre sonst ein offenes Tor.
    /// </summary>
    public static IApplicationBuilder UseClientAuth(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            // CORS-Preflights schickt der Browser grundsätzlich ohne
            // Authorization-Header. Würden wir sie mit 401 abweisen, käme der
            // eigentliche Aufruf nie zustande.
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            if (Matches(path, LocalOnly))
            {
                if (IsLocal(context.Connection.RemoteIpAddress))
                {
                    await next();
                    return;
                }

                Deny(context, StatusCodes.Status403Forbidden, "Nur am Rechner selbst.");
                await context.Response.WriteAsJsonAsync(
                    new { error = "Dieser Aufruf ist nur am Rechner selbst möglich." });
                return;
            }

            if (Matches(path, WithoutCredential))
            {
                await next();
                return;
            }

            var auth = context.RequestServices.GetRequiredService<ClientAuth>();
            var result = auth.Authorize(ExtractCredential(context), path);

            if (result.IsAllowed)
            {
                context.Items[ClientIdItem] = result.ClientId;

                await next();
                return;
            }

            var (status, message) = Describe(result);

            Deny(context, status, message);
            await context.Response.WriteAsJsonAsync(new { error = message });
        });
    }

    private static (int Status, string Message) Describe(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.MissingScope => (
            StatusCodes.Status403Forbidden,
            $"Dieses Gerät hat kein Recht auf '{result.RequiredScope}'."),

        // Ein unbekannter Pfad kommt fast immer von einer neueren App, die einen
        // Endpoint erwartet, den dieser Agent noch nicht hat. Deshalb 403 mit
        // einem Satz, der das sagt — 404 würde nach einem Tippfehler aussehen.
        AuthOutcome.UnknownPath => (
            StatusCodes.Status403Forbidden,
            "Unbekannter Endpoint — vermutlich ist der Agent älter als die App."),

        _ => (StatusCodes.Status401Unauthorized, "Nicht angemeldet.")
    };

    private static void Deny(HttpContext context, int status, string message)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ClientAuth")
            .LogWarning(
                "Abgelehnt ({Reason}): {Method} {Path} von {Remote}",
                message,
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress);

        context.Response.StatusCode = status;
    }

    private static string? ExtractCredential(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        if (context.Request.Query.TryGetValue(ClientAuth.QueryParameter, out var fromQuery))
        {
            return fromQuery.ToString();
        }

        return null;
    }

    /// <summary>
    /// Kestrel meldet eine Verbindung von 127.0.0.1 als IPv4-in-IPv6
    /// (<c>::ffff:127.0.0.1</c>), sobald er auf einem Dual-Stack-Socket lauscht.
    /// Ohne die Rückabbildung hielte der Agent die eigene Maschine für fremd und
    /// verweigerte den Kopplungscode.
    /// </summary>
    private static bool IsLocal(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        return IPAddress.IsLoopback(
            address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    private static bool Matches(string path, IEnumerable<string> known) =>
        known.Any(entry => path.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                           path.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase));
}
