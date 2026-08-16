using RemoteDesktopClient.Pages;
using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Das Fenster. Es gibt nur dieses eine.
///
/// <para>
/// **Der Befund dahinter:** bis V3 waren es drei — Einstellungen, Fernsteuerung
/// und Kopplung, jedes mit eigener Titelzeile, eigenem Platz auf der Taskleiste
/// und eigener Vorstellung davon, wie ein Knopf aussieht. Wer ein Gerät koppeln
/// wollte, während er einen Rechner steuerte, schob Fenster hin und her. Jetzt
/// sind es Seiten, und der Wechsel ist ein Klick in der Leiste links.
/// </para>
///
/// <para>
/// Das Fenster schließt nie wirklich: das Kreuz versteckt es, weil das Programm
/// im Infobereich weiterläuft. Beendet wird dort und nur dort — sonst wäre eine
/// laufende Sitzung mit einem Fehlklick weg.
/// </para>
/// </summary>
public sealed class ShellWindow : Form
{
    private readonly WindowsProbe _probe;
    private readonly NavigationRail _rail = new() { Dock = DockStyle.Left };
    private readonly StatusLine _status = new() { Dock = DockStyle.Bottom };
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Theme.Window };

    private readonly Dictionary<Page, Control> _pages = [];
    private readonly RemotePage _remote;
    private readonly SetupPage _setup;

    /// <summary>
    /// Wie oft das Fenster von allein nachsieht. Zwei Sekunden sind der
    /// Kompromiss: kurz genug, dass ein frisch gestarteter Dienst nicht
    /// „gestoppt“ bleibt, lang genug, dass die Abfragen nicht auffallen.
    /// </summary>
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(2);

    private readonly System.Windows.Forms.Timer _pulse = new();

    private Page _current = Page.Overview;
    private bool _fullscreen;
    private FormWindowState _beforeFullscreen = FormWindowState.Normal;
    private bool _busy;

    /// <summary>
    /// Ob gerade schon jemand nachfragt. Der Takt und ein Seitenwechsel dürfen
    /// sich nicht überholen: beide bauen dieselben Karten neu, und zweimal
    /// gleichzeitig ergäbe eine Seite mit doppeltem Inhalt.
    /// </summary>
    private bool _refreshing;

    public ShellWindow(
        WindowsProbe probe, LocalAgent agent, IAutostartHost autostart, string? appDirectory)
    {
        _probe = probe;

        Text = "RemoteDesktop";
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = Brand.Load(32);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 620);
        Size = new Size(1120, 760);
        KeyPreview = true;

        _remote = new RemotePage(appDirectory);
        _remote.FullscreenToggled += ToggleFullscreen;

        // Ohne Netzprofil ist dieser Rechner noch nie eingerichtet worden —
        // dann führt der erste Blick in den Assistenten und nicht in eine
        // Übersicht, auf der alles fehlt.
        _current = SetupState.Done ? Page.Overview : Page.Setup;

        _setup = new SetupPage(probe, autostart, () => ShowPageAsync(Page.Overview));

        Register(Page.Overview, new OverviewPage(probe, appDirectory, PerformAsync));
        Register(Page.Remote, _remote);
        Register(Page.Network, new NetworkPage(probe));
        Register(Page.Setup, _setup);
        Register(
            Page.Settings,
            new SettingsPage(
                autostart,
                () => _ = ShowPageAsync(Page.Setup),
                () => _ = ShowPageAsync(Page.Network)));

        _rail.Picked += page => _ = ShowPageAsync(page);

        Controls.Add(_host);
        Controls.Add(_rail);
        Controls.Add(_status);

        _rail.Highlight(_current);

        _pulse.Interval = (int)Beat.TotalMilliseconds;
        _pulse.Tick += (_, _) => _ = TickAsync();
        _pulse.Start();
    }

    /// <summary>
    /// Von allein nachsehen, statt auf einen Seitenwechsel zu warten.
    ///
    /// <para>
    /// **Der Befund dahinter:** der Installer startet den Dienst und öffnet
    /// gleich danach das Fenster. Bis der Agent antwortet, vergehen ein paar
    /// Sekunden — das Fenster fragte genau einmal, bekam „nein“ und blieb bei
    /// „Agent gestoppt“. Wer dann auf „Starten“ klickte, bekam einen Fehler,
    /// weil der Dienst längst lief. Dasselbe galt für die gekoppelten Geräte:
    /// ein frisch gekoppeltes Handy tauchte erst auf, wenn man einmal die Seite
    /// wechselte und zurückkam.
    /// </para>
    /// </summary>
    private async Task TickAsync()
    {
        // Nicht sichtbar heißt: niemand liest mit. Und während eines Handgriffs
        // wäre eine zweite Abfrage nur im Weg — der Handgriff fragt selbst nach.
        if (_refreshing || _busy || !Visible || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _refreshing = true;

        try
        {
            if (_pages[_current] is PageView view && view.LiveRefresh)
            {
                await view.RefreshAsync();
            }

            await ShowAgentStateAsync();

        }
        catch (Exception)
        {
            // Ein Nachsehen im Hintergrund darf nichts melden und schon gar
            // nichts abbrechen. Was dauerhaft nicht geht, sieht man am Zustand
            // der Karte; ein Fehlerfenster alle zwei Sekunden wäre eine Plage.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Register(Page page, Control view)
    {
        view.Dock = DockStyle.Fill;
        view.Visible = page == _current;

        if (view is PageView typed)
        {
            typed.Reporter = Say;
        }

        _pages[page] = view;
        _host.Controls.Add(view);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.Darken(Handle);

        // Die Oberfläche im Hintergrund hochfahren, auch wenn gerade eine
        // andere Seite zu sehen ist.
        //
        // **Der Befund dahinter:** die WebView entstand erst beim ersten Öffnen
        // der Fernsteuerung. Solange sie nicht lief, holte niemand das Angebot
        // zur Gegenkopplung ab — und der Kopplungscode darin ist nach fünf
        // Minuten wertlos. Wer am Handy koppelte und den Tab später öffnete,
        // fand dort „Noch kein Gerät gekoppelt", obwohl alles richtig gelaufen
        // war. Nebenbei ist der erste Wechsel auf die Seite jetzt sofort da.
        //
        // Fehlschläge bleiben hier still: sie gehören auf die Seite, wenn
        // jemand sie öffnet, und nicht in eine Statuszeile, in der niemand sie
        // erwartet.
        _ = _remote.ShowRemoteAsync().ContinueWith(
            _ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Vor das Fenster holen und den Zustand neu erfragen. Wird vom Infobereich
    /// gerufen — auch dann, wenn es schon offen ist.
    /// </summary>
    public async Task SurfaceAsync()
    {
        Show();

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();

        await RefreshAsync();
    }

    public async Task ShowPageAsync(Page page)
    {
        if (_fullscreen && page != Page.Remote)
        {
            ToggleFullscreen();
        }

        foreach (var (key, view) in _pages)
        {
            view.Visible = key == page;
        }

        _current = page;
        _rail.Highlight(page);

        // Wer die Einrichtung aus der Leiste aufruft, will von vorn anfangen —
        // und nicht dort weitermachen, wo er beim letzten Mal ausgestiegen ist.
        if (page == Page.Setup)
        {
            _setup.Restart();
        }

        if (page == Page.Remote)
        {
            Say("Fernsteuerung — F11 schaltet auf Vollbild.");
            await _remote.ShowRemoteAsync();

            return;
        }

        if (_pages[page] is PageView view2)
        {
            // Beim Wechsel auf eine Seite alles neu erfragen — auch das, was
            // teuer ist. Der Takt weiter unten tut das ausdrücklich nicht: er
            // liefe sonst alle zwei Sekunden `tailscale status`.
            _probe.Forget();

            await SafelyAsync(view2);
        }

        await ShowAgentStateAsync();
    }

    /// <summary>
    /// Alles neu erfragen. Nach jedem Handgriff, weil ein Fenster, das noch den
    /// Stand von vorhin zeigt, schlimmer ist als eines, das kurz leer aussieht.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_pages[_current] is PageView view)
        {
            _probe.Forget();

            await SafelyAsync(view);
        }

        await ShowAgentStateAsync();
    }

    /// <summary>
    /// Der Zustand des Agents steht unten in der Leiste und damit auf jeder
    /// Seite. Es ist die eine Angabe, wegen der man sonst zurückwechseln müsste.
    /// </summary>
    private async Task ShowAgentStateAsync()
    {
        if (!AgentService.Installed)
        {
            _rail.ShowAgent("Agent nicht eingerichtet", Theme.TextDim);

            return;
        }

        if (await AgentService.RespondsAsync())
        {
            _rail.ShowAgent("Agent läuft", Theme.Online);

            return;
        }

        // „Läuft nicht" und „antwortet nicht" schicken beim Suchen an
        // verschiedene Stellen. Deshalb stehen sie auch verschieden da.
        var alive = AgentService.ProcessRunning;

        _rail.ShowAgent(
            alive ? "Agent antwortet nicht" : "Agent gestoppt",
            alive ? Theme.Warn : Theme.Danger);
    }

    /// <summary>
    /// Eine Seite auffrischen — und einen Fehlschlag dabei in die Statuszeile
    /// schreiben statt ins Verderben.
    ///
    /// <para>
    /// **Der Befund dahinter:** eine gesperrte Registry warf beim Erfragen des
    /// Agent-Zustands. Die Übersicht blieb leer, weil sie nie bis zum Füllen
    /// kam, und kurz darauf stand das Absturzfenster von .NET auf dem
    /// Bildschirm. Keine Auskunft rechtfertigt ein beendetes Programm.
    /// </para>
    /// </summary>
    private async Task SafelyAsync(PageView view)
    {
        _refreshing = true;

        try
        {
            await view.RefreshAsync();
        }
        catch (Exception failure)
        {
            Say($"Diese Seite ließ sich nicht auffrischen: {failure.Message}", Tone.Bad);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Say(string message, Tone tone = Tone.Neutral) => _status.Say(message, tone);

    /// <summary>
    /// Ein Handgriff von der Übersicht. Alles, was Rechte verlangt, geht über
    /// <see cref="Elevation"/>; alles andere über <see cref="ProcessRunner"/> —
    /// und beides ohne aufblitzendes Fenster.
    /// </summary>
    private async Task PerformAsync(PartAction action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        Say("Einen Moment…", Tone.Working);

        try
        {
            switch (action)
            {
                case PartAction.Open:
                    await ShowPageAsync(Page.Remote);
                    _busy = false;

                    return;

                case PartAction.Download:
                    OverviewPage.Open(Tailscale.Download);
                    Say("Tailscale öffnet sich im Browser. Danach hier weiter.");

                    break;

                case PartAction.SignIn:
                    Report(await Task.Run(() => ProcessRunner.Run(
                        Tailscale.Executable,
                        NetworkStore.Read().Coordinator.UpArguments(),
                        TimeSpan.FromMinutes(3))));

                    break;

                case PartAction.Certificate:
                    // Die eingetragene Adresse schlägt den Namen, den Tailscale
                    // gerade meldet: genau sie steht später im QR-Code, und ein
                    // Zertifikat auf einen anderen Namen nützt dort nichts.
                    Report(await Task.Run(() => Elevation.Run(
                        AdminTask.FetchCertificate,
                        NetworkStore.Read().AdvertisedAddress ?? _probe.TailnetName)));

                    break;

                case PartAction.Install:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.InstallService, "auto")));

                    break;

                case PartAction.Remove:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.RemoveService)));

                    break;

                case PartAction.Start:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.StartService)));

                    break;

                default:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.StopService)));

                    break;
            }
        }
        catch (Exception failure)
        {
            Say(failure.Message, Tone.Bad);
        }
        finally
        {
            _busy = false;
        }

        await RefreshAsync();
    }

    private void Report(RunResult result) =>
        Say(result.Message, result.Ok ? Tone.Good : Tone.Bad);

    // ---- Vollbild ----------------------------------------------------------

    /// <summary>
    /// Randlos über den ganzen Bildschirm — dasselbe Fenster, nur ohne alles
    /// darum. Beim Steuern eines fremden Rechners zählt jeder Millimeter, und
    /// die Seitenleiste ist in dem Moment die Information, die am wenigsten
    /// gebraucht wird.
    /// </summary>
    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;

        _rail.Visible = !_fullscreen;
        _status.Visible = !_fullscreen;

        if (_fullscreen)
        {
            _beforeFullscreen = WindowState;

            // Der Umweg über „Normal" ist nötig: aus einem bereits maximierten
            // Fenster heraus nimmt Windows die neue Rahmenart nicht an, und das
            // Fenster bliebe mit Titelzeile stehen.
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;

            return;
        }

        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.Sizable;
        WindowState = _beforeFullscreen;

        WindowChrome.Darken(Handle);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys key)
    {
        if (key == Keys.F11)
        {
            if (_current != Page.Remote)
            {
                _ = ShowPageAsync(Page.Remote);
            }

            ToggleFullscreen();

            return true;
        }

        if (key == Keys.Escape && _fullscreen)
        {
            ToggleFullscreen();

            return true;
        }

        return base.ProcessCmdKey(ref message, key);
    }

    /// <summary>
    /// Das Kreuz versteckt das Fenster, statt das Programm zu beenden. Wer
    /// wirklich aufhören will, tut das im Infobereich — sonst wäre eine laufende
    /// Sitzung mit einem Fehlklick weg.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;

            if (_fullscreen)
            {
                ToggleFullscreen();
            }

            Hide();

            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pulse.Dispose();
        }

        base.Dispose(disposing);
    }
}
