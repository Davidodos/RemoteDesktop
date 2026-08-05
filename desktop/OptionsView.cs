using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Was beim Anmelden startet und woher die nächste Fassung kommt.
///
/// Beides gehört nicht auf die Übersicht: es ist einmal eingestellt und danach
/// selten wieder angefasst. Auf der Übersicht stünde es jedem im Weg, der nur
/// wissen will, ob der Agent läuft.
/// </summary>
public sealed class OptionsView : UserControl
{
    private readonly IAutostartHost _autostart;
    private readonly Action<string> _report;

    private readonly Dictionary<AutostartMode, RadioButton> _modes = [];

    private readonly ClientUpdate _update = new();
    private readonly Label _updateState = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Button _updateAct = new() { AutoSize = true };
    private ReleaseOffer? _offer;

    private bool _loading;

    public OptionsView(IAutostartHost autostart, Action<string> report)
    {
        _autostart = autostart;
        _report = report;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoScroll = true
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildAutostart(), 0, 0);
        layout.Controls.Add(BuildUpdates(), 0, 1);

        Controls.Add(layout);
    }

    private Control BuildAutostart()
    {
        var box = new GroupBox
        {
            Text = "Beim Anmelden starten", Dock = DockStyle.Fill, AutoSize = true
        };

        var inner = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(8)
        };

        foreach (var mode in new[]
                 {
                     AutostartMode.Both, AutostartMode.Agent,
                     AutostartMode.Client, AutostartMode.None
                 })
        {
            var button = new RadioButton { Text = mode.Describe(), AutoSize = true };

            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked && !_loading)
                {
                    ApplyAutostart(mode);
                }
            };

            _modes[mode] = button;
            inner.Controls.Add(button);
        }

        box.Controls.Add(inner);

        return box;
    }

    /// <summary>
    /// Agent und Fenster zusammen erneuern — über den Installer, nicht über
    /// einzeln kopierte Dateien. Beide stecken in demselben Paket, deshalb steht
    /// der Knopf auch nur einmal da.
    /// </summary>
    private Control BuildUpdates()
    {
        var box = new GroupBox { Text = "Updates", Dock = DockStyle.Fill, AutoSize = true };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true,
            Padding = new Padding(8)
        };

        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _updateState.Text = $"Fassung {ClientUpdate.InstalledVersion()}";
        _updateAct.Text = "Nach Updates suchen";
        _updateAct.Click += async (_, _) => await UpdateStepAsync();

        inner.Controls.Add(_updateState, 0, 0);
        inner.Controls.Add(_updateAct, 1, 0);

        box.Controls.Add(inner);

        return box;
    }

    /// <summary>Was gerade eingestellt ist, ohne dabei etwas auszulösen.</summary>
    public void ShowCurrent()
    {
        _loading = true;

        var current = Autostart.Read(_autostart);

        if (_modes.TryGetValue(current, out var button))
        {
            button.Checked = true;
        }

        _loading = false;
    }

    private void ApplyAutostart(AutostartMode mode)
    {
        try
        {
            Autostart.Apply(_autostart, mode);
            _report("Gespeichert.");
        }
        catch (Exception failure)
        {
            // Den Starttyp eines Dienstes zu ändern verlangt Adminrechte. Ohne
            // sie bleibt die Einstellung, wie sie war — das muss dastehen, sonst
            // glaubt jemand, er habe etwas eingestellt.
            _report($"Nicht gespeichert: {failure.Message}");
            ShowCurrent();
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
                _updateState.Text = "Suche…";
                _offer = await _update.CheckAsync(CancellationToken.None);

                if (_offer is null)
                {
                    _updateState.Text =
                        $"Fassung {ClientUpdate.InstalledVersion()} — das ist die neueste.";
                    _updateAct.Text = "Nach Updates suchen";

                    return;
                }

                _updateState.Text = $"Fassung {_offer.Version} liegt bereit.";
                _updateAct.Text = "Jetzt installieren";

                return;
            }

            _updateState.Text = "Wird geladen…";
            await _update.InstallAsync(_offer, CancellationToken.None);

            // Der Installer läuft jetzt und will diese .exe ersetzen. Eine
            // laufende Datei lässt sich nicht überschreiben, also geht das
            // Fenster von selbst — und der Installer startet es danach wieder.
            Application.Exit();
        }
        catch (Exception failure)
        {
            _updateState.Text = $"Nicht geklappt: {failure.Message}";
            _offer = null;
            _updateAct.Text = "Nach Updates suchen";
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
