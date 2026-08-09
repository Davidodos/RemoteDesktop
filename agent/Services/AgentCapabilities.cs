namespace RemoteDesktopAgent.Services;

/// <summary>
/// Was dieses Gerät überhaupt kann. Steht in <c>/api/info</c>, und die App baut
/// ihre Seitenleiste daraus.
///
/// <para>
/// **Warum das nötig wurde:** seit V4 ist der Windows-Agent nicht mehr das
/// einzige Ziel — ein Handy meldet sich mit demselben Protokoll und kann davon
/// gerade drei Dinge. Ohne diese Liste müsste die App raten, wen sie vor sich
/// hat, und das wäre eine Fallunterscheidung, die genau einmal richtig
/// geschrieben und danach vergessen wird.
/// </para>
///
/// <para>
/// **Nicht zu verwechseln mit <see cref="Auth.AgentScopes"/>.** Die Fähigkeit
/// sagt, was das Gerät kann; das Recht sagt, was dieser eine Client davon darf.
/// Beides muss stimmen, damit eine Seite erscheint. Die Namen überschneiden
/// sich absichtlich — dass sie gleich bleiben, hält ein Test fest, statt dass
/// eine Seite die andere importiert.
/// </para>
/// </summary>
public static class AgentCapabilities
{
    /// <summary>Liefert ein Bild.</summary>
    public const string Screen = "screen";

    /// <summary>Nimmt Zeiger- und Texteingaben an.</summary>
    public const string Input = "input";

    /// <summary>
    /// Versteht echte Tastendrücke — Strg+C, F5, Pfeiltasten. Ein Handy kann
    /// das nicht: dort gibt es nur Text in das gerade fokussierte Feld.
    /// Daran hängen auch die Shortcuts, die nichts anderes sind als
    /// gespeicherte Tastenkombinationen.
    /// </summary>
    public const string Keys = "keys";

    public const string Media = "media";
    public const string Power = "power";
    public const string Actions = "actions";
    public const string Wake = "wake";

    /// <summary>Hat den Dateidienst (ab Phase 32).</summary>
    public const string Files = "files";

    /// <summary>
    /// Was ein Windows-Agent meldet. <see cref="Files"/> fehlt hier, solange
    /// der Dienst nicht gebaut ist — eine Fähigkeit anzukündigen, die es noch
    /// nicht gibt, wäre schlimmer als sie zu verschweigen.
    /// </summary>
    public static readonly IReadOnlyList<string> Windows =
        [Screen, Input, Keys, Media, Power, Actions, Wake];
}
