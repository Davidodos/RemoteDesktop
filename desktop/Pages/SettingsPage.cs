using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Was beim Anmelden startet, woher die nächste Fassung kommt, und was gerade
/// läuft.
///
/// <para>
/// Beides gehört nicht auf die Übersicht: es ist einmal eingestellt und danach
/// selten wieder angefasst. Auf der Übersicht stünde es jedem im Weg, der nur
/// wissen will, ob der Agent läuft.
/// </para>
/// </summary>
public sealed class SettingsPage : PageView
{
    private const string Project = "https://github.com/Davidodos/RemoteDesktop";

    /// <summary>
    /// Wie die Protokolldatei des Agents heißt. Doppelt genannt statt geteilt:
    /// das Fenster hängt nicht am Agent-Projekt, und ein Verweis dorthin wäre
    /// eine Abhängigkeit für eine Zeichenkette.
    /// </summary>
    private const string AgentLogFile = "agent.log";

    private readonly IAutostartHost _autostart;
    private readonly ClientUpdate _update = new();

    /// <summary>
    /// Zwei Ja/Nein-Fragen statt vier gleichrangiger Modi — dieselben wie in der
    /// Einrichtung, siehe <see cref="AutostartModes.From"/>. Die zweite kommt
    /// nur, wenn die erste ein Ja war; sonst gibt es nichts zu entscheiden.
    /// </summary>
    private readonly ChoiceGroup<bool> _withWindows = new();
    private readonly ChoiceGroup<bool> _withAgent = new();
    private readonly TextBlock _agentQuestion = new("Soll der Agent auch automatisch starten?");

    /// <summary>Der Stapel, in dem die zweite Frage steckt — er blendet sie aus.</summary>
    private Stack? _autostartBody;

    /// <summary>
    /// Ob gerade der gespeicherte Stand eingelesen wird. Ohne diese Sperre löste
    /// das Einlesen dieselben Ereignisse aus wie ein Klick — und schriebe die
    /// Einstellung, die es gerade nur anzeigen wollte.
    /// </summary>
    private bool _reading;

    private readonly TextBlock _updateState;
    private readonly ThemedButton _updateAct = new("Nach Updates suchen", ButtonTone.Primary);

    private ReleaseOffer? _offer;

    private readonly Action _openSetup;

    public SettingsPage(IAutostartHost autostart, Action openSetup)
        : base("Einstellungen", "Start, Aktualisierung und was sonst selten gebraucht wird.")
    {
        _autostart = autostart;
        _openSetup = openSetup;

        _updateState = new TextBlock($"Fassung {ClientUpdate.InstalledVersion()}");

        _withWindows.Add(
            true,
            "Ja",
            "Das Fenster wartet nach dem Anmelden im Infobereich, ohne sich in den "
            + "Vordergrund zu drängen.");

        _withWindows.Add(
            false,
            "Nein",
            "Nichts startet von allein. Du öffnest RemoteDesktop, wenn du es brauchst.");

        _withAgent.Add(
            true,
            "Ja",
            "Dieser Rechner ist erreichbar, sobald du angemeldet bist.");

        _withAgent.Add(
            false,
            "Nein",
            "Den Agent startest du selbst — hier oder aus dem Infobereich.");

        _withWindows.Chosen += _ => Apply();
        _withAgent.Chosen += _ => Apply();

        _updateAct.Click += async (_, _) => await UpdateStepAsync();

        Body.Add(AutostartCard());
        Body.Add(SetupCard());
        Body.Add(UpdateCard());
        Body.Add(AboutCard());
    }

    /// <summary>Was gerade eingestellt ist, ohne dabei etwas auszulösen.</summary>
    public override Task RefreshAsync()
    {
        Show(Autostart.Read(_autostart));

        return Task.CompletedTask;
    }

    private void Show(AutostartMode mode)
    {
        _reading = true;

        try
        {
            _withWindows.Select(mode.WithWindows());
            _withAgent.Select(mode.Starts(AutostartMode.Agent));

            // Die zweite Frage ist ausgeblendet und nicht gesperrt: eine graue
            // Auswahl sieht aus wie etwas, das klemmt, und lässt offen, warum.
            _autostartBody?.Toggle(_agentQuestion, mode.WithWindows());
            _autostartBody?.Toggle(_withAgent, mode.WithWindows());
        }
        finally
        {
            _reading = false;
        }
    }

    private Card AutostartCard()
    {
        var card = new Card("Beim Anmelden starten");

        card.Body.Add(new TextBlock("Soll RemoteDesktop mit Windows starten?"));
        card.Body.Add(_withWindows);
        card.Body.Add(_agentQuestion);
        card.Body.Add(_withAgent);

        _autostartBody = card.Body;

        return card;
    }

