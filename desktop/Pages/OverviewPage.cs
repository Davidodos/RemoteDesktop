using System.Diagnostics;
using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Was auf diesem Rechner steht — und was als Nächstes dran ist.
///
/// <para>
/// **Der Befund dahinter:** bis Release v1.0.0 zeigte das Fenster nur, was
/// installiert war. Wer den Agent allein hatte, sah gar nichts; wer ihn
/// nachrüsten wollte, musste den Installer wiederfinden. Hier stehen alle Teile,
/// auch die nicht eingerichteten, und jedes hat den Knopf, der es einrichtet.
/// </para>
///
/// <para>
/// Welcher Handgriff wann passt, entscheidet <see cref="Inventory"/> — dort ist
/// es geprüft. Hier stehen nur Knöpfe.
/// </para>
/// </summary>
public sealed class OverviewPage : PageView
{
    private readonly WindowsProbe _probe;
    private readonly string? _appDirectory;
    private readonly Func<PartAction, Task> _perform;

    public OverviewPage(WindowsProbe probe, string? appDirectory, Func<PartAction, Task> perform)
        : base("Übersicht", "Was auf diesem Rechner steht.")
    {
        _probe = probe;
        _appDirectory = appDirectory;
        _perform = perform;
    }

    /// <summary>
    /// Diese Seite zeigt nur fremden Zustand — kein Feld, in das jemand tippt.
    /// Also darf sie von allein nachsehen.
    /// </summary>
    public override bool LiveRefresh => true;

    /// <summary>
    /// Woran die Seite erkennt, dass sich nichts geändert hat. Ohne das würde
    /// der Takt aus <see cref="ShellWindow"/> die Karten alle zwei Sekunden neu
    /// bauen — und ein Knopf, den man gerade drückt, wäre unter dem Finger weg.
    /// </summary>
    private string? _shown;

    public override async Task RefreshAsync()
    {
        var profile = NetworkStore.Read();
        var machine = await _probe.SnapshotAsync(_appDirectory);
        var name = AgentData.DeviceName();

        var state = $"{machine}|{profile}|{name}";

        if (state == _shown)
        {
            return;
        }

        Body.Clear();

        if (NextStep(machine, profile) is { } next)
        {
            Body.Add(NextCard(next));
        }

        foreach (var part in Inventory.For(machine, profile))
        {
            Body.Add(PartCard(part));
        }

        // Erst jetzt gemerkt, nicht vorher: geht auf dem Weg hierher etwas
        // schief, muss der nächste Versuch es noch einmal probieren. Sonst
        // bliebe die Seite leer und hielte sich für aktuell.
        _shown = state;
    }

    /// <summary>
    /// Der eine Satz, der sagt, was jetzt dran ist — oder <c>null</c>, wenn
    /// nichts mehr dran ist. Eine Karte „alles erledigt" wäre eine Karte, die
    /// nie wieder etwas mitteilt.
    /// </summary>
    private string? NextStep(Machine machine, NetworkProfile profile)
    {
        if (machine.LegacyService)
        {
            return "Den Agent neu einrichten — als Dienst kann er weder Bildschirm noch "
                   + "Eingaben, und genau das ist seine Aufgabe.";
        }

        if (!machine.AgentService && machine.AgentBinary)
        {
            return "Den Agent einrichten, damit dieser Rechner erreichbar wird.";
        }

        var selection = new Selection(
            SetupComponent.Agent | SetupComponent.Client, AutostartMode.None);

        var next = SetupSteps.Next(SetupSteps.For(selection, _probe, profile));

        return next is null ? null : $"{next.Title} — {next.Explanation}";
    }

    private static Card NextCard(string step)
    {
        var card = new Card("Als Nächstes");

        card.Body.Add(new TextBlock(step, Theme.Body, Theme.Text));

        return card;
    }

    private Card PartCard(Part part)
    {
        var card = new Card(part.Title);

        card.ShowState(part.State, part.Ok ? Theme.Online : part.Missing ? Theme.TextDim : Theme.Danger);
        card.Body.Add(new TextBlock(part.Purpose));

        if (part.Actions.Count == 0)
        {
            return card;
        }

        var buttons = part.Actions
            .Select((action, index) => Button(action, first: index == 0))
            .ToArray<Control>();

        card.Body.Add(Row.Buttons(buttons));

        return card;
    }

    /// <summary>
    /// Der erste Handgriff einer Karte ist der gemeinte — <see cref="Inventory"/>
    /// führt sie in dieser Reihenfolge. Deshalb ist genau er hervorgehoben und
    /// nicht der, der zufällig am besten klingt.
    /// </summary>
    private Control Button(PartAction action, bool first)
    {
        var tone = action switch
        {
            PartAction.Remove or PartAction.Stop => ButtonTone.Danger,
            _ when first => ButtonTone.Primary,
            _ => ButtonTone.Secondary
        };

        var button = new ThemedButton(Inventory.Describe(action), tone);

        button.Click += async (_, _) => await _perform(action);

        return button;
    }

    /// <summary>Der Weg nach draußen für die Tailscale-Seite.</summary>
    public static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
