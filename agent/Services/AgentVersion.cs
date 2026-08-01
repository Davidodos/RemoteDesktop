using System.Reflection;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Wie alt dieser Agent ist und mit welchen Clients er sich versteht.
///
/// Beides steht in <c>/api/info</c>. Seit Agent und App getrennt aktualisiert
/// werden, treffen zwangsläufig verschiedene Stände aufeinander — ohne diese
/// Auskunft merkt das niemand, bis eine Nachricht ankommt, die die Gegenseite
/// nicht kennt, und der Fehler sieht dann nach einem kaputten Rechner aus.
/// </summary>
public static class AgentVersion
{
    /// <summary>
    /// Die Sprache zwischen Agent und Client. Wird erhöht, wenn eine Änderung
    /// die alte Seite nicht mehr versteht — nicht bei jeder neuen Funktion.
    ///
    /// Wer nur etwas hinzufügt, das ältere Clients ignorieren können, lässt die
    /// Zahl stehen. Sonst hätte jede Kleinigkeit eine Warnung zur Folge, und die
    /// Warnung wäre nach dem dritten Mal nichts mehr wert.
    /// </summary>
    public const int Protocol = 1;

    /// <summary>
    /// Die Fassung aus den Assembly-Angaben, ohne den Git-Zusatz, den
    /// <c>dotnet publish</c> anhängt (<c>1.2.3+abcdef</c>).
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AgentVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var raw = informational ?? typeof(AgentVersion).Assembly.GetName().Version?.ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return "0.0.0";
        }

        var plus = raw.IndexOf('+');

        return plus < 0 ? raw : raw[..plus];
    }
}
