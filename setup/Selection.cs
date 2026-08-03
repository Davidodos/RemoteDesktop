namespace RemoteDesktopSetup;

/// <summary>
/// Die Teile, die man einzeln haben kann.
///
/// Die Trennung ist keine Spielerei: ein Rechner im Keller braucht nur den
/// Agent und nie ein Fenster, ein Arbeitslaptop nur den Client und niemals einen
/// Dienst, der Fremdzugriff erlaubt. Wer beides wählt, bekommt beides — das ist
/// der Normalfall auf dem Hauptrechner.
/// </summary>
[Flags]
public enum SetupComponent
{
    None = 0,

    /// <summary>Der Dienst, der den Rechner steuerbar macht.</summary>
    Agent = 1,

    /// <summary>Das Fenster, mit dem man andere Rechner steuert.</summary>
    Client = 2,

    /// <summary>
    /// Tailscale. Unabhängig von den beiden anderen, weil es meistens schon da
    /// ist — und weil es nicht von hier stammt, sondern nur mitinstalliert wird.
    /// </summary>
    Tailscale = 4
}

/// <summary>
/// Was der Nutzer im Installer angekreuzt hat, samt Prüfung.
/// </summary>
public sealed record Selection(SetupComponent Components, AutostartMode Autostart)
{
    /// <summary>Der Vorschlag für einen Rechner, auf dem noch nichts steht.</summary>
    public static Selection Default { get; } = new(
        SetupComponent.Agent | SetupComponent.Client | SetupComponent.Tailscale,
        AutostartMode.Both);

    public bool Has(SetupComponent component) => Components.HasFlag(component);

    /// <summary>
    /// Warum diese Auswahl nicht geht — <c>null</c>, wenn sie geht.
    ///
    /// Tailscale allein ist keine Installation dieses Programms, sondern das
    /// Herunterladen eines fremden. Wer nur das will, holt es sich dort.
    /// </summary>
    public string? Rejection =>
        Has(SetupComponent.Agent) || Has(SetupComponent.Client)
            ? null
            : "Wähle mindestens den Agent oder den Client. Tailscale allein "
              + "installiert nichts von RemoteDesktop.";

    /// <summary>
    /// Nimmt dem Autostart, was gar nicht installiert wird.
    ///
    /// Ein Autostart für einen Teil, den es auf diesem Rechner nicht gibt, wäre
    /// ein Eintrag, der bei jedem Anmelden ins Leere zeigt — und der Fehler
    /// fiele frühestens beim nächsten Neustart auf, weit weg von hier.
    /// </summary>
    public Selection Normalized()
    {
        var mode = Autostart;

        if (!Has(SetupComponent.Agent))
        {
            mode = mode.Without(AutostartMode.Agent);
        }

        if (!Has(SetupComponent.Client))
        {
            mode = mode.Without(AutostartMode.Client);
        }

        return this with { Autostart = mode };
    }
}
