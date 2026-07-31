using System.Text.Json;
using System.Text.RegularExpressions;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Actions;

/// <summary>
/// Wird beim Start geworfen, wenn <c>actions.json</c> nicht in Ordnung ist.
/// Die Meldung ist für den Menschen gedacht, der die Datei geschrieben hat.
/// </summary>
public sealed class ActionConfigurationException : Exception
{
    public ActionConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Die <c>actions.json</c> neben der <c>appsettings.json</c>: was dieser Rechner
/// auf Zuruf tun darf.
///
/// <para>
/// Geprüft wird <b>beim Start</b>, nicht beim Auslösen. Ein Tippfehler im Pfad
/// soll auffallen, während jemand am Rechner sitzt — und nicht Wochen später,
/// wenn der Knopf am Handy nichts tut und niemand weiß, warum. Der Agent
/// startet dann gar nicht erst und sagt im Klartext, welcher Eintrag falsch
/// ist.
/// </para>
///
/// <para>
/// Fehlt die Datei ganz, ist das kein Fehler: ein frisch eingerichteter Rechner
/// hat keine Aktionen, und die App kommt damit zurecht.
/// </para>
/// </summary>
public sealed partial class ActionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyDictionary<string, AgentAction> _byId;

    private ActionCatalog(IReadOnlyDictionary<string, AgentAction> byId)
    {
        _byId = byId;
    }

    /// <summary>Die Aktionen in der Reihenfolge der Datei.</summary>
    public IReadOnlyList<AgentAction> All => [.. _byId.Values];

    public AgentAction? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Was der Client zu sehen bekommt — ohne Pfade und Argumente.</summary>
    public IReadOnlyList<ActionSummary> Summaries() =>
    [
        .. _byId.Values.Select(action => new ActionSummary(
            action.Id!,
            action.Label!,
            action.Icon,
            action.Type.ToString().ToLowerInvariant(),
            action.Confirm))
    ];

    public static ActionCatalog Empty() => new(new Dictionary<string, AgentAction>());

    /// <param name="fileExists">
    /// Wird hereingereicht, damit die Prüfung ohne echtes Dateisystem laufen
    /// kann. Im Betrieb ist es <see cref="System.IO.File.Exists(string)"/>.
    /// </param>
    /// <exception cref="ActionConfigurationException">
    /// Sobald ein Eintrag nicht stimmt. Ein halb gültiger Katalog wäre
    /// schlimmer als keiner — dann wüsste niemand, welche Knöpfe wirklich gehen.
    /// </exception>
    public static ActionCatalog Load(string path, Func<string, bool>? fileExists = null)
    {
        if (!System.IO.File.Exists(path))
        {
            return Empty();
        }

        return Parse(System.IO.File.ReadAllText(path), fileExists ?? System.IO.File.Exists);
    }

    public static ActionCatalog Parse(string json, Func<string, bool> fileExists)
    {
        var actions = Deserialize(json);
        var byId = new Dictionary<string, AgentAction>(StringComparer.Ordinal);

        foreach (var action in actions)
        {
            var id = (action.Id ?? string.Empty).Trim();

            if (!IdPattern().IsMatch(id))
            {
                throw new ActionConfigurationException(
                    $"Die Kennung '{action.Id}' taugt nicht: erlaubt sind Kleinbuchstaben, " +
                    "Ziffern und Bindestriche, beginnend mit einem Buchstaben oder einer Ziffer.");
            }

            if (!byId.TryAdd(id, action with { Id = id }))
            {
                throw new ActionConfigurationException(
                    $"Die Kennung '{id}' kommt zweimal vor. Welche der beiden gemeint ist, " +
                    "kann niemand entscheiden.");
            }
        }

        foreach (var action in byId.Values)
        {
            Validate(action, byId, fileExists);
        }

        // Erst wenn jede Aktion für sich stimmt, ergibt die Prüfung auf Kreise
        // Sinn — sie folgt denselben Verweisen.
        foreach (var action in byId.Values)
        {
            EnsureNoCycle(action, byId, []);
        }

        return new ActionCatalog(byId);
    }

