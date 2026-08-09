namespace RemoteDesktopSetup;

/// <summary>
/// Was der Rechner schon mitbringt. Als Schnittstelle, damit sich die Schritte
/// des Assistenten ohne Windows durchspielen lassen.
/// </summary>
public interface ISetupProbe
{
    /// <summary>Ob <c>tailscale.exe</c> auf dem Rechner liegt.</summary>
    bool HasTailscale { get; }

    /// <summary>Ob dieser Rechner bereits Teil eines Tailnets ist.</summary>
    bool IsConnected { get; }

    /// <summary>Der eigene Name im Tailnet — leer, solange es keinen gibt.</summary>
    string TailnetName { get; }

    /// <summary>Ob das Zertifikat für diesen Namen vorliegt.</summary>
    bool HasCertificate { get; }

    /// <summary>Ob der Agent-Dienst eingerichtet ist.</summary>
    bool HasService { get; }
}

/// <summary>Ein Schritt im Assistenten.</summary>
/// <param name="Title">Die Überschrift, kurz und ohne Fachwort.</param>
/// <param name="Explanation">Warum es diesen Schritt gibt — für jemanden ohne Vorwissen.</param>
/// <param name="Done">Ob er schon erledigt ist.</param>
/// <param name="Blocking">
/// Ob ohne ihn nichts weitergeht. Das Koppeln ist der einzige Schritt, der das
/// nicht ist: es bleibt immer möglich, ein weiteres Handy hinzuzunehmen.
/// </param>
public sealed record SetupStep(string Title, string Explanation, bool Done, bool Blocking = true);

/// <summary>
/// Der Einrichtungsassistent — als Liste von Schritten, nicht als Fenster.
///
/// Aus „installiere zwei Programme und verstehe VPN" sollen drei Handgriffe
/// werden (<c>docs/PLAN-V2.md</c>, Abschnitt 4b). Was davon noch aussteht, hängt
/// am Rechner und nicht an einer Reihenfolge, die jemand einmal aufgeschrieben
/// hat: wer Tailscale längst benutzt, überspringt die ersten beiden Schritte,
/// ohne dass ihm jemand erklärt, was ein Tailnet ist.
/// </summary>
public static class SetupSteps
{
    /// <summary>Der Titel des Schritts, an dem die Adresse eingetragen wird.</summary>
    public const string AddressStep = "Adresse festlegen";

    /// <summary>Der Titel des Schritts, der das Zertifikat von Tailscale holt.</summary>
    public const string CertificateStep = "Zertifikat holen";

    public static IReadOnlyList<SetupStep> For(Selection selection, ISetupProbe probe) =>
        For(selection, probe, NetworkProfile.Default);

    /// <summary>
    /// Die Schrittliste für dieses Profil.
    ///
    /// Sie hängt seit V3 am Netzmodus und nicht mehr allein an der Auswahl:
    /// Wer den Rechner nur aus dem eigenen WLAN steuert, soll nicht durch zwei
    /// Tailscale-Schritte laufen, die er nie braucht — genau das war die Hürde,
    /// an der die Einrichtung ohne VPN scheiterte.
    /// </summary>
    public static IReadOnlyList<SetupStep> For(
        Selection selection, ISetupProbe probe, NetworkProfile profile)
    {
        var steps = profile.NeedsTailscale
            ? TailscaleSteps(probe)
            : [];

        // Die Adresse gehört in jeden Modus. Sie durfte bei Tailscale einmal
        // fehlen — und dann stand der Windows-Rechnername im QR-Code, unter dem
        // im Tailnet niemand zu finden ist.
        steps.AddRange(AddressSteps(profile));

        if (selection.Has(SetupComponent.Agent))
        {
            if (profile.CanFetchCertificate)
            {
                steps.Add(new SetupStep(
                    CertificateStep,
                    "Damit die Verbindung zu diesem Rechner verschlüsselt ist. Tailscale stellt "
                    + "es kostenlos aus; du musst nichts kaufen und nichts eintragen.",
                    probe.HasCertificate));
            }

            steps.Add(new SetupStep(
                "Agent einrichten",
                "Der Dienst, der diesen Rechner steuerbar macht. Er läuft im Hintergrund und "
                + "lässt nur Geräte herein, die du ausdrücklich gekoppelt hast.",
                probe.HasService));
        }

        steps.Add(new SetupStep(
            "Handy koppeln",
            "Zum Schluss den QR-Code am Rechner mit der App scannen. Erst danach darf das "
            + "Handy überhaupt etwas — vorher kennt der Rechner es nicht.",

            // Bewusst nie „erledigt": ein zweites Handy zu koppeln bleibt immer
            // möglich, und ein abgehakter letzter Schritt sähe aus, als wäre
            // Schluss.
            Done: false,
            Blocking: false));

        return steps;
    }

