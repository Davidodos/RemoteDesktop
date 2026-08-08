using RemoteDesktopClient.Pages;
using RemoteDesktopClient.Ui;

namespace RemoteDesktopClient;

/// <summary>
/// Das Symbol im Infobereich — der eigentliche Sitz des Programms.
///
/// <para>
/// Das Fenster ist nur eine Ansicht davon und startet nicht mit: wer den Rechner
/// hochfährt, will meist nicht sofort ein Fenster, sondern nur, dass alles
/// bereitsteht.
/// </para>
///
/// <para>
/// Im Menü hängen die beiden Handgriffe, die man wirklich unterwegs braucht —
/// den Agent anhalten und wieder anwerfen —, und der Weg ins Fenster. Alles
/// andere steht dort und nicht hier: ein Menü ist kein Ersatz für eine
/// Oberfläche.
/// </para>
/// </summary>
public sealed class ClientTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly LocalAgent _agent = new(LocalAgent.ConfiguredPort());
    private readonly WindowsProbe _probe = new();
    private readonly string? _appDirectory;

    private readonly ToolStripMenuItem _startAgent = new("Agent starten");
    private readonly ToolStripMenuItem _stopAgent = new("Agent beenden");

    private ShellWindow? _window;

    public ClientTrayContext(string? appDirectory)
    {
        _appDirectory = appDirectory;

        var menu = new ContextMenuStrip();

        menu.Items.Add("RemoteDesktop öffnen", image: null, async (_, _) => await OpenAsync());
        menu.Items.Add("Fernsteuerung", image: null, async (_, _) => await OpenAsync(Page.Remote));

        menu.Items.Add(
            "Geräte koppeln…", image: null, async (_, _) => await OpenAsync(Page.Devices));

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
            // 16 Pixel: genau die Größe, in der Windows das Symbol im
            // Infobereich zeigt. Die .ico hat dafür eine eigene, gröbere
            // Zeichnung — siehe assets/icon-small.svg.
            Icon = Brand.Load(16) ?? SystemIcons.Application,
            Text = "RemoteDesktop",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.DoubleClick += async (_, _) => await OpenAsync();

        // Beim allerersten Start führt der Weg ins Fenster und dort in die
        // Einrichtung. Sonst stünde nach der Installation nur ein Symbol im
        // Infobereich, und der Rechner täte nichts — richtig eingerichtet ist
        // er ja noch nicht.
        if (!SetupState.Done)
        {
            _ = OpenAsync(Page.Setup);
        }
    }

    /// <summary>Das eine Fenster, auf der gewünschten Seite.</summary>
    private async Task OpenAsync(Page? page = null)
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new ShellWindow(
                _probe,
                _agent,
                new WindowsAutostart(Environment.ProcessPath ?? string.Empty),
                _appDirectory);
        }

        await _window.SurfaceAsync();

        if (page is { } wanted)
        {
            await _window.ShowPageAsync(wanted);
        }
    }

    private async Task ServiceAsync(AdminTask task)
    {
        var result = await Task.Run(() => Elevation.Run(task));

        if (_window is { IsDisposed: false })
        {
            await _window.RefreshAsync();
        }

        if (result.Ok)
        {
            return;
        }

        // Ohne offenes Fenster gibt es keine Statuszeile, in die das passen
        // würde — und ein stiller Fehlschlag beim Starten des Agents ist genau
        // der, den man später nicht mehr erklären kann.
        _tray.ShowBalloonTip(5000, "RemoteDesktop", result.Message, ToolTipIcon.Warning);
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
        // Ohne das bleibt das Symbol als Leiche im Infobereich stehen, bis
        // jemand mit der Maus darüberfährt.
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
        }

        base.Dispose(disposing);
    }
}
