namespace RemoteDesktopSetup;

/// <summary>Ein Handgriff, den die Oberfläche an einem Teil anbietet.</summary>
public enum PartAction
{
    /// <summary>Den Dienst eintragen, damit Windows ihn kennt.</summary>
    Install,

    /// <summary>Den Eintrag wieder entfernen. Die Dateien bleiben liegen.</summary>
    Remove,

    Start,

    Stop,

    /// <summary>Das Fernsteuerfenster öffnen.</summary>
    Open,

    /// <summary>Tailscale herunterladen — ein fremdes Programm, von deren Seite.</summary>
    Download,

    /// <summary>Bei Tailscale anmelden.</summary>
    SignIn,

    /// <summary>Das Zertifikat von Tailscale holen.</summary>
    Certificate
}

/// <summary>Was der Rechner über ein Teil meldet — die Rohdaten, ohne Deutung.</summary>
/// <param name="AgentBinary">Ob <c>RemoteDesktopAgent.exe</c> überhaupt daliegt.</param>
/// <param name="AgentService">Ob der Dienst bei Windows eingetragen ist.</param>
/// <param name="AgentRunning">Ob er gerade antwortet.</param>
/// <param name="ClientFiles">Ob die Oberfläche für das Fernsteuerfenster daliegt.</param>
/// <param name="WebView2">Ob die Anzeigekomponente von Windows vorhanden ist.</param>
/// <param name="AgentProcess">
/// Ob überhaupt ein Agent-Prozess läuft. Nicht dasselbe wie
/// <paramref name="AgentRunning"/>: der sagt, ob er auch **antwortet**. Ein
/// Prozess, der läuft und schweigt, ist eine eigene Auskunft — und die schickt
/// beim Suchen an eine andere Stelle als „läuft gar nicht".
/// </param>
/// <param name="LegacyService">
/// Ob noch der Windows-Dienst aus v1.2 eingetragen ist. Er antwortet zwar, kann
/// aber weder Bild noch Eingabe — siehe <see cref="AgentTask"/>.
/// </param>
public sealed record Machine(
    bool AgentBinary = false,
    bool AgentService = false,
    bool AgentRunning = false,
    bool AgentProcess = false,
    bool LegacyService = false,
    bool ClientFiles = false,
    bool WebView2 = false,
    bool Tailscale = false,
    bool TailscaleConnected = false,
    bool Certificate = false);

/// <summary>
/// Ein Teil von RemoteDesktop, so wie es im Fenster steht.
/// </summary>
/// <param name="Ok">
/// Ob dieses Teil einsatzbereit ist. Nicht dasselbe wie „läuft": ein Client ist
/// bereit, sobald er sich öffnen lässt.
/// </param>
/// <param name="Missing">
/// Ob es auf diesem Rechner gar nicht eingerichtet ist. Es wird trotzdem
/// angezeigt — das ist der ganze Punkt.
/// </param>
public sealed record Part(
    string Title,
    string Purpose,
    string State,
    bool Ok,
    bool Missing,
    IReadOnlyList<PartAction> Actions);

/// <summary>
/// Alle Teile auf einen Blick — auch die, die es hier nicht gibt.
///
/// <para>
/// **Der Befund dahinter:** bis Release v1.0.0 zeigte das Fenster nur, was
/// installiert war (<c>ClientTrayContext.InstalledSelection</c>). Wer nur den
/// Agent installiert hatte, sah gar kein Fenster; wer nur den Client hatte, sah
/// den Agent nirgends und hatte keinen Weg, ihn nachzuholen, außer den Installer
/// erneut zu suchen. Ein Teil, das fehlt, ist eine Auskunft — kein Grund, es zu
/// verschweigen.
/// </para>
/// </summary>
public static class Inventory
{
    public const string AgentTitle = "Agent";
    public const string ClientTitle = "Fernsteuerung";
    public const string NetworkTitle = "Netz";

    public static IReadOnlyList<Part> For(Machine machine, NetworkProfile profile) =>
        [Agent(machine), Client(machine), Network(machine, profile)];

    /// <summary>Ein Satz für den Knopf, kein Aufzählungsname.</summary>
    public static string Describe(PartAction action) => action switch
    {
        PartAction.Install => "Einrichten",
        PartAction.Remove => "Entfernen",
        PartAction.Start => "Starten",
        PartAction.Stop => "Beenden",
        PartAction.Open => "Öffnen",
        PartAction.Download => "Tailscale herunterladen",
        PartAction.SignIn => "Jetzt anmelden",
        _ => "Zertifikat holen"
    };

