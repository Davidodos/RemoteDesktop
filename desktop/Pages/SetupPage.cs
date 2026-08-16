using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Die Einrichtung: eine Frage je Schritt, und kein Schritt ohne Antwort.
///
/// <para>
/// **Der Befund dahinter:** bis v1.2.0 entschied der Installer, was auf diesem
/// Rechner passiert — Häkchen für den Dienst, Häkchen für den Autostart, und
/// gestartet wurde der Agent gleich mit. Wer beim Klicken durch den Installer
/// noch gar nicht wusste, ob er den Rechner fernsteuerbar machen will, hatte ihn
/// danach trotzdem laufen.
/// </para>
///
/// <para>
/// **Der zweite Befund, und der teurere:** bis v1.3.0 stand die Netzfrage in
/// *einem* Schritt — Modus wählen, Tailscale einrichten, Adresse eintippen,
/// Zertifikat holen, alles auf einer Karte, und „Weiter" ging immer. Am echten
/// Gerät kam man damit bis zum Ende durch, las „Einrichtung abgeschlossen" und
/// bekam auf dem Handy trotzdem die Rückfrage, der Rechner habe sich sein
/// Zertifikat selbst ausgestellt. Denn nichts davon war Pflicht gewesen.
/// </para>
///
/// <para>
/// Jetzt wird erst der Weg gewählt und dann — im nächsten Schritt und für sich —
/// eingerichtet, was genau dieser Weg braucht. „Weiter" bleibt gesperrt, solange
/// etwas davon fehlt, und daneben steht, was. Ein Assistent, den man mit einer
/// halben Einrichtung verlassen kann, verschiebt den Fehlschlag nur dorthin, wo
/// er niemandem mehr etwas erklärt.
/// </para>
///
/// <para>
/// Sie ist jederzeit erneut aufrufbar — über die Einstellungen. In der
/// Seitenleiste steht sie nicht: sie ist kein Ort, an dem man sich aufhält,
/// sondern ein Weg, den man einmal geht.
/// </para>
/// </summary>
public sealed class SetupPage : PageView
{
    private enum Step
    {
        /// <summary>Wie dieser Rechner heißt — der Name, den fremde Listen zeigen.</summary>
        Name,

        /// <summary>Was dieser Rechner können soll.</summary>
        Parts,

        /// <summary>Auf welchem Weg — und **nur** das.</summary>
        Kind,

        /// <summary>Was dieser eine Weg braucht.</summary>
        Details,

        /// <summary>Startet RemoteDesktop mit Windows?</summary>
        Windows,

        /// <summary>Und der Agent auch? Nur, wenn das Vorige ein Ja war.</summary>
        AgentStart,

        /// <summary>Nachsehen und abschließen.</summary>
        Summary
    }

    private readonly WindowsProbe _probe;
    private readonly IAutostartHost _autostartHost;
    private readonly Func<Task> _finished;

    private Step _step = Step.Name;
    private string _deviceName = string.Empty;
    private bool _withAgent = true;
    private NetworkKind _kind = NetworkKind.Lan;
    private string _address = string.Empty;
    private string _coordinator = string.Empty;
    private bool _withWindows = true;
    private bool _agentWithWindows = true;
    private bool _busy;

    /// <summary>
    /// Die Felder des gerade sichtbaren Schrittes. Sie werden bei jedem
    /// Zeichnen neu gebaut — <see cref="Stack.Clear"/> entsorgt seinen Inhalt,
    /// und ein Feld, das das überlebt hätte, wäre danach ein Steuerelement ohne
    /// Fenster. Was jemand hineingetippt hat, wird deshalb vorher abgeholt.
    /// </summary>
    private ThemedTextBox? _addressBox;
    private ThemedTextBox? _coordinatorBox;
    private ThemedTextBox? _nameBox;

    /// <summary>
    /// Der „Weiter"-Knopf des Detailschritts und die Zeile darüber, die sagt,
    /// warum er noch aus ist. Beide werden bei jeder Eingabe aufgefrischt, ohne
    /// die Karte neu zu bauen — sonst verlöre das Feld bei jedem Zeichen den
    /// Fokus.
    /// </summary>
    private ThemedButton? _forward;
    private TextBlock? _blocker;

