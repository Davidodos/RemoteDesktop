using System.Diagnostics;
using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Wie dieser Rechner heißt und was auf ihm läuft.
///
/// <para>
/// **Was hier nicht mehr steht** (17.08.2026): die Karte „Als Nächstes" und die
/// Karte „Fernsteuerung". Die erste wiederholte in einem Satz, was die Karten
/// darunter ohnehin zeigten — und stand ganz oben, also dort, wo der Blick
/// zuerst hinfällt. Die zweite beschrieb das Fenster, in dem sie steht. Übrig
/// bleiben die beiden Dinge, deretwegen jemand hier nachsieht: läuft der Agent,
/// und unter welcher Adresse ist dieser Rechner erreichbar.
/// </para>
///
/// <para>
/// **Updates und „Über" stehen hier *und* in den Einstellungen.** Kein
/// Versehen: in den Einstellungen sucht man sie, wenn man etwas ändern will —
/// hier sieht man sie, ohne zu suchen. Der Unterschied ist der Umfang. Dort
/// steht, woher die Fassung kommt und welche es ist; hier nur der eine Satz,
/// der eine Handlung auslöst oder eben keine.
/// </para>
///
/// <para>
/// Welcher Handgriff wann passt, entscheidet <see cref="Inventory"/> — dort ist
/// es geprüft. Hier stehen nur Knöpfe.
/// </para>
/// </summary>
public sealed class OverviewPage : PageView
{
    private const string Project = "https://github.com/Davidodos/RemoteDesktop";

    private readonly WindowsProbe _probe;
    private readonly string? _appDirectory;
    private readonly Func<PartAction, Task> _perform;
    private readonly ClientUpdate _update = new();

    /// <summary>
    /// Die Teile, die auf diese Seite gehören. „Fernsteuerung" fehlt: das ist
    /// dieses Fenster, und ein Fenster, das über sich selbst berichtet, sagt
    /// nichts, was man nicht schon sieht.
    /// </summary>
    private static readonly string[] Shown = [Inventory.AgentTitle, Inventory.NetworkTitle];

    public OverviewPage(WindowsProbe probe, string? appDirectory, Func<PartAction, Task> perform)
        : base("Übersicht", string.Empty)
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

    /// <summary>
    /// Was in der Update-Karte steht. Als Zeichenkette und nicht als Control:
    /// <see cref="Stack.Clear"/> verwirft die Karten samt Inhalt, und ein
    /// gemerkter Verweis zeigte danach auf ein weggeräumtes Feld.
    /// </summary>
    private string _updateLine = "Wird geprüft…";

    private ReleaseOffer? _offer;

    /// <summary>Ob beim Start schon einmal nachgesehen wurde.</summary>
    private bool _checked;

    /// <summary>Ob gerade eine Suche oder eine Installation läuft.</summary>
    private bool _busy;

    public override async Task RefreshAsync()
    {
        // **Einmal je Start**, und zwar hier: die Übersicht ist die Seite, mit
        // der das Fenster aufgeht. Wer nie in die Einstellungen sieht, erfuhr
        // sonst nie, dass es eine neuere Fassung gibt.
        if (!_checked)
        {
            _checked = true;
            _ = LookForUpdateAsync();
        }

        var profile = NetworkStore.Read();
        var machine = await _probe.SnapshotAsync(_appDirectory);
        var name = AgentData.DeviceName();

        var state = $"{machine}|{profile}|{name}|{_updateLine}|{_busy}";

        if (state == _shown)
        {
            return;
        }

        Body.Clear();

        // Ganz oben der Name — das Erste, was ein anderes Gerät von diesem hier
        // zu sehen bekommt, und deshalb das Erste, was hier steht.
        Body.Add(new TextBlock(name, Theme.PageTitle, Theme.Text));

        foreach (var part in Inventory.For(machine, profile).Where(p => Shown.Contains(p.Title)))
        {
            Body.Add(PartCard(part));
        }

        Body.Add(UpdateCard());
        Body.Add(AboutCard());

        // Erst jetzt gemerkt, nicht vorher: geht auf dem Weg hierher etwas
        // schief, muss der nächste Versuch es noch einmal probieren. Sonst
        // bliebe die Seite leer und hielte sich für aktuell.
        _shown = state;
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
    /// Ein Satz und ein Knopf.
    ///
    /// Kein „Aktualisiert wird über den Installer" — das ist eine Auskunft über
    /// die Bauart und keine über diesen Rechner. Keine Fassungsnummer — sie
    /// steht eine Karte weiter unten, und zweimal dasselbe zu lesen heißt, es
    /// zweimal zu prüfen.
    /// </summary>
    private Card UpdateCard()
    {
        var card = new Card("Updates");
        var act = new ThemedButton(
            _offer is null ? "Nach Updates suchen" : "Jetzt installieren", ButtonTone.Primary)
        {
            Enabled = !_busy
        };

        act.Click += async (_, _) => await UpdateStepAsync();

        card.Body.Add(Row.Fill(new TextBlock(_updateLine), act));

        return card;
    }

    private Card AboutCard()
    {
        var card = new Card("Über");
        var project = new ThemedButton("Projektseite öffnen");

        project.Click += (_, _) => Open(Project);

        card.Body.Add(new TextBlock(
            $"RemoteDesktop {ClientUpdate.InstalledVersion()}\n"
            + $"Anzeigekomponente WebView2: {WebView2Runtime.InstalledVersion() ?? "fehlt"}\n"
            + $"Datenordner des Agents: {Elevation.DataDirectory}"));

        card.Body.Add(Row.Buttons(project));

        return card;
    }

    /// <summary>
    /// Nachsehen, ohne jemanden zu unterbrechen. Scheitert es — kein Netz, kein
    /// GitHub —, steht das da und sonst passiert nichts: eine Fassungssuche ist
    /// nichts, wofür sich eine Meldung lohnt, die jemand wegklicken muss.
    /// </summary>
    private async Task LookForUpdateAsync()
    {
        try
        {
            _offer = await _update.CheckAsync(CancellationToken.None);
            _updateLine = _offer is null ? "Alles aktuell." : $"Update verfügbar: {_offer.Version}.";
        }
        catch (Exception failure)
        {
            _updateLine = $"Nicht nachgesehen: {failure.Message}";
        }

        // Die Seite sieht im Takt nach; ein zurückgesetzter Vergleichswert
        // genügt, damit sie beim nächsten Blick neu zeichnet.
        _shown = null;
    }

    /// <summary>
    /// Ein Knopf mit zwei Bedeutungen: erst suchen, dann — wenn etwas dalag —
    /// installieren. Dieselbe Reihenfolge wie in den Einstellungen.
    /// </summary>
    private async Task UpdateStepAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _shown = null;

        try
        {
            if (_offer is null)
            {
                _updateLine = "Wird geprüft…";

                await LookForUpdateAsync();

                return;
            }

            _updateLine = "Wird geladen…";
            Report("Der Installer wird geladen. Das Fenster schließt sich gleich.", Tone.Working);

            await _update.InstallAsync(_offer, CancellationToken.None);

            // Der Installer läuft jetzt und will diese .exe ersetzen. Eine
            // laufende Datei lässt sich nicht überschreiben, also geht das
            // Fenster von selbst — und der Installer startet es danach wieder.
            Application.Exit();
        }
        catch (Exception failure)
        {
            _updateLine = $"Nicht geklappt: {failure.Message}";
            _offer = null;
            Report($"Nicht geklappt: {failure.Message}", Tone.Bad);
        }
        finally
        {
            _busy = false;
            _shown = null;
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _update.Dispose();
        }

        base.Dispose(disposing);
    }
}