    /// <summary>
    /// Der Weg über den Tailscale-Client: erst das Programm, dann die Anmeldung.
    /// Beides fremde Schritte, die RemoteDesktop nur anstößt — bei Headscale
    /// dieselben, nur gegen einen anderen Koordinator.
    /// </summary>
    private static List<SetupStep> TailscaleSteps(ISetupProbe probe) =>
    [
        new(
            "Tailscale installieren",
            "Tailscale verbindet deine Geräte direkt miteinander — ohne dass du am Router "
            + "etwas freigeben musst. Es ist der bequemste Weg, wenn du den Rechner auch von "
            + "unterwegs erreichen willst.",
            probe.HasTailscale),

        new(
            "Bei Tailscale anmelden",
            "Einmal im Browser anmelden, damit dieser Rechner zu deinem Netz gehört. "
            + "Ein bestehendes Konto bei Google, Microsoft oder GitHub genügt.",
            probe.IsConnected)
    ];

    /// <summary>
    /// Wie dieser Rechner heißt — die eine Angabe, ohne die nichts geht.
    ///
    /// Im Heimnetz ist das die Adresse, die der Router vergeben hat; bei einem
    /// fremden VPN die, die dort gilt; bei Tailscale und Headscale der Name im
    /// Tailnet. Verbinden und Einrichten eines fremden VPN bleibt Sache dessen,
    /// der es betreibt — RemoteDesktop startet fremde Programme nicht und prüft
    /// sie nicht.
    /// </summary>
    private static List<SetupStep> AddressSteps(NetworkProfile profile) =>
    [
        new(
            AddressStep,
            profile.Kind switch
            {
                NetworkKind.Lan =>
                    "Unter welcher Adresse dein Handy diesen Rechner im Heimnetz findet. "
                    + "Meistens steht sie schon da — du musst sie nur bestätigen.",
                NetworkKind.Vpn =>
                    "Trage die Adresse ein, unter der dieser Rechner in deinem VPN erreichbar "
                    + "ist. Wie du das herausfindest, steht in der Anleitung „Anderes VPN "
                    + "benutzen“.",
                _ =>
                    "Der Name dieses Rechners im Tailscale-Netz. Genau er steht später im "
                    + "QR-Code, und genau ihn muss das Handy auflösen können."
            },
            profile.AdvertisedAddress is not null)
    ];

    /// <summary>
    /// Der erste Schritt, der noch aussteht — <c>null</c>, wenn alles steht.
    /// Genau darauf zeigt das Fenster, statt eine Liste mit Haken anzubieten,
    /// in der man sich selbst zurechtfinden muss.
    /// </summary>
    public static SetupStep? Next(IReadOnlyList<SetupStep> steps) =>
        steps.FirstOrDefault(step => !step.Done);

    /// <summary>Ob die Einrichtung so weit ist, dass sich koppeln lässt.</summary>
    public static bool Ready(IReadOnlyList<SetupStep> steps) =>
        steps.Where(step => step.Blocking).All(step => step.Done);
}