    /// <summary>
    /// Ob Tailscale läuft und angemeldet ist — beim Zeichnen einmal erfragt.
    ///
    /// <para>
    /// Die Auskunft kostet einen Prozessstart (<c>tailscale status --json</c>),
    /// und geprüft wird bei jedem getippten Zeichen, weil davon abhängt, ob
    /// „Weiter" angeht. Live abgefragt hinge das Fenster beim Tippen. Neu
    /// erfragt wird sie da, wo sie sich ändern kann: nach „Neu prüfen", nach
    /// „Jetzt anmelden", beim Betreten der Seite.
    /// </para>
    /// </summary>
    private bool _tailscaleInstalled;
    private bool _tailscaleConnected;

    public SetupPage(WindowsProbe probe, IAutostartHost autostart, Func<Task> finished)
        : base("Einrichtung", "Ein paar Fragen, dann ist dieser Rechner fertig eingerichtet.")
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
        if (_step == Step.Name)
        {
            _deviceName = AgentData.DeviceName();

            var profile = NetworkStore.Read();
            var autostart = Autostart.Read(_autostartHost);

            _kind = profile.Kind;
            _address = profile.Address;
            _coordinator = profile.Kind == NetworkKind.Headscale
                ? profile.Coordinator.Address
                : string.Empty;

            // Auch der alte Dienst zählt: wer aktualisiert, hat den Agent
            // gewollt und soll ihn nicht abwählen müssen, um ihn zu behalten.
            _withAgent = AgentService.Installed || AgentService.LegacyService || !SetupState.Done;

            _withWindows = autostart.WithWindows();
            _agentWithWindows = autostart.Starts(AutostartMode.Agent);
        }

        Draw();

