using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Die Einrichtung: vier Fragen, dann läuft es.
///
/// <para>
/// **Der Befund dahinter:** bis v1.2.0 entschied der Installer, was auf diesem
/// Rechner passiert — Häkchen für den Dienst, Häkchen für den Autostart, und
/// gestartet wurde der Agent gleich mit. Wer beim Klicken durch den Installer
/// noch gar nicht wusste, ob er den Rechner fernsteuerbar machen will, hatte ihn
/// danach trotzdem laufen. Und wer es sich anders überlegte, musste den
/// Installer wiederfinden.
/// </para>
///
/// <para>
/// Jetzt legt der Installer nur Dateien ab. Was davon *aktiv* wird, wird hier
/// entschieden, in dieser Reihenfolge: erst was dieser Rechner können soll, dann
/// auf welchem Weg er erreichbar ist, dann was beim Hochfahren mitkommt. Der
/// Agent startet ganz zum Schluss — vorher wüsste er nicht, unter welchem Namen
/// er sich ausweisen soll.
/// </para>
///
/// <para>
/// Sie ist jederzeit erneut aufrufbar. Ein Assistent, den es nur einmal gibt,
/// ist eine Falle: genau die Frage, die man beim ersten Mal falsch beantwortet,
/// ist danach die unerreichbare.
/// </para>
/// </summary>
public sealed class SetupPage : PageView
{
    private enum Step
    {
        Parts,
        Network,
        Autostart,
        Finish
    }

    private readonly WindowsProbe _probe;
    private readonly IAutostartHost _autostartHost;
    private readonly Func<Task> _finished;

    private Step _step = Step.Parts;
    private bool _withAgent = true;
    private NetworkKind _kind = NetworkKind.Lan;
    private string _address = string.Empty;
    private string _coordinator = Coordinator.Default.Address;
    private AutostartMode _autostart = AutostartMode.Both;
    private bool _busy;

    /// <summary>
    /// Die Felder des gerade sichtbaren Schrittes. Sie werden bei jedem
    /// Zeichnen neu gebaut — <see cref="Stack.Clear"/> entsorgt seinen Inhalt,
    /// und ein Feld, das das überlebt hätte, wäre danach ein Steuerelement ohne
    /// Fenster. Was jemand hineingetippt hat, wird deshalb vorher abgeholt.
    /// </summary>
    private ThemedTextBox? _addressBox;
    private ThemedTextBox? _coordinatorBox;

    public SetupPage(WindowsProbe probe, IAutostartHost autostart, Func<Task> finished)
        : base("Einrichtung", "Vier Fragen, dann ist dieser Rechner fertig eingerichtet.")
    {
        _probe = probe;
        _autostartHost = autostart;
        _finished = finished;
    }

    /// <summary>
    /// Beim Betreten den Stand von der Platte übernehmen — aber nur, wenn nicht
    /// gerade jemand mitten im Assistenten steht.
    /// </summary>
    public override Task RefreshAsync()
    {
        if (_step == Step.Parts)
        {
            var profile = NetworkStore.Read();

            _kind = profile.Kind;
            _address = profile.Address;
            _coordinator = profile.Coordinator.Address;
            // Auch der alte Dienst zählt: wer aktualisiert, hat den Agent
            // gewollt und soll ihn nicht abwählen müssen, um ihn zu behalten.
            _withAgent = AgentService.Installed || AgentService.LegacyService || !SetupState.Done;
            _autostart = Autostart.Read(_autostartHost);
        }

        Draw();

        return Task.CompletedTask;
    }

    /// <summary>Von vorn — der Weg, über den man die Einrichtung erneut startet.</summary>
    public void Restart()
    {
        _step = Step.Parts;
        _addressBox = null;
        _coordinatorBox = null;
    }

    // ---- Zeichnen ----------------------------------------------------------

    private void Draw()
    {
        Remember();

        Body.Clear();
        _addressBox = null;
        _coordinatorBox = null;

        Body.Add(ProgressCard());

        switch (_step)
        {
            case Step.Parts:
                Body.Add(PartsCard());
                break;

            case Step.Network:
                Body.Add(NetworkCard());
                break;

            case Step.Autostart:
                Body.Add(AutostartCard());
                break;

            default:
                Body.Add(FinishCard());
                break;
        }
    }

    /// <summary>Was jemand getippt hat, bevor die Felder verschwinden.</summary>
    private void Remember()
    {
        if (_addressBox is { IsDisposed: false })
        {
            _address = _addressBox.Value;
        }

        if (_coordinatorBox is { IsDisposed: false })
        {
            _coordinator = _coordinatorBox.Value;
        }
    }

