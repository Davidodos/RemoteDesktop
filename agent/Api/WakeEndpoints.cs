using RemoteDesktopAgent.Services;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Wecken als Netz-Fähigkeit: wer wach ist, weckt den Nachbarn.
///
/// Die MAC steht in der Anfrage und nicht in einer Konfiguration auf diesem
/// Rechner. Das ist der Kern des Entwurfs (<c>docs/PLAN-V2.md</c>, Abschnitt 3):
/// der Client merkt sich MAC und Standort-Kennung seiner Geräte, solange sie
/// wach sind, und sucht sich beim Wecken selbst einen Knoten im selben Netz.
/// Ein neuer Rechner heißt damit nirgends eine gepflegte Liste.
/// </summary>
public static class WakeEndpoints
{
    public static void MapWakeEndpoints(this WebApplication app)
    {
        app.MapPost("/api/wol", async (
            WakeRequest request, WakeService wake, CancellationToken cancellationToken) =>
        {
            var outcome = await wake.WakeAsync(request.Mac, cancellationToken);

            return outcome switch
            {
                WakeOutcome.Sent => Results.Ok(new { status = "sent", mac = request.Mac }),

                WakeOutcome.BadMac => Results.BadRequest(new
                {
                    error = "Keine gültige MAC-Adresse."
                }),

                WakeOutcome.TooMany => Results.Json(
                    new { error = "Zu viele Weckversuche. Bitte kurz warten." },
                    statusCode: StatusCodes.Status429TooManyRequests),

                _ => Results.Problem("Magic Packet konnte nicht gesendet werden.")
            };
        });
    }
}

/// <summary>Wen wecken. Die Adresse ist alles, was der Knoten braucht.</summary>
internal sealed record WakeRequest(string? Mac);