        return Task.CompletedTask;
    }

    /// <summary>Von vorn — der Weg, über den man die Einrichtung erneut startet.</summary>
    public void Restart()
    {
        _step = Step.Name;
        Forget();
    }

    /// <summary>
    /// Welche Schritte es diesmal gibt. Die Liste hängt an den Antworten: wer
    /// nichts mit Windows starten lässt, bekommt die Anschlussfrage nicht
    /// gestellt, und wer keinen Agent einrichtet, erst recht nicht.
    /// </summary>
    private List<Step> Steps()
    {
        var steps = new List<Step>
        {
            Step.Name, Step.Parts, Step.Kind, Step.Details, Step.Windows
        };

        if (_withWindows && _withAgent)
        {
            steps.Add(Step.AgentStart);
        }

        steps.Add(Step.Summary);

        return steps;
    }

    // ---- Zeichnen ----------------------------------------------------------

    private void Draw()
    {
        Remember();

        Body.Clear();
        Forget();

        Body.Add(ProgressCard());

        Body.Add(_step switch
        {
            Step.Name => NameCard(),
            Step.Parts => PartsCard(),
            Step.Kind => KindCard(),
            Step.Details => DetailsCard(),
            Step.Windows => WindowsCard(),
            Step.AgentStart => AgentStartCard(),
            _ => SummaryCard()
        });
    }

    /// <summary>Was jemand getippt hat, bevor die Felder verschwinden.</summary>
    private void Remember()
    {
        if (_nameBox is { IsDisposed: false })
        {
            _deviceName = _nameBox.Value;
        }

        if (_addressBox is { IsDisposed: false })
        {
            _address = _addressBox.Value;
        }

        if (_coordinatorBox is { IsDisposed: false })
        {
            _coordinator = _coordinatorBox.Value;
        }
    }

    /// <summary>Die Verweise auf entsorgte Steuerelemente fallen lassen.</summary>
    private void Forget()
    {
        _addressBox = null;
        _coordinatorBox = null;
        _nameBox = null;
        _forward = null;
        _blocker = null;
    }

    private Card ProgressCard()
    {
        var steps = Steps();
        var card = new Card($"Schritt {steps.IndexOf(_step) + 1} von {steps.Count}");

        card.Body.Add(new TextBlock(_step switch
        {
            Step.Name => "Wie dieser Rechner heißt.",
            Step.Parts => "Was dieser Rechner können soll.",
            Step.Kind => "Auf welchem Weg dein Handy ihn findet.",
            Step.Details => $"Was für „{Profile().Name()}“ nötig ist.",
            Step.Windows => "Ob RemoteDesktop mit Windows startet.",
            Step.AgentStart => "Ob der Agent dabei mitkommt.",
            _ => "Nachsehen und abschließen."
        }));

        return card;
    }

    /// <summary>
    /// Der Name dieses Rechners — der erste Schritt, weil er der einzige ist,
    /// den andere Geräte je zu sehen bekommen.
    ///
    /// <para>
    /// **Der Befund dahinter:** es gab ihn nicht. Wer koppelte, tippte jedes Mal
    /// neu ein, wie dieser Rechner drüben heißen soll; wer nur seinen Code
    /// vorzeigte, hieß drüben <c>DESKTOP-4F2K9L1</c>. Jetzt steht er einmal in
    /// <c>{app}\data\devicename.txt</c> und geht bei jeder Kopplung von allein
    /// mit — siehe <see cref="DeviceNameFile"/>.
    /// </para>
    /// </summary>
    private Card NameCard()
    {
        var card = new Card("Wie heißt dieser Rechner?");

        card.Body.Add(new TextBlock(
            "So steht er in den Listen der Geräte, mit denen du ihn koppelst."));

        _nameBox = new ThemedTextBox
        {
            Value = _deviceName,
            MaxLength = DeviceNameFile.MaxLength
        };

        card.Body.Add(_nameBox);
        card.Body.Add(Navigation(back: false, next: () =>
        {
            Remember();

            if (DeviceNameFile.Sanitize(_deviceName) is null)
            {
                Report("Ohne Namen geht es nicht.", Tone.Bad);

                return;
            }

            Forward();
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
            "Kein Agent, nichts, was lauscht. Dieser Rechner bleibt für das Handy "
            + "unsichtbar.");

        choice.Select(_withAgent);
        choice.Chosen += value => _withAgent = value;

        card.Body.Add(choice);
        card.Body.Add(Navigation(back: false, next: Forward));

        return card;
    }

    /// <summary>
    /// Der Netzschritt fragt **nur** nach dem Weg. Alles, was dazugehört, kommt
    /// im nächsten Schritt — und dort dann vollständig.
    /// </summary>
    private Card KindCard()
    {
        var card = new Card("Wie findet dein Handy diesen Rechner?");
        var kinds = new ChoiceGroup<NetworkKind>();

        kinds.Add(
            NetworkKind.Lan,
            "Heimnetz",
            "Handy und Rechner hängen am selben Router. Nichts zu installieren, dafür "
            + "geht es von unterwegs nicht.");

        kinds.Add(
            NetworkKind.Tailscale,
            "Tailscale",
            "Auch von unterwegs. Braucht das Programm Tailscale auf beiden Seiten — "
            + "dafür gibt es ein echtes Zertifikat und auf dem Handy nichts zu "
            + "bestätigen.");

        kinds.Add(
            NetworkKind.Headscale,
            "Headscale",
            "Derselbe Tailscale-Client, aber an deinem eigenen Koordinator statt an "
            + "dem der Firma.");

        kinds.Add(
            NetworkKind.Vpn,
            "Anderer VPN-Anbieter",
            "Du hast schon eins — WireGuard auf der Fritzbox, OpenVPN, ZeroTier. "
            + "RemoteDesktop benutzt nur die Adresse, die dort gilt.");

        kinds.Select(_kind);
        kinds.Chosen += kind => _kind = kind;

        card.Body.Add(kinds);
        card.Body.Add(Navigation(back: true, next: Forward));

        return card;
    }

    // ---- Der Detailschritt -------------------------------------------------

    /// <summary>
    /// Was dieser eine Weg braucht — und nichts von den anderen.
    ///
    /// <para>
    /// „Weiter" ist gesperrt, solange etwas fehlt. Das ist der Kern dieses
    /// Umbaus: bei Tailscale muss das Zertifikat wirklich dort liegen und auf
    /// genau die Adresse lauten, die gleich in den QR-Code geht.
    /// </para>
    /// </summary>
    private Card DetailsCard()
    {
        var card = new Card(_kind switch
        {
            NetworkKind.Lan => "Adresse im Heimnetz",
            NetworkKind.Tailscale => "Tailscale einrichten",
            NetworkKind.Headscale => "Headscale einrichten",
            _ => "Adresse in deinem VPN"
        });

        if (_kind == NetworkKind.Headscale)
        {
            AddCoordinator(card);
        }

        if (Profile().NeedsTailscale)
        {
            AddTailscale(card);
        }

        AddAddress(card);

        if (_kind == NetworkKind.Tailscale)
        {
            AddCertificate(card);
        }
        else if (_withAgent)
        {
            card.Body.Add(new TextBlock(
                _kind == NetworkKind.Headscale
                    ? "Zertifikate stellt der Dienst von Tailscale aus; ein Headscale-Server "
                      + "bringt diese Stelle nicht mit. Der Agent stellt sich deshalb selbst "
                      + "eins aus, und dein Handy bestätigt es einmal beim Koppeln — danach "
                      + "nie wieder."
                    : "Für diese Adresse stellt keine öffentliche Stelle ein Zertifikat aus. "
                      + "Der Agent stellt sich deshalb selbst eins aus, und dein Handy "
                      + "bestätigt es einmal beim Koppeln — danach nie wieder.",
                Theme.Body,
                Theme.TextDim));
        }

        _blocker = new TextBlock(string.Empty, Theme.Body, Theme.Warn);
        card.Body.Add(_blocker);

        card.Body.Add(Navigation(back: true, next: Forward));

        UpdateGate();

        return card;
    }

    private void AddCoordinator(Card card)
    {
        card.Body.Add(new TextBlock(
            "Die Adresse deines Headscale-Servers. Genau dorthin meldet sich der "
            + "Tailscale-Client an, statt an den Dienst von Tailscale."));

        _coordinatorBox = new ThemedTextBox("z. B. https://headscale.example.org")
        {
            Value = _coordinator
        };

        _coordinatorBox.ValueChanged += (_, _) =>
        {
            _coordinator = _coordinatorBox.Value;
            UpdateGate();
        };

        card.Body.Add(_coordinatorBox);
    }

    /// <summary>
    /// Die fremden Schritte: Tailscale installieren und anmelden. RemoteDesktop
    /// stößt sie an und prüft danach, was daraus geworden ist — mehr ist bei
    /// einem fremden Programm nicht ehrlich möglich.
    /// </summary>
    private void AddTailscale(Card card)
    {
        _tailscaleInstalled = _probe.HasTailscale;
        _tailscaleConnected = _tailscaleInstalled && _probe.IsConnected;

        var installed = _tailscaleInstalled;
        var connected = _tailscaleConnected;

        card.Body.Add(new TextBlock(
            !installed
                ? "Der Tailscale-Client ist auf diesem Rechner noch nicht installiert."
                : !connected
                    ? "Tailscale ist installiert, dieser Rechner ist aber noch nicht angemeldet."
                    : $"Angemeldet als {_probe.TailnetName}.",
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
                    Profile().Coordinator.UpArguments(),
                    TimeSpan.FromMinutes(3)));

            buttons.Add(signIn);
        }

        var recheck = new ThemedButton("Neu prüfen");

        recheck.Click += (_, _) =>
        {
            _probe.Forget();

            // Der Name im Tailscale-Netz ist genau das, was gleich in den QR-Code geht.
            // Steht er noch nicht da, kommt er hier von allein hinein.
            if (_address.Trim().Length == 0)
            {
                _address = _probe.TailnetName;
            }

            Draw();
        };

        buttons.Add(recheck);

        card.Body.Add(Row.Buttons([.. buttons]));
    }

    private void AddAddress(Card card)
    {
        card.Body.Add(new TextBlock(_kind switch
        {
            NetworkKind.Tailscale or NetworkKind.Headscale =>
                "Der Name dieses Rechners im Tailscale-Netz. Genau er steht später im QR-Code, "
                + "und genau ihn muss das Handy auflösen können.",
            NetworkKind.Vpn =>
                "Die Adresse, unter der dieser Rechner in deinem VPN erreichbar ist.",
            _ =>
                "Die Adresse, unter der dieser Rechner im Heimnetz erreichbar ist. "
                + "Meistens steht sie schon da — du musst sie nur bestätigen."
        }));

        _addressBox = new ThemedTextBox(
            Profile().NeedsTailscale ? "z. B. pc.tailnet-1234.ts.net" : "z. B. 192.168.178.33")
        {
            Value = _address
        };

        _addressBox.ValueChanged += (_, _) =>
        {
            _address = _addressBox.Value;
            UpdateGate();
        };

        if (_kind == NetworkKind.Vpn)
        {
            card.Body.Add(_addressBox);

            return;
        }

        var suggest = new ThemedButton("Vorschlag");

        suggest.Click += async (_, _) =>
        {
            var found = Profile().NeedsTailscale
                ? await Task.Run(() =>
                {
                    _probe.Forget();

                    return _probe.TailnetName;
                })
                : NetworkStore.Guess() ?? string.Empty;

            if (found.Length == 0)
            {
                Report(
                    Profile().NeedsTailscale
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

    /// <summary>
    /// Das Zertifikat von Tailscale — der Schritt, an dem am echten Gerät alles
    /// hing, und der einzige in diesem Assistenten, der wirklich blockiert.
    /// </summary>
    private void AddCertificate(Card card)
    {
        if (!_withAgent)
        {
            // Ohne Agent lauscht hier nichts, das ein Zertifikat vorzeigen
            // müsste. Es zu verlangen wäre eine Hürde für nichts.
            return;
        }

        var wanted = Profile().Normalized().AdvertisedAddress;
        var certificate = _probe.Certificate;
        var fits = wanted is not null && _probe.CertificateCovers(wanted);

        card.Body.Add(new TextBlock(
            fits
                ? $"Das Zertifikat von Tailscale liegt bereit — ausgestellt auf {wanted}."
                : certificate is null
                    ? "Das Zertifikat von Tailscale fehlt noch. Ohne es stellt der Agent sich "
                      + "selbst eins aus, und jedes Handy muss die ausstellende Stelle "
                      + "bestätigen."
                    : !certificate.IsValidAt(DateTimeOffset.UtcNow)
                        ? "Hier liegt ein abgelaufenes Zertifikat. Es muss neu geholt werden — "
                          + "der Agent zeigt es sonst vor, und jede Verbindung scheitert daran."
                        : $"Das Zertifikat hier lautet auf {string.Join(", ", certificate.Names)} "
                          + $"und nicht auf {wanted}. Unter dem eingetragenen Namen käme keine "
                          + "Verbindung zustande.",
            Theme.Body,
            fits ? Theme.Text : Theme.TextDim));

        var fetch = new ThemedButton(
            fits ? "Zertifikat neu holen" : "Zertifikat holen",
            fits ? ButtonTone.Secondary : ButtonTone.Primary);

        fetch.Click += async (_, _) => await FetchCertificateAsync();

        card.Body.Add(Row.Buttons(fetch));
    }

    /// <summary>
    /// Es wird für die **eingetragene** Adresse geholt und nicht für den Namen,
    /// den <c>tailscale status</c> gerade meldet. Beides ist meistens dasselbe;
    /// wenn nicht, gewinnt das, was gleich im QR-Code steht — sonst zeigt der
    /// Agent nachher ein Zertifikat auf einen Namen vor, den niemand abfragt.
    /// </summary>
    private async Task FetchCertificateAsync()
    {
        Remember();

        var target = Profile().Normalized().AdvertisedAddress;

        if (target is null)
        {
            Report("Trage zuerst den Namen dieses Rechners im Tailscale-Netz ein.", Tone.Bad);

            return;
        }

        await StepAsync(
            $"Das Zertifikat für {target} wird geholt…",
            () => Elevation.Run(AdminTask.FetchCertificate, target));
    }

    /// <summary>
    /// Warum es hier nicht weitergeht — <c>null</c>, wenn es weitergeht.
    ///
    /// Ein Satz und keine Liste: es gibt eine nächste Sache zu tun, und die
    /// steht da. Fünf offene Punkte gleichzeitig anzuzeigen hieße, den Assistenten
    /// durch eine Aufgabenliste zu ersetzen.
    /// </summary>
    private string? Blocker()
    {
        var profile = Profile().Normalized();

        if (profile.Rejection is { } rejection)
        {
            return rejection;
        }

        if (profile.NeedsTailscale)
        {
            if (!_tailscaleInstalled)
            {
                return "Zuerst den Tailscale-Client installieren, dann auf „Neu prüfen“.";
            }

            if (!_tailscaleConnected)
            {
                return "Dieser Rechner ist noch nicht angemeldet — „Jetzt anmelden“, dann "
                       + "„Neu prüfen“.";
            }
        }

        if (_withAgent
            && profile.CanFetchCertificate
            && !_probe.CertificateCovers(profile.AdvertisedAddress))
        {
            return $"Es fehlt noch das Zertifikat für {profile.AdvertisedAddress}.";
        }

        return null;
    }

    /// <summary>
    /// Knopf und Begründung an den aktuellen Stand anpassen — ohne die Karte neu
    /// zu bauen, sonst verlöre das Eingabefeld bei jedem Zeichen den Fokus.
    /// </summary>
    private void UpdateGate()
    {
        if (_forward is null || _blocker is null)
        {
            return;
        }

        var blocker = Blocker();

        _forward.Enabled = blocker is null;
        _blocker.Retext(blocker ?? string.Empty);
    }

    // ---- Autostart ---------------------------------------------------------

    private Card WindowsCard()
    {
        var card = new Card("Soll RemoteDesktop mit Windows starten?");
        var choice = new ChoiceGroup<bool>();

        choice.Add(
            true,
            "Ja",
            "Das Fenster wartet nach dem Anmelden im Infobereich, ohne sich in den "
            + "Vordergrund zu drängen.");

        choice.Add(
            false,
            "Nein",
            "Nichts startet von allein. Du öffnest RemoteDesktop, wenn du es brauchst.");

        choice.Select(_withWindows);
        choice.Chosen += value => _withWindows = value;

        card.Body.Add(choice);
        card.Body.Add(Navigation(back: true, next: Forward));

        return card;
    }

    private Card AgentStartCard()
    {
        var card = new Card("Soll der Agent auch automatisch starten?");
        var choice = new ChoiceGroup<bool>();

        choice.Add(
            true,
            "Ja",
            "Dieser Rechner ist erreichbar, sobald du angemeldet bist — ohne dass "
            + "jemand hier etwas anklickt.");

        choice.Add(
            false,
            "Nein",
            "Den Agent startest du selbst, hier im Fenster oder aus dem Infobereich.");

        choice.Select(_agentWithWindows);
        choice.Chosen += value => _agentWithWindows = value;

        card.Body.Add(choice);

        card.Body.Add(new TextBlock(
            "Der Agent läuft in deiner Sitzung und startet mit deiner Anmeldung; ohne "
            + "angemeldeten Benutzer ist dieser Rechner nicht erreichbar.",
            Theme.Body,
            Theme.TextDim));

        card.Body.Add(Navigation(back: true, next: Forward));

        return card;
    }

    // ---- Übersicht ---------------------------------------------------------

    private Card SummaryCard()
    {
        var profile = Profile().Normalized();
        var card = new Card("Nachsehen und abschließen");

        var lines = new List<string>
        {
            $"Name: {DeviceNameFile.Sanitize(_deviceName) ?? Environment.MachineName}",
            $"Dieser Rechner: {(_withAgent ? "steuert und wird gesteuert" : "steuert nur")}",
            $"Verbindung: {profile.Name()}",
            $"Adresse: {profile.AdvertisedAddress ?? "—"}"
        };

        if (profile.Kind == NetworkKind.Headscale)
        {
            lines.Add($"Koordinator: {profile.Coordinator.Address}");
        }

        if (_withAgent)
        {
            lines.Add($"Zertifikat: {DescribeCertificate(profile)}");
        }

        lines.Add($"Beim Hochfahren: {Mode().Describe()}");

        card.Body.Add(new TextBlock(string.Join("\n", lines), Theme.Body, Theme.Text));

        if (_withAgent)
        {
            card.Body.Add(new TextBlock(
                "Windows fragt gleich einmal nach Administratorrechten — für den Eintrag in "
                + "die Aufgabenplanung. Danach nicht mehr. Anschließend startet der Agent "
                + "mit genau diesen Einstellungen.",
                Theme.Body,
                Theme.TextDim));
        }

        var back = new ThemedButton("Zurück");
        var finish = new ThemedButton("Einrichtung abschließen", ButtonTone.Primary);

        back.Click += (_, _) => Backward();
        finish.Click += async (_, _) => await CompleteAsync();

        card.Body.Add(Row.Buttons(back, finish));

        return card;
    }

    private string DescribeCertificate(NetworkProfile profile)
    {
        if (_probe.CertificateCovers(profile.AdvertisedAddress))
        {
            return "von Tailscale — auf dem Handy gibt es nichts zu bestätigen";
        }

        return profile.CanFetchCertificate
            ? "wird beim Abschließen von Tailscale geholt"
            : "vom Agent selbst ausgestellt — das Handy bestätigt es einmal beim Koppeln";
    }

    // ---- Navigation --------------------------------------------------------

    private Row Navigation(bool back, Action next)
    {
        var buttons = new List<Control>();

        if (back)
        {
            var previous = new ThemedButton("Zurück");

            previous.Click += (_, _) => Backward();
            buttons.Add(previous);
        }

        _forward = new ThemedButton("Weiter", ButtonTone.Primary);
        _forward.Click += (_, _) => next();

        buttons.Add(_forward);

        return Row.Buttons([.. buttons]);
    }

    private void Forward()
    {
        Remember();

        // Der Name wird hier geschrieben und nicht erst beim Abschließen: er
        // hängt an nichts, was Rechte verlangt, und wer den Assistenten in der
        // Mitte verlässt, soll ihn trotzdem vergeben haben.
        if (_step == Step.Name && DeviceNameFile.Sanitize(_deviceName) is { } chosen)
        {
            try
            {
                AgentData.SetDeviceName(chosen);
            }
            catch (Exception failure)
            {
                Report($"Der Name ließ sich nicht speichern: {failure.Message}", Tone.Bad);
            }
        }

        var steps = Steps();
        var index = steps.IndexOf(_step);

        if (index >= 0 && index + 1 < steps.Count)
        {
            _step = steps[index + 1];
        }

        Draw();
    }

    private void Backward()
    {
        Remember();

        var steps = Steps();
        var index = steps.IndexOf(_step);

        if (index > 0)
        {
            _step = steps[index - 1];
        }

        Draw();
    }

    private NetworkProfile Profile() =>
        new(_kind, _address, _kind == NetworkKind.Headscale
            ? Coordinator.From(_coordinator)
            : Coordinator.Default);

    private AutostartMode Mode() => AutostartModes.From(_withWindows, _withAgent && _agentWithWindows);

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
            _step = Step.Details;
            Draw();

            return;
        }

        _busy = true;
        Report("Einen Moment…", Tone.Working);

        var mode = Mode();

        var request = new SetupRequest(
            profile,
            !_withAgent
                ? AgentSetup.None
                : mode.Starts(AutostartMode.Agent)
                    ? AgentSetup.Automatic
                    : AgentSetup.Manual,
            // In wessen Sitzung der Agent laufen soll. Der erhöhte Aufruf kann
            // das nicht wissen — er läuft womöglich unter einem anderen Konto.
            AgentService.InteractiveUser,
            // Der Regelfall ist inzwischen, dass das Zertifikat schon dasteht —
            // ohne es käme man am Detailschritt gar nicht vorbei. Bleibt der
            // Nachzügler: eins, das zwischen jenem Schritt und hier abgelaufen
            // oder verschwunden ist.
            Certificate: _withAgent
                         && profile.CanFetchCertificate
                         && !_probe.CertificateCovers(profile.AdvertisedAddress));

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
            _autostartHost.SetClientEntry(mode.Starts(AutostartMode.Client));

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
