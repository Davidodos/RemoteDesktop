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

    private readonly IAutostartHost _autostart;
    private readonly ClientUpdate _update = new();

    private readonly ChoiceGroup<AutostartMode> _modes = new();
    private readonly TextBlock _updateState;
    private readonly ThemedButton _updateAct = new("Nach Updates suchen", ButtonTone.Primary);

    private ReleaseOffer? _offer;

    public SettingsPage(IAutostartHost autostart)
        : base("Einstellungen", "Start, Aktualisierung und was sonst selten gebraucht wird.")
    {
        _autostart = autostart;

        _updateState = new TextBlock($"Fassung {ClientUpdate.InstalledVersion()}");

        foreach (var mode in new[]
                 {
                     AutostartMode.Both, AutostartMode.Agent,
                     AutostartMode.Client, AutostartMode.None
                 })
        {
            _modes.Add(mode, mode.Describe(), Explain(mode));
        }

        _modes.Chosen += Apply;
        _updateAct.Click += async (_, _) => await UpdateStepAsync();

        Body.Add(AutostartCard());
        Body.Add(UpdateCard());
        Body.Add(AboutCard());
    }

    /// <summary>Was gerade eingestellt ist, ohne dabei etwas auszulösen.</summary>
    public override Task RefreshAsync()
    {
        _modes.Select(Autostart.Read(_autostart));

        return Task.CompletedTask;
    }

    private static string Explain(AutostartMode mode) => mode switch
    {
        AutostartMode.Both =>
            "Der Rechner ist erreichbar, sobald er an ist, und das Fenster wartet im "
            + "Infobereich.",

        AutostartMode.Agent =>
            "Der Rechner ist erreichbar, sobald er an ist. Das Fenster startest du "
            + "selbst, wenn du es brauchst.",

        AutostartMode.Client =>
            "Nur das Fenster. Den Agent musst du dann von Hand starten — von hier aus "
            + "oder aus dem Infobereich.",

        _ => "Nichts startet von allein."
    };

    private Card AutostartCard()
    {
        var card = new Card("Beim Anmelden starten");

        card.Body.Add(_modes);

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

        card.Body.Add(Row.Buttons(project));

        return card;
    }

    private void Apply(AutostartMode mode)
    {
        try
        {
            Autostart.Apply(_autostart, mode);
            Report("Gespeichert.", Tone.Good);
        }
        catch (Exception failure)
        {
            // Den Starttyp eines Dienstes zu ändern verlangt Adminrechte. Ohne
            // sie bleibt die Einstellung, wie sie war — das muss dastehen, sonst
            // glaubt jemand, er habe etwas eingestellt.
            Report($"Nicht gespeichert: {failure.Message}", Tone.Bad);
            _modes.Select(Autostart.Read(_autostart));
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
