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
        : base("Übersicht", $"Dieser Rechner heißt {Environment.MachineName}.")
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
        var fingerprint = OwnFingerprint();

        var state = $"{machine}|{profile}|{fingerprint}";

        if (state == _shown)
        {
            return;
        }

        _shown = state;

        Body.Clear();

        if (NextStep(machine, profile) is { } next)
        {
            Body.Add(NextCard(next));
        }

        foreach (var part in Inventory.For(machine, profile))
        {
            Body.Add(PartCard(part));
        }

        Body.Add(FingerprintCard(fingerprint));
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

    /// <summary>
    /// Der Fingerabdruck der eigenen Zertifizierungsstelle. Er steht hier, weil
    /// ihn jemand ablesen muss: am Handy und am anderen Rechner wird genau
    /// dieser Wert zum Vergleich angezeigt, bevor dort etwas bestätigt wird.
    /// Ohne den Vergleich wäre das Bestätigen wertlos.
    /// </summary>
    private Card FingerprintCard(string? value)
    {
        var card = new Card("Fingerabdruck dieses Rechners");

        card.Body.Add(new TextBlock(
            "Diesen Wert zeigt das Handy an, bevor es diesem Rechner vertraut. "
            + "Stimmen beide überein, ist die Verbindung echt."));

        if (value is null)
        {
            card.Body.Add(new TextBlock(
                "Dieser Rechner weist sich (noch) nicht mit einer eigenen Stelle aus."));

            return card;
        }

        var field = new ThemedTextBox { Value = value, ReadOnly = true };
        field.UseMonospace();

        var copy = new ThemedButton("Kopieren");

        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(value);
                Report("Der Fingerabdruck liegt in der Zwischenablage.", Tone.Good);
            }
            catch (Exception failure)
            {
                // Die Zwischenablage gehört dem ganzen System, und ein anderes
                // Programm kann sie gerade halten. Daran darf das Fenster nicht
                // sterben — der Wert steht ja im Feld daneben und lässt sich
                // von Hand markieren.
                Report($"Nicht in die Zwischenablage gekommen: {failure.Message}", Tone.Bad);
            }
        };

        card.Body.Add(Row.Fill(field, copy));

        return card;
    }

    private static string? OwnFingerprint()
    {
        var file = Path.Combine(Elevation.DataDirectory, "agentca.crt");

        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var hex = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(file))).ToLowerInvariant();

            return string.Join(':', Enumerable.Range(0, hex.Length / 2)
                .Select(index => hex.Substring(index * 2, 2)));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Der Weg nach draußen für die Tailscale-Seite.</summary>
    public static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
