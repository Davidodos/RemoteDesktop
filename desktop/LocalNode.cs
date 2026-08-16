using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RemoteDesktopClient;

/// <summary>
/// Dieser Rechner als Gegenstelle — gefragt wird der Agent nebenan, und wenn
/// der nicht läuft, sein Datenordner.
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
/// **Zwei Wege zu denselben Daten.** Läuft der Agent, geht alles über ihn: er
/// hält <c>clients.json</c> im Speicher, und eine Datei unter ihm zu ändern
/// ginge beim nächsten Schreiben verloren. Läuft er nicht, liest und schreibt
/// das Fenster die Dateien selbst — beim nächsten Start liest der Agent sie
/// ohnehin neu. Das ist der ganze Unterschied zwischen „koppeln geht nur mit
/// laufendem Agent" und „eingerichtet genügt": wer diesen Rechner nur zum
/// Steuern benutzt, soll den Agent nicht dafür starten müssen.
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
    private static readonly HttpClient Client = Build();

    /// <summary>
    /// Der eigene Steckbrief: Adresse, Port, Name und die beiden Fingerabdrücke.
    ///
    /// Er hängt ausdrücklich **nicht** daran, ob dieser Rechner gerade steuerbar
    /// sein will — er beschreibt, wie er erreichbar wäre. Deshalb kommt er bei
    /// gestopptem Agent aus dem Datenordner statt gar nicht.
    /// </summary>
    /// <returns>
    /// <c>null</c>, wenn keine Adresse eingetragen ist. Dann ist dieser Rechner
    /// kein mögliches Ziel, und es bleibt bei einer Richtung.
    /// </returns>
    public static async Task<object?> SelfAsync(CancellationToken cancellationToken = default)
    {
        var running = await ReadAsync("/api/pair/self", "profile", cancellationToken);

        // Ein Agent, der antwortet, hat recht: er kennt sein eigenes Zertifikat
        // und nicht nur die Dateien, aus denen es entstanden ist. Antwortet er
        // mit `null`, ist das ebenfalls seine Auskunft — dann fehlt die Adresse,
        // und der Datenordner wüsste es auch nicht besser.
        if (running is { } answered)
        {
            return answered.ValueKind == JsonValueKind.Null ? null : answered;
        }

        return AgentData.Profile();
    }

    /// <summary>
    /// Die Steckbriefe, die beim Koppeln hier abgegeben wurden.
    ///
    /// Nur mit laufendem Agent, und das ist keine Einschränkung: dort landet
    /// überhaupt nur etwas, wenn ein anderes Gerät diesen Rechner gekoppelt
    /// hat — und dafür musste er antworten.
    /// </summary>
    public static async Task<object?> PeersAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync("/api/pair/peers", "peers", cancellationToken);

    /// <summary>
    /// Vergisst die Steckbriefe, die in der Liste stehen. Erst nach dem
    /// Eintragen — vorher wäre ein Fehlschlag endgültig.
    /// </summary>
    public static Task ForgetAsync(
        string[] ids, CancellationToken cancellationToken = default) =>
        PostAsync("/api/pair/peers/forget", new { ids }, cancellationToken);

    /// <summary>
    /// Trägt die Oberfläche der Gegenseite in die <c>clients.json</c> dieses
    /// Rechners ein — die Gegenrichtung, ohne einen zweiten Aufruf über das Netz.
    ///
    /// Bei gestopptem Agent schreibt das Fenster die Datei selbst. Ein Eintrag
    /// wirkt, sobald der Agent startet; er hat keine Frist.
    /// </summary>
    public static async Task GrantAsync(
        string publicKey, string? label, CancellationToken cancellationToken = default)
    {
        if (await PostAsync(
                "/api/pair/grant",
                new { publicKey, label = label ?? string.Empty },
                cancellationToken))
        {
            return;
        }

        AgentData.Grant(publicKey, label ?? string.Empty);
    }

    /// <summary>
    /// Hebt die Gegenrichtung wieder auf: dieses Gerät darf diesen Rechner
    /// nicht mehr steuern.
    ///
    /// Bei laufendem Agent über ihn — nur so werden auch die Verbindungen
    /// getrennt, die gerade offen sind. Bei gestopptem Agent aus der Datei;
    /// offene Verbindungen kann es dann keine geben.
    /// </summary>
    public static async Task RevokeAsync(
        string clientId, CancellationToken cancellationToken = default)
    {
        if (await DeleteAsync($"/api/clients/{Uri.EscapeDataString(clientId)}", cancellationToken))
        {
            return;
        }

        AgentData.Revoke(clientId);
    }

    /// <summary>
    /// Ob der Agent gerade läuft.
    ///
    /// Am Rechner ist „dieses Gerät freigeben" keine Einstellung der
    /// Oberfläche, sondern eine Auskunft: freigegeben ist er, solange der Agent
    /// läuft. Ein Schalter dafür stünde in der App und meinte etwas, das ihr
    /// nicht gehört.
    /// </summary>
    public static async Task<bool> RunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(
                $"https://127.0.0.1:{AgentData.AgentPort}/health", cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Ein frischer Kopplungscode.
    ///
    /// <para>
    /// Der eine Weg, der einen laufenden Agent wirklich voraussetzt: der Code
    /// lebt in seinem Speicher, und einlösen muss ihn ebenfalls er. Einen Code
    /// anzuzeigen, den niemand einlösen kann, wäre eine Einladung ins Leere —
    /// deshalb fliegt hier ein Fehler statt eines leeren Feldes.
    /// </para>
    /// </summary>
    public static async Task<JsonElement> CodeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsync(
            $"https://127.0.0.1:{AgentData.AgentPort}/api/pair/code",
            content: null,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    /// <summary>
    /// Wer diesen Rechner steuern darf. Bei gestopptem Agent aus der Datei — die
    /// Liste gilt auch dann, sie wirkt nur gerade nicht.
    /// </summary>
    public static async Task<object?> ClientsAsync(CancellationToken cancellationToken = default)
    {
        var running = await ReadAsync("/api/clients", "clients", cancellationToken);

        return running is { } answered ? answered : AgentData.Clients();
    }

    /// <summary>
    /// Liest beim Agent und meldet, ob er überhaupt geantwortet hat.
    /// </summary>
    /// <returns>
    /// <c>null</c>, wenn kein Agent läuft oder er die Anfrage abgelehnt hat.
    /// Ein <c>JsonValueKind.Null</c> ist etwas anderes: dann hat er geantwortet,
    /// und die Antwort lautet „gibt es nicht".
    /// </returns>
    private static async Task<JsonElement?> ReadAsync(
        string path, string field, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(
                $"https://127.0.0.1:{AgentData.AgentPort}{path}", cancellationToken);

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
            // Kein Agent. Das ist der Normalfall auf einem Rechner, der nur
            // steuern und nicht gesteuert werden soll.
            return null;
        }
    }

    /// <summary>
    /// Schreibt beim Agent.
    /// </summary>
    /// <returns>
    /// <c>false</c>, wenn er nicht läuft — dann ist der Aufrufer dran, und zwar
    /// mit den Dateien. Ein Fehlschlag eines <em>laufenden</em> Agents fliegt
    /// dagegen weiter: die schreibenden Wege sind die Gegenrichtung selbst, und
    /// bleibt einer still liegen, sieht das später aus wie eine Kopplung, die
    /// nie angeboten wurde.
    /// </returns>
    private static async Task<bool> PostAsync(
        string path, object payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var response = await Client.PostAsync(
                $"https://127.0.0.1:{AgentData.AgentPort}{path}", content, cancellationToken);

            response.EnsureSuccessStatusCode();

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException { StatusCode: null }
                                       or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Löscht beim Agent.
    /// </summary>
    /// <returns>
    /// <c>false</c>, wenn er nicht läuft — dann ist der Aufrufer dran. Ein
    /// 404 zählt als erledigt: was nicht da ist, muss nicht weg.
    /// </returns>
    private static async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.DeleteAsync(
                $"https://127.0.0.1:{AgentData.AgentPort}{path}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return true;
            }

            response.EnsureSuccessStatusCode();

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException { StatusCode: null }
                                       or TaskCanceledException)
        {
            return false;
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
}
