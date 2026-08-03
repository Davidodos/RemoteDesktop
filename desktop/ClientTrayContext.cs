namespace RemoteDesktopClient;

/// <summary>
/// Das Tray-Icon — der eigentliche Sitz des Programms.
///
/// Das Fenster ist nur eine Ansicht davon und startet nicht mit: wer den
/// Rechner hochfährt, will meist nicht sofort ein Fenster, sondern nur, dass
/// der Client bereitsteht. Damit ist zugleich der offene Punkt aus Phase 1
/// erledigt.
/// </summary>
public sealed class ClientTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly LocalAgent _agent = new(LocalAgent.ConfiguredPort());
    private readonly string _appDirectory;

    private MainWindow? _window;
    private PairingWindow? _pairing;
    private SettingsWindow? _settings;

    public ClientTrayContext(string appDirectory)
    {
        _appDirectory = appDirectory;

        var menu = new ContextMenuStrip();

        menu.Items.Add("Fenster öffnen", image: null, async (_, _) => await ShowWindowAsync());
        menu.Items.Add("Geräte koppeln…", image: null, (_, _) => ShowPairing());
        menu.Items.Add("Einrichtung…", image: null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", image: null, (_, _) => Quit());

        _tray = new NotifyIcon
        {
            // Ein eigenes Symbol käme mit einer Binärdatei im Repo. Bis es eins
            // gibt, ist das Standardsymbol ehrlicher als gar keins.
            Icon = SystemIcons.Application,
            Text = "RemoteDesktop",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.DoubleClick += async (_, _) => await ShowWindowAsync();
    }

    private async Task ShowWindowAsync()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new MainWindow(_appDirectory);
        }

        _window.Show();
        _window.BringToFront();

        try
        {
            await _window.LoadAsync();
        }
        catch (Exception ex)
        {
            // Bis hierher ist die Runtime schon geprüft worden; scheitert es
            // trotzdem, ist es kein Fall für einen stillen Absturz.
            MessageBox.Show(
                $"Die Oberfläche ließ sich nicht laden.\n\n{ex.Message}",
                "RemoteDesktop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

    /// <summary>
    /// Das gemeinsame Fenster für Agent und Client. Welche Teile auf diesem
    /// Rechner überhaupt liegen, liest es selbst — der Installer hat es
    /// hinterlassen, und was fehlt, wird gar nicht erst angeboten.
    /// </summary>
    private void ShowSettings()
    {
        if (_settings is null || _settings.IsDisposed)
        {
            var probe = new WindowsProbe();

            _settings = new SettingsWindow(
                probe,
                new WindowsAutostart(Environment.ProcessPath ?? string.Empty),
                InstalledSelection(probe));
        }

        _settings.Show();
        _settings.BringToFront();
    }

    /// <summary>
    /// Was hier installiert ist: der Client ist es offensichtlich — er zeigt
    /// gerade dieses Menü —, der Agent nur, wenn sein Dienst existiert.
    /// </summary>
    private static RemoteDesktopSetup.Selection InstalledSelection(RemoteDesktopSetup.ISetupProbe probe)
    {
        var components = RemoteDesktopSetup.SetupComponent.Client;

        if (probe.HasService)
        {
            components |= RemoteDesktopSetup.SetupComponent.Agent;
        }

        if (probe.HasTailscale)
        {
            components |= RemoteDesktopSetup.SetupComponent.Tailscale;
        }

        return new RemoteDesktopSetup.Selection(components, RemoteDesktopSetup.AutostartMode.None);
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
            _window?.Dispose();
            _pairing?.Dispose();
            _settings?.Dispose();
        }

        base.Dispose(disposing);
    }
}