    /// <summary>
    /// Der Agent, der diesen Rechner steuerbar macht.
    ///
    /// Ohne die Datei gibt es nichts einzurichten — dann ist die Installation
    /// unvollständig, und ein Knopf „Einrichten" liefe ins Leere.
    /// </summary>
    private static Part Agent(Machine machine)
    {
        if (!machine.AgentBinary)
        {
            return new Part(
                AgentTitle,
                "Macht diesen Rechner fernsteuerbar. Läuft im Hintergrund und lässt nur "
                + "Geräte herein, die du ausdrücklich gekoppelt hast.",
                "nicht installiert — die Programmdatei fehlt",
                Ok: false,
                Missing: true,
                []);
        }

        // Der alte Dienst antwortet und sieht damit gesund aus. Er ist es
        // nicht: in Sitzung 0 gibt es keinen Bildschirm, und jede Eingabe
        // scheitert an der Trennung der Sitzungen. Am echten Gerät sah das aus
        // wie ein kaputter Agent — dabei war es die falsche Startart.
        if (machine.LegacyService)
        {
            return new Part(
                AgentTitle,
                "Macht diesen Rechner fernsteuerbar. Er läuft noch als Windows-Dienst — so "
                + "sieht er keinen Bildschirm und kann keine Eingaben machen. Einmal neu "
                + "einrichten stellt das um.",
                "läuft als Dienst — muss umgestellt werden",
                Ok: false,
                Missing: false,
                [PartAction.Install]);
        }

        if (!machine.AgentService)
        {
            return new Part(
                AgentTitle,
                "Macht diesen Rechner fernsteuerbar. Läuft im Hintergrund und lässt nur "
                + "Geräte herein, die du ausdrücklich gekoppelt hast.",
                "nicht eingerichtet",
                Ok: false,
                Missing: true,
                [PartAction.Install]);
        }

        if (machine.AgentRunning)
        {
            return new Part(
                AgentTitle,
                "Macht diesen Rechner fernsteuerbar.",
                "läuft",
                Ok: true,
                Missing: false,
                [PartAction.Stop, PartAction.Remove]);
        }

        // Ein Prozess, der läuft und nicht antwortet, ist etwas anderes als
        // keiner. Beides „gestoppt" zu nennen schickte am echten Gerät zum
        // Startknopf — und der half nicht, weil der Agent ja lief.
        return machine.AgentProcess
            ? new Part(
                AgentTitle,
                "Macht diesen Rechner fernsteuerbar.",
                "läuft, antwortet aber nicht",
                Ok: false,
                Missing: false,
                [PartAction.Stop, PartAction.Start, PartAction.Remove])
            : new Part(
                AgentTitle,
                "Macht diesen Rechner fernsteuerbar.",
                "eingerichtet, aber gestoppt",
                Ok: false,
                Missing: false,
                [PartAction.Start, PartAction.Remove]);
    }

    /// <summary>
    /// Das Fenster, mit dem man andere Rechner steuert. Es ist kein Dienst,
    /// deshalb gibt es hier nichts zu starten oder zu beenden — nur zu öffnen.
    /// </summary>
    private static Part Client(Machine machine)
    {
        const string purpose = "Das Fenster, mit dem du von hier aus andere Rechner steuerst.";

        if (!machine.WebView2)
        {
            return new Part(
                ClientTitle,
                purpose,
                "Windows fehlt die Anzeigekomponente WebView2",
                Ok: false,
                Missing: true,
                []);
        }

        return machine.ClientFiles
            ? new Part(ClientTitle, purpose, "bereit", true, false, [PartAction.Open])
            : new Part(ClientTitle, purpose, "nicht installiert", false, true, []);
    }

    /// <summary>
    /// Der Weg, auf dem das Handy hierher findet. Was hier zu tun ist, hängt am
    /// gewählten Modus — im Heimnetz und im eigenen VPN gibt es nichts zu
    /// installieren.
    /// </summary>
    private static Part Network(Machine machine, NetworkProfile profile)
    {
        if (!profile.NeedsTailscale)
        {
            var known = profile.AdvertisedAddress;

            return new Part(
                NetworkTitle,
                profile.Describe(),
                known is null ? "Adresse fehlt noch" : $"erreichbar als {known}",
                Ok: known is not null,
                Missing: false,
                []);
        }

        if (!machine.Tailscale)
        {
            return new Part(
                NetworkTitle, profile.Describe(), "Tailscale ist nicht installiert",
                false, true, [PartAction.Download]);
        }

        if (!machine.TailscaleConnected)
        {
            return new Part(
                NetworkTitle, profile.Describe(), "Tailscale läuft, ist aber nicht angemeldet",
                false, false, [PartAction.SignIn]);
        }

        // Das Zertifikat fehlt zu lassen ist kein Fehler mehr — der Agent stellt
        // sich sonst selbst eins aus. Es bleibt trotzdem der bessere Weg: ein
        // Zertifikat von Tailscale kennt jeder Browser bereits.
        return machine.Certificate
            ? new Part(NetworkTitle, profile.Describe(), "verbunden", true, false, [])
            : new Part(
                NetworkTitle,
                profile.Describe(),
                "verbunden — ohne Zertifikat von Tailscale muss jedes Handy einmal bestätigen",
                Ok: true,
                Missing: false,
                [PartAction.Certificate]);
    }
}
