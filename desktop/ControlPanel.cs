using System.Diagnostics;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Die gemeinsame Oberfläche — ein Fenster für alles.
///
/// <para>
/// **Der Befund dahinter:** bis Release v1.0.0 gab es das Einstellungsfenster
/// nur, wenn der Client installiert war, und es zeigte nur, was ohnehin schon
/// dalag. Wer den Agent allein installiert hatte, sah gar nichts; wer ihn
/// nachrüsten wollte, musste den Installer wiederfinden. Jetzt stehen alle Teile
/// hier, auch die nicht eingerichteten, und jedes hat den Knopf, der es
/// einrichtet.
/// </para>
///
/// <para>
/// Was die Teile *sind* und welcher Handgriff wann passt, steht in
/// <see cref="Inventory"/> und ist dort geprüft. Hier stehen nur Knöpfe.
/// </para>
/// </summary>
public sealed class ControlPanel : Form
{
    private readonly WindowsProbe _probe;
    private readonly string? _appDirectory;
    private readonly Func<Task> _openRemote;

    private readonly FlowLayoutPanel _parts = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true
    };

    private readonly Label _next = new() { Dock = DockStyle.Fill, AutoSize = false, Height = 44 };

    /// <summary>
    /// Der Fingerabdruck der eigenen Zertifizierungsstelle. Er steht hier, weil
    /// ihn jemand ablesen muss: am Handy und am anderen Rechner wird genau
    /// dieser Wert zum Vergleich angezeigt, bevor dort irgendetwas bestätigt
    /// wird. Ohne den Vergleich wäre das Bestätigen wertlos.
    /// </summary>
    private readonly TextBox _fingerprint = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None
    };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 40 };

    private readonly NetworkView _network;
    private readonly OptionsView _options;

    private bool _busy;

    public ControlPanel(
        WindowsProbe probe,
        IAutostartHost autostart,
        string? appDirectory,
        Func<Task> openRemote)
    {
        _probe = probe;
        _appDirectory = appDirectory;
        _openRemote = openRemote;

        _network = new NetworkView(Report);
        _options = new OptionsView(autostart, Report);

        Text = "RemoteDesktop";
        Width = 720;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        tabs.TabPages.Add(Page("Übersicht", BuildOverview()));
        tabs.TabPages.Add(Page("Netz", _network));
        tabs.TabPages.Add(Page("Einstellungen", _options));

        Controls.Add(tabs);
        Controls.Add(_status);

        Shown += async (_, _) => await RefreshAsync();
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(12) };

        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);

        return page;
    }

    private Control BuildOverview()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _next.Font = new Font(Font, FontStyle.Bold);

        layout.Controls.Add(_next, 0, 0);
        layout.Controls.Add(_parts, 0, 1);
        layout.Controls.Add(_fingerprint, 0, 2);

        return layout;
    }

    /// <summary>
    /// Den Zustand neu erfragen und alles neu zeichnen. Nach jedem Handgriff,
    /// weil ein Fenster, das noch den Stand von vorhin zeigt, schlimmer ist als
    /// eines, das kurz leer aussieht.
    /// </summary>
    public async Task RefreshAsync()
    {
        _probe.Forget();

        var profile = NetworkStore.Read();
        var machine = await _probe.SnapshotAsync(_appDirectory);

        _parts.Controls.Clear();

        foreach (var part in Inventory.For(machine, profile))
        {
            _parts.Controls.Add(Card(part));
        }

        _next.Text = NextLine(machine, profile);
        _fingerprint.Text = OwnFingerprint();

        _network.Show(profile);
        _options.ShowCurrent();
    }

    /// <summary>
    /// Der eine Satz, der sagt, was jetzt dran ist. Er ersetzt die frühere
    /// Schrittliste im Fenster: dieselbe Auskunft, aber ohne dass man sich in
    /// einer Liste zurechtfinden muss.
    /// </summary>
    private string NextLine(Machine machine, NetworkProfile profile)
    {
        var selection = new Selection(
            SetupComponent.Agent | SetupComponent.Client, AutostartMode.None);

        var steps = SetupSteps.For(selection, _probe, profile);
        var next = SetupSteps.Next(steps);

        if (!machine.AgentService && machine.AgentBinary)
        {
            return "Als Nächstes: den Agent einrichten, damit dieser Rechner erreichbar wird.";
        }

        return next is null
            ? "Alles steht. Zum Koppeln eines weiteren Geräts unten auf „Geräte koppeln…“."
            : $"Als Nächstes: {next.Title} — {next.Explanation}";
    }

    /// <summary>
    /// Der Fingerabdruck der eigenen Stelle, oder ein Satz darüber, dass es
    /// keine gibt. Bei einem Zertifikat von Tailscale ist Letzteres der
    /// Normalfall — dann kennt jeder Browser den Aussteller ohnehin.
    /// </summary>
    private static string OwnFingerprint()
    {
        var file = Path.Combine(Elevation.DataDirectory, "agentca.crt");

        if (!File.Exists(file))
        {
            return "Dieser Rechner weist sich (noch) nicht mit einer eigenen Stelle aus.";
        }

        try
        {
            var raw = File.ReadAllBytes(file);

            var hex = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant();

            var readable = string.Join(':', Enumerable.Range(0, hex.Length / 2)
                .Select(index => hex.Substring(index * 2, 2)));

            return $"Fingerabdruck dieses Rechners: {readable}";
        }
        catch (IOException failure)
        {
            return $"Fingerabdruck nicht lesbar: {failure.Message}";
        }
    }

    /// <summary>Ein Teil als Kachel: Zustand, Zweck und die passenden Knöpfe.</summary>
    private Control Card(Part part)
    {
        var box = new GroupBox
        {
            Text = $"{(part.Ok ? "✓" : "○")}  {part.Title} — {part.State}",
            Width = _parts.ClientSize.Width - 30,
            Height = 118,
            Padding = new Padding(8)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };

        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        inner.Controls.Add(
            new Label
            {
                Text = part.Purpose,
                Dock = DockStyle.Fill,
                AutoSize = false,

                // Grau, wo etwas fehlt: die Kachel bleibt sichtbar, sagt aber
                // ohne Worte, dass hier noch nichts steht.
                ForeColor = part.Missing ? SystemColors.GrayText : SystemColors.ControlText
            },
            0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };

        foreach (var action in part.Actions)
        {
            var button = new Button { Text = Inventory.Describe(action), AutoSize = true };

            button.Click += async (_, _) => await PerformAsync(action);
            buttons.Controls.Add(button);
        }

        inner.Controls.Add(buttons, 0, 1);
        box.Controls.Add(inner);

        return box;
    }

    /// <summary>
    /// Ein Handgriff. Alles, was Rechte verlangt, geht über
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
        Report("Einen Moment…");

        try
        {
            switch (action)
            {
                case PartAction.Open:
                    await _openRemote();
                    Report("Das Fenster ist offen.");
                    break;

                case PartAction.Download:
                    Process.Start(new ProcessStartInfo(Tailscale.Download) { UseShellExecute = true })
                        ?.Dispose();
                    Report("Tailscale öffnet sich im Browser. Danach hier weiter.");
                    break;

                case PartAction.SignIn:
                    Report(await Task.Run(() => ProcessRunner.Run(
                        Tailscale.Executable,
                        NetworkStore.Read().Coordinator.UpArguments(),
                        TimeSpan.FromMinutes(3)).Message));
                    break;

                case PartAction.Certificate:
                    Report(await Task.Run(() =>
                        Elevation.Run(AdminTask.FetchCertificate, _probe.TailnetName).Message));
                    break;

                case PartAction.Install:
                    Report(await Task.Run(() =>
                        Elevation.Run(AdminTask.InstallService, "auto").Message));
                    break;

                case PartAction.Remove:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.RemoveService).Message));
                    break;

                case PartAction.Start:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.StartService).Message));
                    break;

                default:
                    Report(await Task.Run(() => Elevation.Run(AdminTask.StopService).Message));
                    break;
            }
        }
        catch (Exception failure)
        {
            Report(failure.Message);
        }
        finally
        {
            _busy = false;
        }

        await RefreshAsync();
    }

    private void Report(string message) => _status.Text = message;
}
