namespace RemoteDesktopSetup;

/// <summary>
/// Was beim Hochfahren von allein losläuft.
///
/// Die Wahl gehört dem Nutzer und nicht dem Installer: derselbe Rechner kann
/// morgens Arbeitsgerät und abends Ziel einer Fernsteuerung sein, und wer nur
/// gelegentlich fernsteuert, will keinen Dienst, der dauerhaft lauscht.
/// </summary>
[Flags]
public enum AutostartMode
{
    None = 0,
    Agent = 1,
    Client = 2,
    Both = Agent | Client
}

public static class AutostartModes
{
    public static AutostartMode Without(this AutostartMode mode, AutostartMode part) =>
        mode & ~part;

    public static bool Starts(this AutostartMode mode, AutostartMode part) =>
        (mode & part) == part && part != AutostartMode.None;

    /// <summary>Ein Satz für die Oberfläche, kein Aufzählungsname.</summary>
    public static string Describe(this AutostartMode mode) => mode switch
    {
        AutostartMode.Both => "Agent und Client starten mit Windows",
        AutostartMode.Agent => "Nur der Agent startet mit Windows — dieser Rechner ist erreichbar, "
            + "zeigt aber kein Fenster",
        AutostartMode.Client => "Nur der Client startet mit Windows — dieser Rechner steuert andere, "
            + "ist selbst aber nicht erreichbar",
        _ => "Nichts startet mit Windows"
    };
}

/// <summary>
/// Wie der Starttyp eines Windows-Dienstes heißt. Eigene Aufzählung, damit die
/// Bibliothek ohne <c>System.ServiceProcess</c> auskommt und damit auf einem
/// Linux-Container prüfbar bleibt.
/// </summary>
public enum ServiceStart
{
    /// <summary>Startet mit Windows, ohne dass jemand angemeldet sein muss.</summary>
    Automatic,

    /// <summary>Liegt bereit, läuft aber nur, wenn jemand ihn startet.</summary>
    Manual
}

/// <summary>
/// Was für einen Modus tatsächlich zu tun ist — die eine Stelle, an der aus
/// einer Wahl konkrete Eingriffe werden.
/// </summary>
/// <param name="Service">Starttyp des Agent-Dienstes.</param>
/// <param name="ClientEntry">Ob der Client in den Autostart des Benutzers gehört.</param>
public sealed record AutostartPlan(ServiceStart Service, bool ClientEntry)
{
    /// <summary>
    /// Der Dienst wird auf <see cref="ServiceStart.Manual"/> gesetzt und nicht
    /// entfernt: „nicht automatisch starten" heißt nicht „deinstallieren".
    /// Andernfalls verlöre ein Nutzer, der den Autostart abschaltet, die
    /// Möglichkeit, den Agent später von Hand zu starten.
    /// </summary>
    public static AutostartPlan For(AutostartMode mode) => new(
        mode.Starts(AutostartMode.Agent) ? ServiceStart.Automatic : ServiceStart.Manual,
        mode.Starts(AutostartMode.Client));
}

/// <summary>
/// Der Zugriff auf Dienststeuerung und Autostart-Eintrag.
///
/// Als Schnittstelle, weil beides Windows ist: die Registry gibt es hier nicht,
/// <c>sc.exe</c> auch nicht. Die Entscheidung *was* geschehen soll, ist damit
/// prüfbar; *dass* es geschieht, bleibt Sache des Rechners.
/// </summary>
public interface IAutostartHost
{
    void SetServiceStart(ServiceStart start);

    void SetClientEntry(bool enabled);

    /// <summary>Was gerade eingestellt ist — für die Anzeige im Fenster.</summary>
    AutostartPlan Current();
}

public static class Autostart
{
    /// <summary>Name des Dienstes; er steht so auch im Installer.</summary>
    public const string ServiceName = "RemoteDesktopAgent";

    /// <summary>Name des Autostart-Eintrags unter <c>Run</c>.</summary>
    public const string ClientEntryName = "RemoteDesktopClient";

    /// <summary>
    /// Setzt den Modus. Der Rückgabewert ist der Plan, der ausgeführt wurde —
    /// das Fenster zeigt ihn an, statt „gespeichert" zu behaupten.
    /// </summary>
    public static AutostartPlan Apply(IAutostartHost host, AutostartMode mode)
    {
        var plan = AutostartPlan.For(mode);

        host.SetServiceStart(plan.Service);
        host.SetClientEntry(plan.ClientEntry);

        return plan;
    }

    /// <summary>Der umgekehrte Weg: aus dem Zustand des Rechners den Modus ablesen.</summary>
    public static AutostartMode Read(IAutostartHost host)
    {
        var plan = host.Current();
        var mode = AutostartMode.None;

        if (plan.Service == ServiceStart.Automatic)
        {
            mode |= AutostartMode.Agent;
        }

        if (plan.ClientEntry)
        {
            mode |= AutostartMode.Client;
        }

        return mode;
    }
}