    private Card ProgressCard()
    {
        var card = new Card($"Schritt {(int)_step + 1} von 4");

        card.Body.Add(new TextBlock(_step switch
        {
            Step.Parts => "Was dieser Rechner können soll.",
            Step.Network => "Auf welchem Weg dein Handy ihn findet.",
            Step.Autostart => "Was beim Hochfahren von allein mitkommt.",
            _ => "Nachsehen und abschließen."
        }));

        return card;
    }

    private Card PartsCard()
    {
        var card = new Card("Was soll dieser Rechner können?");
        var choice = new ChoiceGroup<bool>();

        choice.Add(
            true,
            "Steuern und gesteuert werden",
            "Der Agent wird eingerichtet: du kannst diesen Rechner vom Handy aus "
            + "bedienen, und von hier aus andere.");

        choice.Add(
            false,
            "Nur andere steuern",
            "Kein Agent, kein Dienst, nichts, was lauscht. Dieser Rechner bleibt für "
            + "das Handy unsichtbar.");

        choice.Select(_withAgent);
        choice.Chosen += value => _withAgent = value;

        card.Body.Add(choice);
        card.Body.Add(Navigation(back: null, next: () => Go(Step.Network)));

        return card;
    }

    /// <summary>
    /// Der Netzschritt zeigt **nur**, was zum gewählten Modus gehört. Gesperrte
    /// Felder waren die schlechtere Antwort: sie sehen aus wie etwas, das man
    /// gleich ausfüllen muss, und ließen die Frage offen, warum es nicht geht.
    /// </summary>
    private Card NetworkCard()
    {
        var card = new Card("Wie findet dein Handy diesen Rechner?");
        var kinds = new ChoiceGroup<NetworkKind>();

        kinds.Add(
            NetworkKind.Lan,
            "Nur zuhause",
            "Handy und Rechner hängen am selben Router. Nichts zu installieren.");

        kinds.Add(
            NetworkKind.Tailscale,
            "Tailscale",
            "Auch von unterwegs. Braucht das Programm Tailscale auf beiden Seiten.");

        kinds.Add(
            NetworkKind.Vpn,
            "Eigenes VPN",
            "Du hast schon eins — WireGuard auf der Fritzbox oder etwas Ähnliches.");

        kinds.Select(_kind);

        kinds.Chosen += kind =>
        {
            Remember();
            _kind = kind;
            Draw();
        };

        card.Body.Add(kinds);

        if (_kind == NetworkKind.Tailscale)
        {
            AddTailscale(card);
        }

        AddAddress(card);

        card.Body.Add(Navigation(
            back: () => Go(Step.Parts),
            next: () =>
            {
                Remember();

                if (Profile().Rejection is { } rejection)
                {
                    Report(rejection, Tone.Bad);

                    return;
                }

                Go(Step.Autostart);
            }));

        return card;
    }

    /// <summary>
    /// Die fremden Schritte: Tailscale installieren, anmelden, Zertifikat holen.
    /// RemoteDesktop stößt sie an und prüft danach, was daraus geworden ist —
    /// mehr ist bei einem fremden Programm nicht ehrlich möglich.
    /// </summary>
    private void AddTailscale(Card card)
    {
        var installed = _probe.HasTailscale;
        var connected = installed && _probe.IsConnected;

        card.Body.Add(new TextBlock(
            !installed
                ? "Tailscale ist auf diesem Rechner noch nicht installiert."
                : !connected
                    ? "Tailscale ist installiert, dieser Rechner ist aber noch nicht angemeldet."
                    : $"Angemeldet als {_probe.TailnetName}."
                      + (_probe.HasCertificate
                          ? " Das Zertifikat liegt bereit."
                          : " Ohne Zertifikat von Tailscale muss jedes Handy die Stelle "
                            + "einmal bestätigen — der QR-Code bringt sie mit."),
            Theme.Body,
            connected ? Theme.Text : Theme.TextDim));

        var buttons = new List<Control>();

        if (!installed)
        {
            var download = new ThemedButton("Tailscale herunterladen", ButtonTone.Primary);

            download.Click += (_, _) =>
            {
                OverviewPage.Open(Tailscale.Download);
                Report("Tailscale öffnet sich im Browser. Danach hier auf „Neu prüfen“.");
            };

            buttons.Add(download);
        }
        else if (!connected)
        {
            var signIn = new ThemedButton("Jetzt anmelden", ButtonTone.Primary);

            signIn.Click += async (_, _) => await StepAsync(
                "Tailscale meldet diesen Rechner an…",
                () => ProcessRunner.Run(
                    Tailscale.Executable,
                    new NetworkProfile(_kind, _address, Coordinator.From(_coordinator))
                        .Coordinator.UpArguments(),
                    TimeSpan.FromMinutes(3)));

            buttons.Add(signIn);
        }
        else if (!_probe.HasCertificate)
        {
            var certificate = new ThemedButton("Zertifikat holen");

            certificate.Click += async (_, _) => await StepAsync(
                "Das Zertifikat wird geholt…",
                () => Elevation.Run(AdminTask.FetchCertificate, _probe.TailnetName));

            buttons.Add(certificate);
        }

        var recheck = new ThemedButton("Neu prüfen");

        recheck.Click += (_, _) =>
        {
            _probe.Forget();

            // Der Name im Tailnet ist genau das, was gleich in den QR-Code geht.
            // Steht er noch nicht da, kommt er hier von allein hinein.
            if (_address.Trim().Length == 0)
            {
                _address = _probe.TailnetName;
            }

            Draw();
        };

        buttons.Add(recheck);

        card.Body.Add(Row.Buttons([.. buttons]));

        card.Body.Add(new TextBlock(
            "Eigener Koordinator (Headscale) — nur, wenn du Tailscale nicht über deren "
            + "Server betreibst.", Theme.Body, Theme.TextDim));

        _coordinatorBox = new ThemedTextBox(Coordinator.Default.Address)
        {
            Value = _coordinator
        };

        card.Body.Add(_coordinatorBox);
    }