    /// <summary>
    /// Der Weg zurück in die Einrichtung. Sie ist kein einmaliger Vorgang: den
    /// Agent später nachzurüsten oder den Netzmodus zu wechseln ist derselbe
    /// Ablauf wie beim ersten Mal.
    /// </summary>
    private Card SetupCard()
    {
        var card = new Card("Einrichtung");
        var again = new ThemedButton("Einrichtung erneut durchführen");

        again.Click += (_, _) => _openSetup();

        card.Body.Add(new TextBlock(
            "Nachträglich den Agent einrichten, den Netzmodus wechseln, die Adresse "
            + "ändern oder das Zertifikat neu holen — derselbe Weg wie beim ersten "
            + "Start. Er steht nur hier: in der Seitenleiste wäre er eine ständige "
            + "Einladung, eine fertige Einrichtung noch einmal anzufassen. Die Adresse "
            + "allein steht auch unter „Netz“."));

        card.Body.Add(Row.Buttons(again));

        return card;
    }

    /// <summary>
    /// Agent und Fenster zusammen erneuern — über den Installer, nicht über
    /// einzeln kopierte Dateien. Beide stecken in demselben Paket, deshalb steht
    /// der Knopf auch nur einmal da.
    /// </summary>
    private Card UpdateCard()
    {
        var card = new Card("Updates");

        card.Body.Add(new TextBlock(
            "Aktualisiert wird über den Installer. Er kennt die Komponenten, die beim "
            + "letzten Mal gewählt waren, und richtet nichts nach, was du bewusst "
            + "weggelassen hast."));

        card.Body.Add(Row.Fill(_updateState, _updateAct));

        return card;
    }

    private Card AboutCard()
    {
        var card = new Card("Über");
        var project = new ThemedButton("Projektseite öffnen");

        project.Click += (_, _) => OverviewPage.Open(Project);

        card.Body.Add(new TextBlock(
            $"RemoteDesktop {ClientUpdate.InstalledVersion()}\n"
            + $"Anzeigekomponente WebView2: {WebView2Runtime.InstalledVersion() ?? "fehlt"}\n"
            + $"Datenordner des Agents: {Elevation.DataDirectory}"));

        // Der Agent hat kein Fenster und damit keine Konsole. Was er meldet,
        // steht ausschließlich in dieser Datei — und ohne einen Weg dorthin ist
        // sie so gut wie nicht vorhanden.
        var logPath = Path.Combine(Elevation.DataDirectory, AgentLogFile);
        var log = new ThemedButton("Protokoll des Agents öffnen");

        log.Click += (_, _) =>
        {
            if (!File.Exists(logPath))
            {
                Report(
                    "Es gibt noch kein Protokoll — der Agent hat auf diesem Rechner noch "
                    + "nicht gelaufen.",
                    Tone.Bad);

                return;
            }

            OverviewPage.Open(logPath);
        };

        card.Body.Add(Row.Buttons(project, log));

        return card;
    }

    private void Apply()
    {
        if (_reading)
        {
            return;
        }

        var mode = AutostartModes.From(_withWindows.Selected, _withAgent.Selected);

        try
        {
            Autostart.Apply(_autostart, mode);
            Show(mode);
            Report("Gespeichert.", Tone.Good);
        }
        catch (Exception failure)
        {
            // Den Starttyp eines Dienstes zu ändern verlangt Adminrechte. Ohne
            // sie bleibt die Einstellung, wie sie war — das muss dastehen, sonst
            // glaubt jemand, er habe etwas eingestellt.
            Report($"Nicht gespeichert: {failure.Message}", Tone.Bad);
            Show(Autostart.Read(_autostart));
        }
    }

    /// <summary>
    /// Ein Knopf mit zwei Bedeutungen: erst suchen, dann — wenn etwas dalag —
    /// installieren. Zwei Knöpfe, von denen einer fast immer nutzlos ist, wären
    /// die schlechtere Antwort.
    /// </summary>
    private async Task UpdateStepAsync()
    {
        _updateAct.Enabled = false;

        try
        {
            if (_offer is null)
            {
                _updateState.Retext("Suche…");
                Report("Es wird nach einer neueren Fassung gesucht.", Tone.Working);

                _offer = await _update.CheckAsync(CancellationToken.None);

                if (_offer is null)
                {
                    _updateState.Retext(
                        $"Fassung {ClientUpdate.InstalledVersion()} — das ist die neueste.");

                    _updateAct.Relabel("Nach Updates suchen");
                    Report("Alles aktuell.", Tone.Good);

                    return;
                }

                _updateState.Retext($"Fassung {_offer.Version} liegt bereit.");
                _updateAct.Relabel("Jetzt installieren");
                Report($"Fassung {_offer.Version} steht zum Installieren bereit.", Tone.Good);

                return;
            }

            _updateState.Retext("Wird geladen…");
            Report("Der Installer wird geladen. Das Fenster schließt sich gleich.", Tone.Working);

            await _update.InstallAsync(_offer, CancellationToken.None);

            // Der Installer läuft jetzt und will diese .exe ersetzen. Eine
            // laufende Datei lässt sich nicht überschreiben, also geht das
            // Fenster von selbst — und der Installer startet es danach wieder.
            Application.Exit();
        }
        catch (Exception failure)
        {
            _updateState.Retext($"Nicht geklappt: {failure.Message}");
            _offer = null;
            _updateAct.Relabel("Nach Updates suchen");
            Report($"Nicht geklappt: {failure.Message}", Tone.Bad);
        }
        finally
        {
            _updateAct.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _update.Dispose();
        }

        base.Dispose(disposing);
    }
}
