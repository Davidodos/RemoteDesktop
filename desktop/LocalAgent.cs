using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RemoteDesktopClient;

/// <summary>Ein gekoppeltes Gerät, wie der Agent es meldet.</summary>
public sealed record PairedClientInfo(
    string Id,
    string Label,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

/// <summary>Ein frisch erzeugter Kopplungscode.</summary>
public sealed record PairingCodeInfo(string Code, int ExpiresInSeconds);

/// <summary>
/// Spricht den Agent auf demselben Rechner an.
///
/// Kopplungscode anzeigen und Clients widerrufen sind am Agent absichtlich nur
/// über die Loopback-Adresse erreichbar (siehe <c>agent/Api/ClientAuthMiddleware.cs</c>).
/// Genau deshalb kann dieses Fenster sie bedienen und niemand sonst — und
/// deshalb braucht es dafür auch keinen Ausweis.
/// </summary>
public sealed class LocalAgent : IDisposable
{
    private readonly HttpClient _http;

    public LocalAgent(int port)
    {
        var handler = new HttpClientHandler
        {
            // Das Zertifikat des Agents ist auf seinen Tailnet-Namen ausgestellt,
            // nicht auf "localhost" — die Prüfung müsste hier also scheitern.
            // Vertretbar ist das nur, weil die Verbindung den Rechner nicht
            // verlässt; für alles andere bleibt die Prüfung scharf.
            ServerCertificateCustomValidationCallback = (message, _, _, _) =>
                message.RequestUri?.IsLoopback == true
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://localhost:{port}"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>Der Port, auf dem der Agent lauscht — überschreibbar für Testaufbauten.</summary>
    public static int ConfiguredPort()
    {
        var raw = Environment.GetEnvironmentVariable("REMOTEDESKTOP_AGENT_PORT");

        return int.TryParse(raw, out var port) && port is > 0 and <= 65535 ? port : 8443;
    }

    public async Task<PairingCodeInfo> IssueCodeAsync(CancellationToken cancellationToken)
    {
        var response = await _http.PostAsync("/api/pair/code", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PairingCodeInfo>(cancellationToken)
               ?? throw new InvalidOperationException("Der Agent hat keinen Code geliefert.");
    }

    public async Task<IReadOnlyList<PairedClientInfo>> ListClientsAsync(
        CancellationToken cancellationToken)
    {
        var answer = await _http.GetFromJsonAsync<ClientList>("/api/clients", cancellationToken);

        return answer?.Clients ?? [];
    }

    public async Task RevokeAsync(string id, CancellationToken cancellationToken)
    {
        var response = await _http.DeleteAsync($"/api/clients/{Uri.EscapeDataString(id)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();

    private sealed record ClientList([property: JsonPropertyName("clients")] PairedClientInfo[] Clients);
}
