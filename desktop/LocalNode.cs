using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
///
/// <para>
/// **Vier Wege statt zweier.** Bis Phase 31e reichte das Fenster einen
/// Kopplungscode weiter, den die Gegenseite binnen fünf Minuten einlösen
/// musste. Jetzt geht der Steckbrief bei der Kopplung mit, und was danach zu
/// tun ist, erledigt jede Seite bei sich zu Hause: den Schlüssel der anderen in
/// die eigene <c>clients.json</c>, ihren Steckbrief in die eigene Geräteliste.
/// </para>
/// </summary>
public static class LocalNode
{
    /// <summary>Der Agent lauscht immer hier; andere Ports sieht die Einrichtung nicht vor.</summary>
    private const int AgentPort = 8443;

    private static readonly HttpClient Client = Build();

    /// <summary>
    /// Der eigene Steckbrief: Adresse, Port, Name und die beiden Fingerabdrücke.
    ///
    /// Er hängt ausdrücklich **nicht** daran, ob dieser Rechner gerade steuerbar
    /// sein will — er beschreibt, wie er erreichbar wäre.
    /// </summary>
    /// <returns><c>null</c>, wenn kein Agent läuft. Dann bleibt es bei einer Richtung.</returns>
    public static Task<JsonElement?> SelfAsync(CancellationToken cancellationToken = default) =>
        ReadAsync("/api/pair/self", "profile", cancellationToken);

    /// <summary>
    /// Die Steckbriefe, die beim Koppeln hier abgegeben wurden. Einmalig: beim
    /// Abholen ist der Eingang leer.
    /// </summary>
    public static Task<JsonElement?> PeersAsync(CancellationToken cancellationToken = default) =>
        ReadAsync("/api/pair/peers", "peers", cancellationToken);

    /// <summary>
    /// Trägt die Oberfläche der Gegenseite in die <c>clients.json</c> dieses
    /// Rechners ein — die Gegenrichtung, ohne einen zweiten Aufruf über das Netz.
    /// </summary>
    public static Task GrantAsync(
        string publicKey, string? label, CancellationToken cancellationToken = default) =>
        PostAsync(
            "/api/pair/grant",
            new { publicKey, label = label ?? string.Empty },
            cancellationToken);

    /// <summary>
    /// Hinterlegt den Ausweis dieses Fensters beim eigenen Agent, damit er beim
    /// Koppeln mitgehen kann. Ohne ihn bliebe jede Kopplung einseitig.
    /// </summary>
    public static Task RegisterAsync(
        string publicKey, CancellationToken cancellationToken = default) =>
        PostAsync("/api/pair/local", new { publicKey }, cancellationToken);

    private static async Task<JsonElement?> ReadAsync(
        string path, string field, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(
                $"https://127.0.0.1:{AgentPort}{path}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            return body.TryGetProperty(field, out var value) ? value : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or JsonException)
        {
            // Kein Agent, keine Gegenrichtung. Das ist der Normalfall auf einem
            // Rechner, der nur steuern und nicht gesteuert werden soll.
            return null;
        }
    }

    /// <summary>
    /// Hier wird ein Fehlschlag **nicht** verschluckt: die beiden schreibenden
    /// Wege sind die Gegenrichtung selbst. Bleibt einer still liegen, sieht das
    /// später aus wie eine Kopplung, die nie angeboten wurde — und danach sucht
    /// niemand mehr.
    /// </summary>
    private static async Task PostAsync(
        string path, object payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync(
            $"https://127.0.0.1:{AgentPort}{path}", content, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static HttpClient Build()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }
}