    private void AddAddress(Card card)
    {
        card.Body.Add(new TextBlock(_kind switch
        {
            NetworkKind.Tailscale =>
                "Der Name dieses Rechners im Tailnet. Genau er steht später im QR-Code, "
                + "und genau ihn muss das Handy auflösen können.",
            NetworkKind.Vpn =>
                "Die Adresse, unter der dieser Rechner in deinem VPN erreichbar ist.",
            _ =>
                "Die Adresse, unter der dieser Rechner im Heimnetz erreichbar ist. "
                + "Meistens steht sie schon da — du musst sie nur bestätigen."
        }));

        _addressBox = new ThemedTextBox(
            _kind == NetworkKind.Tailscale ? "z. B. pc.tailnet-1234.ts.net" : "z. B. 192.168.178.33")
        {
            Value = _address
        };

        if (_kind == NetworkKind.Vpn)
        {
            card.Body.Add(_addressBox);

            return;
        }

        var suggest = new ThemedButton("Vorschlag");

        suggest.Click += async (_, _) =>
        {
            var found = _kind == NetworkKind.Tailscale
                ? await Task.Run(() =>
                {
                    _probe.Forget();

                    return _probe.TailnetName;
                })
                : NetworkStore.Guess() ?? string.Empty;

            if (found.Length == 0)
            {
                Report(
                    _kind == NetworkKind.Tailscale
                        ? "Tailscale meldet für diesen Rechner keinen Namen — läuft es, und "
                          + "ist dieser Rechner angemeldet?"
                        : "Hier ist gerade keine Netzwerkverbindung zu finden.",
                    Tone.Bad);

                return;
            }

            _addressBox.Value = found;
            Report($"Gefunden: {found}.", Tone.Good);
        };

        card.Body.Add(Row.Fill(_addressBox, suggest));
    }

    private Card AutostartCard()
    {
        var card = new Card("Was startet mit Windows?");
        var modes = new ChoiceGroup<AutostartMode>();

        // Ohne Agent gibt es nichts zu starten, was lauscht. Die beiden Modi mit
        // Agent stünden dann als Wahl da, die keine ist.
        var offered = _withAgent
            ? new[] { AutostartMode.Both, AutostartMode.Agent, AutostartMode.Client, AutostartMode.None }
            : [AutostartMode.Client, AutostartMode.None];

        foreach (var mode in offered)
        {
            modes.Add(mode, mode.Describe(), Explain(mode));
        }

        if (!_withAgent && _autostart.Starts(AutostartMode.Agent))
        {
            _autostart = _autostart.Without(AutostartMode.Agent);
        }

        modes.Select(_autostart);
        modes.Chosen += mode => _autostart = mode;

        card.Body.Add(modes);
        card.Body.Add(Navigation(back: () => Go(Step.Network), next: () => Go(Step.Finish)));

        return card;
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
            "Nur das Fenster. Den Agent startest du dann von Hand — hier oder aus dem "
            + "Infobereich.",

        _ => "Nichts startet von allein."
    };

