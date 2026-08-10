using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace RemoteDesktopClient;

/// <summary>
/// Dieser Rechner als Gegenstelle — gefragt wird der Agent nebenan.
///
/// <para>
/// **Warum das Fenster fragt und nicht die Seite:** der Agent weist sich mit
/// einem selbst ausgestellten Zertifikat aus. Die Seite müsste ihm erst
/// vertrauen, um ihn überhaupt fragen zu können, wem man vertrauen soll — ein
/// Kreis, der sich nicht schließt. Hier gibt es ihn nicht: das Fenster läuft
/// auf demselben Rechner wie der Agent.
/// </para>
///
/// <para>
/// Deshalb wird das Zertifikat auf <c>127.0.0.1</c> auch nicht geprüft. Wer
/// dort lauscht, hat den Port 8443 dieses Rechners belegt — dann liefe der
/// eigene Agent gar nicht, und die Fernsteuerung wäre das kleinste Problem.
/// </para>
/// </summary>
public static class LocalNode
{
    /// <summary>Der Agent lauscht immer hier; andere Ports sieht die Einrichtung nicht vor.</summary>
    private const int AgentPort = 8443;

    private static readonly HttpClient Client = Build();

    /// <summary>
    /// Was die Gegenseite braucht, um sich hier zu melden: Adresse, Port, ein
    /// frischer Code und der eigene Fingerabdruck.
    ///
    /// Alles davon steht bereits in der Adresse, die der Agent für den QR-Code
    /// baut — sie wird hier nur wieder auseinandergenommen. Ein zweiter Weg zu
    /// denselben Angaben wäre ein zweiter, der veralten kann.
    /// </summary>
    /// <returns><c>null</c>, wenn kein Agent läuft. Dann bleibt es bei einer Richtung.</returns>
    public static async Task<object?> OfferAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.PostAsync(
                $"https://127.0.0.1:{AgentPort}/api/pair/code", null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<CodeResponse>(cancellationToken);

            if (body?.PairingUri is null || body.Code is null)
            {
                return null;
            }

            var uri = new Uri(body.PairingUri);
            var query = HttpUtility.ParseQueryString(uri.Query);

            var host = query["host"];

            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            return new
            {
                host,
                port = int.TryParse(query["port"], out var parsed) ? parsed : AgentPort,
                code = body.Code,
                caFingerprint = query["ca"],
                name = Environment.MachineName
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or UriFormatException or JsonException)
        {
            // Kein Agent, kein Angebot. Das ist der Normalfall auf einem
            // Rechner, der nur steuern und nicht gesteuert werden soll.
            return null;
        }
    }

    /// <summary>Das Angebot, das die Gegenseite hinterlassen hat. Einmalig.</summary>
    public static async Task<object?> PendingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(
                $"https://127.0.0.1:{AgentPort}/api/pair/pending", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<PendingResponse>(cancellationToken);

            return body?.Pending;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or JsonException)
        {
            return null;
        }
    }

    private static HttpClient Build()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }

    private sealed record CodeResponse(string? Code, string? PairingUri);

    private sealed record PendingResponse(JsonElement? Pending);
}
