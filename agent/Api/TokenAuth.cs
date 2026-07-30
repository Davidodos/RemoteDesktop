using System.Security.Cryptography;
using System.Text;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Prüft das Pre-Shared-Token. Tailscale ist die erste Schicht, das hier die
/// zweite: ein fremdes Gerät im Tailnet soll nicht automatisch den PC steuern
/// können.
/// </summary>
public sealed class TokenAuth
{
    /// <summary>
    /// Browser können bei WebSocket-Verbindungen keine eigenen Header setzen.
    /// Deshalb ist das Token dort im Query-String erlaubt — die Verbindung ist
    /// TLS-verschlüsselt, und der Agent loggt keine Query-Strings.
    /// </summary>
    public const string QueryParameter = "token";

    private readonly byte[] _expected;

    public TokenAuth(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
        {
            throw new ArgumentException(
                "Agent-Token fehlt oder ist kürzer als 32 Zeichen. " +
                "Der Agent hat volle Kontrolle über den PC — ein schwaches Token ist keine Option.",
                nameof(token));
        }

        _expected = Encoding.UTF8.GetBytes(token);
    }

    public bool IsValid(HttpContext context)
    {
        var presented = ExtractToken(context);

        if (presented is null)
        {
            return false;
        }

        var candidate = Encoding.UTF8.GetBytes(presented);

        // Fixed-time-Vergleich: ein früher Abbruch würde die Token-Länge und
        // stellenweise den Inhalt verraten.
        return CryptographicOperations.FixedTimeEquals(candidate, _expected);
    }

    private static string? ExtractToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        if (context.Request.Query.TryGetValue(QueryParameter, out var fromQuery))
        {
            return fromQuery.ToString();
        }

        return null;
    }
}

public static class TokenAuthExtensions
{
    /// <summary>
    /// Blockt alles außer <c>/health</c>. Absichtlich als Sperre für den
    /// gesamten Baum statt pro Endpoint — ein vergessenes Attribut an einem
    /// neuen Endpoint wäre sonst ein offenes Tor.
    /// </summary>
    public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            // CORS-Preflights schickt der Browser grundsätzlich ohne
            // Authorization-Header. Würden wir sie mit 401 abweisen, käme der
            // eigentliche Aufruf nie zustande.
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                await next();
                return;
            }

            var auth = context.RequestServices.GetRequiredService<TokenAuth>();

            if (!auth.IsValid(context))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("TokenAuth");

                logger.LogWarning(
                    "Abgelehnt: {Method} {Path} von {Remote}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Connection.RemoteIpAddress);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Ungültiges Token." });
                return;
            }

            await next();
        });
    }
}
