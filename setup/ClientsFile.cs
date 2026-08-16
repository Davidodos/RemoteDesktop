using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteDesktopSetup;

/// <summary>
/// Die <c>clients.json</c>: wer diesen Rechner steuern darf.
///
/// <para>
/// **Warum die Datei hier steht und nicht nur im Agent:** koppeln soll auch
/// gehen, wenn der Agent eingerichtet, aber gestoppt ist. Wer diesen Rechner nur
/// zum Steuern anderer benutzt, soll ihn nicht starten müssen, um ein Gerät zu
/// koppeln — und die Gegenrichtung einer Kopplung ist genau ein Eintrag in
/// dieser Datei.
/// </para>
///
/// <para>
/// **Zwei Schreiber, einer nach dem anderen.** Läuft der Agent, geht alles über
/// ihn: er hält die Liste im Speicher, und eine Datei unter ihm zu ändern ginge
/// beim nächsten Schreiben verloren. Läuft er nicht, schreibt das Fenster hier
/// direkt — beim nächsten Start liest der Agent die Datei ohnehin neu. Wer
/// entscheidet, steht in <c>desktop/LocalNode.cs</c>.
/// </para>
///
/// <para>
/// Das Format ist dasselbe, das <c>agent/Auth/ClientStore.cs</c> liest und
/// schreibt — es benutzt diese Klasse dafür. Zwei Fassungen desselben Formats
/// wären ein Fehler, der erst am echten Gerät auffiele.
/// </para>
/// </summary>
public static class ClientsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Wo die Datei zu einem Datenordner liegt.</summary>
    public static string In(string dataDirectory) =>
        Path.Combine(dataDirectory, AgentPaths.ClientsFileName);

    /// <summary>
    /// Eine fehlende Datei ist der Normalfall beim ersten Start. Eine kaputte
    /// Datei ist es nicht — deshalb fliegt die Ausnahme, statt still mit einer
    /// leeren Liste weiterzulaufen und alle gekoppelten Geräte auszusperren.
    /// </summary>
    public static List<ClientEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var content = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ClientEntry>>(content, JsonOptions)
               ?? throw new InvalidOperationException($"{path} enthält keine Client-Liste.");
    }

    /// <summary>
    /// Schreibt die Liste — erst daneben, dann umbenennen.
    ///
    /// Ein Absturz mitten im Schreiben würde sonst die Liste aller zugelassenen
    /// Geräte zerstören, und damit den Fernzugang zu dem Rechner, an dem man
    /// gerade nicht sitzt.
    /// </summary>
    public static void Write(string path, IEnumerable<ClientEntry> clients)
    {
        var temporary = path + ".tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(temporary, JsonSerializer.Serialize(clients, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Trägt die Oberfläche der Gegenseite ein — die Gegenrichtung einer
    /// Kopplung, ohne laufenden Agent.
    ///
    /// <para>
    /// Ohne Rückfrage und ohne Code: der Schlüssel kam über eine Verbindung, an
    /// deren Anfang jemand einen Kopplungscode eingetippt oder einen QR-Code
    /// gescannt hat. Ein zweiter Eintrag desselben Geräts ersetzt den ersten —
    /// die Kennung kommt aus dem Schlüssel und nicht aus dem Namen.
    /// </para>
    /// </summary>
    public static void Grant(string path, string publicKey, string label, DateTimeOffset now)
    {
        var id = ClientKeyFile.Fingerprint(publicKey);
        var trimmed = label.Trim();

        var entry = new ClientEntry(
            id,
            trimmed.Length is > 0 and <= MaxLabelLength ? trimmed : "Gekoppeltes Gerät",
            publicKey,
            [.. AgentScopeNames.All],
            now,
            now);

        Write(path, [.. Read(path).Where(existing => existing.Id != id), entry]);
    }

    /// <summary>Länger nennt sich kein Gerät; alles darüber ist ein Versehen.</summary>
    private const int MaxLabelLength = 64;
}

/// <summary>
/// Ein Eintrag in der <c>clients.json</c>, so wie er auf der Platte steht.
///
/// Gespeichert wird nur der öffentliche Schlüssel. Wer die Datei liest, hat
/// deshalb nichts in der Hand — er kann prüfen, wer sich anmeldet, sich aber
/// nicht selbst als dieser Client ausgeben.
/// </summary>
public sealed record ClientEntry(
    string Id,
    string Label,
    string PublicKey,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt)
{
    /// <summary>
    /// Ob dieser Client ein Recht hat. <c>null</c> steht für einen Pfad, der
    /// keins verlangt — siehe <c>AgentScopes.TryResolve</c>.
    /// </summary>
    public bool Allows(string? scope) => scope is null || Scopes.Contains(scope);
}

/// <summary>
/// Die Rechte, die ein gekoppelter Client haben kann.
///
/// <para>
/// Sie stehen hier, weil sie an zwei Stellen gebraucht werden: der Agent prüft
/// mit ihnen jeden Aufruf (<c>agent/Auth/AgentScopes.cs</c>), und das Fenster
/// trägt bei gestopptem Agent selbst ein Gerät ein — mit allen.
/// </para>
/// </summary>
public static class AgentScopeNames
{
    public const string Screen = "screen";
    public const string Input = "input";
    public const string Media = "media";
    public const string Power = "power";
    public const string Actions = "actions";
    public const string Wake = "wake";

    public static readonly IReadOnlyList<string> All =
        [Screen, Input, Media, Power, Actions, Wake];
}
