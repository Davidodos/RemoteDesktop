using RemoteDesktopSetup;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Welche Rechte ein gekoppelter Client haben kann und welcher Pfad welches
/// verlangt.
///
/// Die Zuordnung ist bewusst eine Whitelist: ein Pfad, der hier nicht steht,
/// wird abgelehnt. Ein neuer Endpoint, bei dem jemand den Eintrag vergisst,
/// fällt dadurch beim ersten Aufruf auf — die Alternative wäre ein Endpoint,
/// den jeder Client mit irgendeinem Recht bedienen darf.
/// </summary>
public static class AgentScopes
{
    // Die Namen selbst stehen in der gemeinsamen Bibliothek: das Fenster trägt
    // bei gestopptem Agent selbst ein Gerät ein, und zwar mit allen Rechten.
    // Zwei Listen, die auseinanderlaufen können, wären genau die Art Fehler,
    // die erst am echten Gerät auffällt.
    public const string Screen = AgentScopeNames.Screen;
    public const string Input = AgentScopeNames.Input;
    public const string Media = AgentScopeNames.Media;
    public const string Power = AgentScopeNames.Power;
    public const string Actions = AgentScopeNames.Actions;
    public const string Wake = AgentScopeNames.Wake;

    public static readonly IReadOnlyList<string> All = AgentScopeNames.All;

    /// <summary>
    /// Pfade, die jeder angemeldete Client aufrufen darf. <c>/api/info</c> sagt
    /// nur, wie der Rechner heißt und welche Monitore er hat — ohne diese
    /// Auskunft könnte die App nicht einmal ihre Oberfläche aufbauen.
    /// </summary>
    ///
    /// <c>/api/unpair</c> braucht ebenfalls keins: wer sich austrägt, gibt
    /// etwas auf, statt etwas zu bekommen. Ein Recht dafür zu verlangen hieße,
    /// dass ein Gerät mit wenigen Rechten gekoppelt bliebe, obwohl beide Seiten
    /// es loswerden wollen.
    private static readonly string[] WithoutScope = ["/api/info", "/api/unpair"];

    private static readonly (string Prefix, string Scope)[] Mapping =
    [
        ("/ws/screen", Screen),
        ("/api/webrtc", Screen),
        ("/ws/input", Input),
        ("/api/media", Media),
        ("/api/power", Power),

        // Kein eigenes Recht: ein Update tauscht den Agent aus und startet ihn
        // neu — das ist derselbe Eingriff, den 'power' ohnehin erlaubt, nur
        // harmloser. Ein siebtes Recht hätte dagegen jedes bereits gekoppelte
        // Gerät ausgesperrt, weil in dessen clients.json-Eintrag nur die sechs
        // von damals stehen.
        ("/api/update", Power),
        ("/api/actions", Actions),
        ("/api/wol", Wake)
    ];

    public static bool IsKnown(string scope) => All.Contains(scope);

    /// <summary>
    /// Ermittelt das nötige Recht für einen Pfad.
    /// </summary>
    /// <param name="scope">
    /// Das verlangte Recht, oder <c>null</c>, wenn der Pfad keins braucht.
    /// </param>
    /// <returns>
    /// <c>false</c> für einen unbekannten Pfad — dann wird abgelehnt, nicht
    /// durchgelassen.
    /// </returns>
    public static bool TryResolve(string path, out string? scope)
    {
        scope = null;

        if (WithoutScope.Any(known => Matches(path, known)))
        {
            return true;
        }

        foreach (var (prefix, required) in Mapping)
        {
            if (Matches(path, prefix))
            {
                scope = required;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Vergleicht auf Segmentgrenzen. Ein einfacher Präfixvergleich würde
    /// <c>/api/powerful</c> als <c>/api/power</c> durchgehen lassen.
    /// </summary>
    private static bool Matches(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == prefix.Length || path[prefix.Length] == '/';
    }
}