    private static IReadOnlyList<AgentAction> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AgentAction>>(json, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // Der häufigste Fall ist `"args": "--foo --bar"` statt eines Arrays.
            // Die Rohmeldung von System.Text.Json ist englisch und nennt nur
            // eine Byte-Position — das hilft niemandem weiter.
            throw new ActionConfigurationException(
                "actions.json lässt sich nicht lesen. Häufigste Ursache: 'args' steht als " +
                "Zeichenkette statt als Array (richtig ist [\"--eins\", \"--zwei\"]), oder " +
                $"'type' nennt eine Art, die es nicht gibt.\n\nDetails: {ex.Message}");
        }
    }

    private static void Validate(
        AgentAction action, IReadOnlyDictionary<string, AgentAction> byId, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(action.Label))
        {
            throw Fault(action, "hat keine Beschriftung.");
        }

        switch (action.Type)
        {
            case ActionType.Process:
                RequireExistingFile(action, fileExists);
                RequireArgumentsWithoutNulls(action);
                break;

            case ActionType.Script:
                RequireExistingFile(action, fileExists);

                // Gestartet wird mit `powershell -File`. Eine andere Endung
                // liefe entweder ins Leere oder — bei .exe — an der Prüfung
                // vorbei, die dieser Typ eigentlich sein soll.
                if (!action.File!.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    throw Fault(action, "verweist auf keine .ps1-Datei.");
                }

                break;

            case ActionType.Keys:
                RequireResolvableChord(action);
                break;

            case ActionType.Url:
                RequireWebAddress(action);
                break;

            case ActionType.Sequence:
                RequireValidSteps(action, byId);
                break;

            default:
                throw Fault(action, $"nennt die unbekannte Art '{action.Type}'.");
        }
    }

    private static void RequireExistingFile(AgentAction action, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(action.File))
        {
            throw Fault(action, "nennt keine Datei.");
        }

        if (!fileExists(action.File))
        {
            throw Fault(action, $"verweist auf '{action.File}' — dort liegt nichts.");
        }
    }

    private static void RequireArgumentsWithoutNulls(AgentAction action)
    {
        if (action.Args?.Any(argument => argument is null) == true)
        {
            throw Fault(action, "hat einen leeren Eintrag in 'args'.");
        }
    }

    private static void RequireResolvableChord(AgentAction action)
    {
        if (action.Chord is not { Count: > 0 })
        {
            throw Fault(action, "nennt keine Tasten.");
        }

        foreach (var key in action.Chord)
        {
            if (!VirtualKeys.TryResolve(key ?? string.Empty, out _))
            {
                throw Fault(action, $"nennt die unbekannte Taste '{key}'.");
            }
        }
    }

    private static void RequireWebAddress(AgentAction action)
    {
        if (!Uri.TryCreate(action.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            // Nur http und https. Ein `file:`- oder `ms-settings:`-Ziel wäre
            // ein zweiter Weg, Beliebiges zu starten — vorbei an allem, was die
            // Typen oben absichern sollen.
            throw Fault(action, $"nennt keine http(s)-Adresse: '{action.Url}'.");
        }
    }

    private static void RequireValidSteps(
        AgentAction action, IReadOnlyDictionary<string, AgentAction> byId)
    {
        if (action.Steps is not { Count: > 0 })
        {
            throw Fault(action, "hat keine Schritte.");
        }

        foreach (var step in action.Steps)
        {
            var hasAction = !string.IsNullOrWhiteSpace(step.Action);

            if (hasAction == step.DelayMs.HasValue)
            {
                throw Fault(action, "hat einen Schritt, der weder genau eine Aktion noch genau eine Pause ist.");
            }

            if (step.DelayMs is < 0 or > 60_000)
            {
                throw Fault(action, $"pausiert {step.DelayMs} ms — erlaubt sind 0 bis 60000.");
            }

            if (hasAction && !byId.ContainsKey(step.Action!))
            {
                throw Fault(action, $"ruft '{step.Action}' auf, was es nicht gibt.");
            }
        }
    }

    /// <summary>
    /// Eine Sequenz, die sich selbst aufruft, liefe endlos und nähme den Rechner
    /// mit. Der Kreis fällt beim Start auf, nicht beim ersten Druck auf den Knopf.
    /// </summary>
    private static void EnsureNoCycle(
        AgentAction action, IReadOnlyDictionary<string, AgentAction> byId, HashSet<string> seen)
    {
        if (action.Type != ActionType.Sequence)
        {
            return;
        }

        if (!seen.Add(action.Id!))
        {
            throw Fault(action, "ruft sich am Ende selbst auf.");
        }

        foreach (var step in action.Steps!)
        {
            if (step.Action is not null && byId.TryGetValue(step.Action, out var next))
            {
                EnsureNoCycle(next, byId, seen);
            }
        }

        seen.Remove(action.Id!);
    }

    /// <summary>
    /// Immer dieselbe Form: welche Aktion, was ist mit ihr. Eine Fabrikmethode
    /// und kein eigener Ausnahmetyp — nach außen soll es genau eine Ausnahme
    /// geben, auf die man prüfen kann.
    /// </summary>
    private static ActionConfigurationException Fault(AgentAction action, string problem) =>
        new($"Die Aktion '{action.Id}' {problem}");

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex IdPattern();
}