    private Card FinishCard()
    {
        var profile = Profile();
        var card = new Card("Nachsehen und abschließen");

        card.Body.Add(new TextBlock(
            $"Agent: {(_withAgent ? "wird eingerichtet und gestartet" : "wird nicht eingerichtet")}\n"
            + $"Netz: {profile.Describe()}\n"
            + $"Beim Hochfahren: {_autostart.Describe()}",
            Theme.Body,
            Theme.Text));

        if (_withAgent)
        {
            card.Body.Add(new TextBlock(
                "Windows fragt gleich einmal nach Administratorrechten — für den "
                + "Eintrag in die Aufgabenplanung. Danach nicht mehr.\n"
                + "Der Agent läuft in deiner Sitzung und startet mit deiner Anmeldung; "
                + "ohne angemeldeten Benutzer ist dieser Rechner nicht erreichbar."));
        }

        var back = new ThemedButton("Zurück");
        var finish = new ThemedButton("Einrichtung abschließen", ButtonTone.Primary);

        back.Click += (_, _) => Go(Step.Autostart);
        finish.Click += async (_, _) => await CompleteAsync();

        card.Body.Add(Row.Buttons(back, finish));

        return card;
    }

    private Row Navigation(Action? back, Action next)
    {
        var buttons = new List<Control>();

        if (back is not null)
        {
            var previous = new ThemedButton("Zurück");

            previous.Click += (_, _) => back();
            buttons.Add(previous);
        }

        var forward = new ThemedButton("Weiter", ButtonTone.Primary);

        forward.Click += (_, _) => next();
        buttons.Add(forward);

        return Row.Buttons([.. buttons]);
    }

    private void Go(Step step)
    {
        Remember();
        _step = step;
        Draw();
    }

    private NetworkProfile Profile() =>
        new(_kind, _address, Coordinator.From(_coordinator));

    // ---- Ausführen ---------------------------------------------------------

    /// <summary>Ein einzelner Handgriff mit Rückmeldung, ohne das Fenster anzuhalten.</summary>
    private async Task StepAsync(string what, Func<RunResult> work)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Report(what, Tone.Working);

        try
        {
            var result = await Task.Run(work);

            Report(result.Message, result.Ok ? Tone.Good : Tone.Bad);
        }
        catch (Exception failure)
        {
            Report(failure.Message, Tone.Bad);
        }
        finally
        {
            _busy = false;
            _probe.Forget();
            Draw();
        }
    }

    /// <summary>
    /// Der Abschluss: alles Erhöhte in einem Auftrag, danach der Autostart des
    /// Fensters — der gehört dem angemeldeten Benutzer und braucht keine Rechte.
    /// </summary>
    private async Task CompleteAsync()
    {
        if (_busy)
        {
            return;
        }

        Remember();

        var profile = Profile().Normalized();

        if (profile.Rejection is { } rejection)
        {
            Report(rejection, Tone.Bad);
            Go(Step.Network);

            return;
        }

        _busy = true;
        Report("Einen Moment…", Tone.Working);

        var request = new SetupRequest(
            profile,
            !_withAgent
                ? AgentSetup.None
                : _autostart.Starts(AutostartMode.Agent)
                    ? AgentSetup.Automatic
                    : AgentSetup.Manual,
            // In wessen Sitzung der Agent laufen soll. Der erhöhte Aufruf kann
            // das nicht wissen — er läuft womöglich unter einem anderen Konto.
            AgentService.InteractiveUser,
            // Wenn Tailscale steht und noch kein Zertifikat daliegt, wird es
            // gleich mitgeholt. Sonst stellt sich der Agent selbst eins aus,
            // und jedes Handy müsste eine Zertifizierungsstelle bestätigen —
            // der Schritt, an dem am echten Gerät alles hing.
            Certificate: _kind == NetworkKind.Tailscale
                         && _probe.IsConnected
                         && !_probe.HasCertificate);

        var prepared = Path.Combine(
            Path.GetTempPath(), $"remotedesktop-setup-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(prepared, request.Write());

            var result = await Task.Run(() => Elevation.Run(AdminTask.Complete, prepared));

            if (!result.Ok)
            {
                Report(result.Message, Tone.Bad);

                return;
            }

            // Der Eintrag des Fensters hängt am angemeldeten Benutzer, nicht am
            // Rechner — deshalb hier und nicht im erhöhten Auftrag.
            _autostartHost.SetClientEntry(_autostart.Starts(AutostartMode.Client));

            Report(result.Message, Tone.Good);

            _step = Step.Parts;

            await _finished();
        }
        catch (Exception failure)
        {
            Report(failure.Message, Tone.Bad);
        }
        finally
        {
            _busy = false;

            try
            {
                File.Delete(prepared);
            }
            catch (IOException)
            {
                // Eine Datei im Temp-Ordner, die liegen bleibt, ist kein Grund,
                // dem Nutzer etwas zu melden.
            }
        }
    }
}

/// <summary>
/// Ob dieser Rechner schon einmal eingerichtet wurde.
///
/// Gefragt wird die Datei, die die Einrichtung schreibt — kein eigener Merker.
/// Ein zweiter Zustand daneben wäre einer, der irgendwann abweicht.
/// </summary>
public static class SetupState
{
    public static bool Done => NetworkStore.Exists;
}
