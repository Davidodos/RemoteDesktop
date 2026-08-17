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
    private readonly Action _openNetwork;

    /// <summary>Der Gerätename. Er steht ganz oben — siehe <see cref="NameCard"/>.</summary>
    private readonly ThemedTextBox _nameBox = new();

    /// <summary>Das Kürzel für den Vollzugriff — siehe <see cref="HotkeyCard"/>.</summary>
    private readonly ThemedTextBox _hotkeyBox = new();

    /// <summary>Ob das Feld gerade auf den nächsten Anschlag wartet.</summary>
    private bool _catchingHotkey;

    public SettingsPage(IAutostartHost autostart, Action openSetup, Action openNetwork)
        : base("Einstellungen", "Start, Aktualisierung und was sonst selten gebraucht wird.")
    {
        _autostart = autostart;
        _openSetup = openSetup;
        _openNetwork = openNetwork;

        // Kein Anfangszustand mit Fassungsnummer: die steht in „Über" darunter.
        // Hier steht, was die letzte Suche ergeben hat — und vor der ersten,
        // dass es noch keine gab.
        _updateState = new TextBlock("Noch nicht nachgesehen.");

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

        Body.Add(NameCard());
        Body.Add(HotkeyCard());
        Body.Add(AutostartCard());
        Body.Add(NetworkCard());
        Body.Add(SetupCard());
        Body.Add(UpdateCard());
        Body.Add(AboutCard());
    }

    /// <summary>Was gerade eingestellt ist, ohne dabei etwas auszulösen.</summary>
    public override Task RefreshAsync()
    {
        Show(Autostart.Read(_autostart));

        // Nicht überschreiben, während jemand tippt — sonst rutscht der
        // Zeiger bei jedem Takt an den Anfang zurück.
        if (!_nameBox.Focused)
        {
            _nameBox.Value = AgentData.DeviceName();
        }

        // Nicht, während das Feld auf den nächsten Anschlag wartet: „Jetzt
        // drücken…" wäre sonst beim ersten Nachsehen wieder weg.
        if (!_catchingHotkey)
        {
            _hotkeyBox.Value = Beschreibe(AgentData.Hotkey());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Wie dieser Rechner heißt.
    ///
    /// <para>
    /// Er steht ganz oben, weil er das Einzige ist, was andere Geräte von diesem
    /// Rechner je zu sehen bekommen. Vergeben wird er im ersten Schritt der
    /// Einrichtung; hier ist der Weg, ihn danach zu ändern — er wirkt sofort,
    /// auch bei laufendem Agent.
    /// </para>
    /// </summary>
    private Card NameCard()
    {
        var card = new Card("Name dieses Rechners");
        var save = new ThemedButton("Übernehmen", ButtonTone.Primary);

        _nameBox.MaxLength = DeviceNameFile.MaxLength;

        save.Click += (_, _) =>
        {
            if (DeviceNameFile.Sanitize(_nameBox.Value) is not { } chosen)
            {
                Report("Ohne Namen geht es nicht.", Tone.Bad);

                return;
            }

            try
            {
                AgentData.SetDeviceName(chosen);
                _nameBox.Value = chosen;
                Report($"Dieser Rechner heißt jetzt „{chosen}“.", Tone.Good);
            }
            catch (Exception failure)
            {
                Report($"Nicht gespeichert: {failure.Message}", Tone.Bad);
            }
        };

        card.Body.Add(new TextBlock("So steht er in den Listen der gekoppelten Geräte."));
        card.Body.Add(Row.Fill(_nameBox, save));

        return card;
    }

    /// <summary>
    /// Das Kürzel für den Vollzugriff auf einen anderen Rechner.
    ///
    /// <para>
    /// Vergeben wird es beim ersten Verbinden, in der Fernsteuerung selbst —
    /// dort sieht man, welche Taste man wirklich unter dem Finger hat. Hier
    /// steht, was daraus geworden ist, und hier ist der Weg, es zu ändern.
    /// </para>
    ///
    /// <para>
    /// Geändert wird durch Drücken und nicht durch Tippen: <c>ctrl+alt+KeyK</c>
    /// ist keine Schreibweise, die jemand kennen muss. Deshalb ist das Feld
    /// gesperrt und der Knopf daneben schaltet das Zuhören ein.
    /// </para>
    /// </summary>
    private Card HotkeyCard()
    {
        var card = new Card("Vollzugriff auf einen anderen Rechner");
        var change = new ThemedButton("Kürzel ändern");

        _hotkeyBox.ReadOnly = true;
        _hotkeyBox.Value = Beschreibe(AgentData.Hotkey());

        change.Click += (_, _) =>
        {
            _catchingHotkey = !_catchingHotkey;

            if (_catchingHotkey)
            {
                change.Relabel("Abbrechen");
                _hotkeyBox.Value = "Jetzt drücken…";
                _hotkeyBox.FocusInput();
                Report("Die gewünschte Tastenkombination drücken.", Tone.Working);

                return;
            }

            change.Relabel("Kürzel ändern");
            _hotkeyBox.Value = Beschreibe(AgentData.Hotkey());
            Report("Unverändert.");
        };

        _hotkeyBox.KeyPressed += (_, pressed) =>
        {
            if (!_catchingHotkey)
            {
                return;
            }

            // Nichts von dem hier soll nebenbei etwas auslösen — auch nicht
            // die Tabulatortaste, die sonst den Fokus weiterreicht.
            pressed.SuppressKeyPress = true;
            pressed.Handled = true;

            // Nur Modifier: da greift jemand noch. Kein Fehler, keine Meldung.
            if (HotkeyKeys.From(pressed) is not { } combination)
            {
                return;
            }

            try
            {
                AgentData.SetHotkey(HotkeyKeys.Serialize(combination));
                _catchingHotkey = false;
                change.Relabel("Kürzel ändern");
                _hotkeyBox.Value = HotkeyKeys.Describe(combination);
                Report($"Der Vollzugriff schaltet jetzt mit {_hotkeyBox.Value}.", Tone.Good);
            }
            catch (Exception failure)
            {
                Report($"Nicht gespeichert: {failure.Message}", Tone.Bad);
            }
        };

        card.Body.Add(new TextBlock(
            "Während des Vollzugriffs gehen Maus und Tastatur vollständig auf den anderen "
            + "Rechner. Dieses Kürzel schaltet ihn ein und wieder aus — es ist das Einzige, "
            + "was hier bleibt."));

        card.Body.Add(Row.Fill(_hotkeyBox, change));

        return card;
    }

    /// <summary>Was in dem Feld steht, solange niemand daran dreht.</summary>
    private static string Beschreibe(string? stored) =>
        HotkeyKeys.Parse(stored) is { } combination
            ? HotkeyKeys.Describe(combination)
            : "Noch keins — wird beim ersten Verbinden vergeben.";

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
    /// <summary>
    /// Der Weg zum Netz — seit 31g von hier aus und nicht mehr aus der Leiste.
    ///
    /// Ein Netzmodus wird einmal eingestellt und danach nie wieder angefasst.
    /// Neben etwas zu stehen, das man täglich benutzt, macht ihn nicht
    /// zugänglicher, sondern nur auffälliger, als er sein sollte.
    /// </summary>
    private Card NetworkCard()
    {
        var card = new Card("Netz");
        var open = new ThemedButton("Netz öffnen");

        open.Click += (_, _) => _openNetwork();

        card.Body.Add(new TextBlock(
            "Adresse und Netzmodus — Heimnetz, Tailscale, Headscale oder eigenes VPN."));

        card.Body.Add(Row.Buttons(open));

        return card;
    }

    private Card SetupCard()
    {
        var card = new Card("Einrichtung");
        var again = new ThemedButton("Einrichtung erneut durchführen");

        again.Click += (_, _) => _openSetup();

        card.Body.Add(new TextBlock(
            "Agent nachrüsten, Netzmodus wechseln, Zertifikat neu holen — derselbe "
            + "Weg wie beim ersten Start."));

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

        // Kein „Aktualisiert wird über den Installer" — das ist eine Auskunft
        // über die Bauart und keine über diesen Rechner. Und keine
        // Fassungsnummer: sie steht in der Karte direkt darunter.
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
                    _updateState.Retext("Alles aktuell.");
                    _updateAct.Relabel("Nach Updates suchen");
                    Report("Alles aktuell.", Tone.Good);

                    return;
                }

                _updateState.Retext($"Update verfügbar: {_offer.Version}.");
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
