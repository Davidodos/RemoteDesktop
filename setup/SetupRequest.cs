using System.Text.Json;

namespace RemoteDesktopSetup;

/// <summary>Was am Ende der Einrichtung mit dem Agent geschehen soll.</summary>
public enum AgentSetup
{
    /// <summary>Gar nichts — dieser Rechner steuert nur und wird nicht gesteuert.</summary>
    None,

    /// <summary>Eintragen und starten, aber nicht beim Hochfahren mitstarten.</summary>
    Manual,

    /// <summary>Eintragen, starten, und ab jetzt mit Windows hochkommen.</summary>
    Automatic
}

/// <summary>
/// Der ganze Abschluss einer Einrichtung in einer Datei.
///
/// <para>
/// **Warum es das gibt:** die Einrichtung schreibt das Netzprofil, trägt den
/// Dienst ein, stellt seinen Starttyp und startet ihn. Jeder dieser Schritte
/// verlangt Administratorrechte, und jeder einzeln gesprungen hieße vier
/// Rückfragen von Windows hintereinander. Vier Nachfragen für eine Entscheidung
/// klickt niemand aufmerksam durch — also geht alles in einem Auftrag.
/// </para>
///
/// <para>
/// Übergeben wird eine Datei und keine Kommandozeile: dort stünde JSON, und
/// jedes Anführungszeichen darin wäre eine Einladung, etwas anderes zu bedeuten.
/// </para>
/// </summary>
/// <param name="Profile">Das Netzprofil, so wie es in die <c>setup.json</c> geht.</param>
/// <param name="Agent">Was mit dem Dienst geschehen soll.</param>
public sealed record SetupRequest(NetworkProfile Profile, AgentSetup Agent)
{
    public string Write()
    {
        var document = new Document(
            NetworkConfig.Write(Profile.Normalized()),
            Agent.ToString());

        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>
    /// Liest einen Auftrag. Unlesbares ergibt <c>null</c> statt einer
    /// Teilausführung: eine halb verstandene Einrichtung wäre schlimmer als
    /// keine — sie richtete etwas ein, das niemand so gewählt hat.
    /// </summary>
    public static SetupRequest? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Options);

            if (document?.Network is null)
            {
                return null;
            }

            return new SetupRequest(
                NetworkConfig.Read(document.Network),
                Enum.TryParse<AgentSetup>(document.Agent, ignoreCase: true, out var agent)
                    ? agent
                    : AgentSetup.None);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Das Netzprofil steht als fertiger Text drin und nicht als verschachteltes
    /// Objekt: geschrieben und gelesen wird es ausschließlich von
    /// <see cref="NetworkConfig"/>, und zwei Wege in dieselbe Datei wären zwei
    /// Gelegenheiten, sie unterschiedlich zu verstehen.
    /// </summary>
    private sealed record Document(string? Network, string? Agent);
}
