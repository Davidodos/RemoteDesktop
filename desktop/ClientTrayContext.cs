namespace RemoteDesktopClient;

/// <summary>
/// Das Tray-Icon — der eigentliche Sitz des Programms.
///
/// Die Fenster sind nur Ansichten davon und starten nicht mit: wer den Rechner
/// hochfährt, will meist nicht sofort ein Fenster, sondern nur, dass alles
/// bereitsteht.
///
/// Seit V3 hängen hier auch die beiden Handgriffe, die man wirklich unterwegs
/// braucht: den Agent anhalten und wieder anwerfen, ohne dafür ein Fenster zu
/// öffnen.
/// </summary>
public sealed class ClientTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly LocalAgent _agent = new(LocalAgent.ConfiguredPort());
    private readonly WindowsProbe _probe = new();
    private readonly string? _appDirectory;

    private readonly ToolStripMenuItem _startAgent = new("Agent starten");
    private readonly ToolStripMenuItem _stopAgent = new("Agent beenden");

    private MainWindow? _remote;
    private PairingWindow? _pairing;
    private ControlPanel? _panel;

    public ClientTrayContext(string? appDirectory)
    {
        _appDirectory = appDirectory;

        var menu = new ContextMenuStrip();

        menu.Items.Add("RemoteDesktop öffnen", image: null, async (_, _) => await ShowPanelAsync());
        menu.Items.Add("Fernsteuerung öffnen", image: null, async (_, _) => await ShowRemoteAsync());
        menu.Items.Add("Geräte koppeln…", image: null, (_, _) => ShowPairing());
        menu.Items.Add(new ToolStripSeparator());

        _startAgent.Click += async (_, _) => await ServiceAsync(AdminTask.StartService);
        _stopAgent.Click += async (_, _) => await ServiceAsync(AdminTask.StopService);

        menu.Items.Add(_startAgent);
        menu.Items.Add(_stopAgent);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", image: null, (_, _) => Quit());

        // Erst beim Aufklappen fragen: der Zustand des Dienstes ändert sich, und
        // ein Menü, das den Stand von vorhin zeigt, führt in die Irre.
        menu.Opening += async (_, _) => await RefreshMenuAsync();

        _tray = new NotifyIcon
        {
            // Ein eigenes Symbol käme mit einer Binärdatei im Repo. Bis es eins
            // gibt, ist das Standardsymbol ehrlicher als gar keins.
            Icon = SystemIcons.Application,
            Text = "RemoteDesktop",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.DoubleClick += async (_, _) => await ShowPanelAsync();
    }

    /// <summary>
    /// Das gemeinsame Fenster. Es zeigt alle Teile, auch die auf diesem Rechner
    /// nicht eingerichteten — der Punkt des ganzen Umbaus.
    /// </summary>
    private async Task ShowPanelAsync()
    {
        if (_panel is null || _panel.IsDisposed)
        {
            _panel = new ControlPanel(
                _probe,
                new WindowsAutostart(Environment.ProcessPath ?? string.Empty),
                _appDirectory,
                ShowRemoteAsync);
        }

        _panel.Show();
        _panel.BringToFront();

        await _panel.RefreshAsync();
    }

    /// <summary>Das Fenster, mit dem man andere Rechner steuert.</summary>
    private async Task ShowRemoteAsync()
    {
        if (_appDirectory is null)
        {
            MessageBox.Show(
                WebAppLocator.MissingMessage, "RemoteDesktop",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        if (WebView2Runtime.InstalledVersion() is null)
        {
            MessageBox.Show(
                WebView2Runtime.MissingMessage, "RemoteDesktop",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        if (_remote is null || _remote.IsDisposed)
        {
            _remote = new MainWindow(_appDirectory);
        }

        _remote.Show();
        _remote.BringToFront();

        try
        {
            await _remote.LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Die Oberfläche ließ sich nicht laden.\n\n{ex.Message}",
                "RemoteDesktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowPairing()
    {
        if (_pairing is null || _pairing.IsDisposed)
        {
            _pairing = new PairingWindow(_agent);
        }

        _pairing.Show();
        _pairing.BringToFront();
    }

    private async Task ServiceAsync(AdminTask task)
    {
        var result = await Task.Run(() => Elevation.Run(task));

        if (!result.Ok)
        {
            MessageBox.Show(
                result.Message, "RemoteDesktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (_panel is { IsDisposed: false })
        {
            await _panel.RefreshAsync();
        }
    }

    /// <summary>
    /// Nur anbieten, was gerade geht: einen nicht eingetragenen Dienst kann man
    /// weder starten noch beenden, und ein laufender braucht keinen Startknopf.
    /// </summary>
    private async Task RefreshMenuAsync()
    {
        var installed = AgentService.Installed;
        var running = installed && await AgentService.RespondsAsync();

        _startAgent.Enabled = installed && !running;
        _stopAgent.Enabled = installed && running;
    }

    private void Quit()
    {
        // Ohne das bleibt das Symbol als Leiche im Tray stehen, bis jemand mit
        // der Maus darüberfährt.
        _tray.Visible = false;

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _agent.Dispose();
            _remote?.Dispose();
            _pairing?.Dispose();
            _panel?.Dispose();
        }

        base.Dispose(disposing);
    }
}
