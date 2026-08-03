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
    public static IReadOnlyList<SetupStep> For(Selection selection, ISetupProbe probe)
    {
        var steps = new List<SetupStep>
        {
            new(
                "Tailscale installieren",
                "Tailscale verbindet deine Geräte direkt miteinander — ohne dass du am Router "
                + "etwas freigeben musst. Ohne diesen Schritt findet dein Handy den Rechner nicht.",
                probe.HasTailscale),

            new(
                "Bei Tailscale anmelden",
                "Einmal im Browser anmelden, damit dieser Rechner zu deinem Netz gehört. "
                + "Ein bestehendes Konto bei Google, Microsoft oder GitHub genügt.",
                probe.IsConnected)
        };

        if (selection.Has(SetupComponent.Agent))
        {
            steps.Add(new SetupStep(
                "Zertifikat holen",
                "Damit die Verbindung zu diesem Rechner verschlüsselt ist. Tailscale stellt es "
                + "kostenlos aus; du musst nichts kaufen und nichts eintragen.",
                probe.HasCertificate));

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
