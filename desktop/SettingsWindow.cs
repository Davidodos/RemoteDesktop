using System.Diagnostics;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Die gemeinsame Oberfläche von Agent und Client.
///
/// Beide sind getrennte Programme — ein Dienst und ein Fenster —, aber für den
/// Menschen davor ist es ein Programm. Deshalb gibt es genau **ein** Fenster,
/// in dem beide eingerichtet werden: was noch fehlt, ob dieser Rechner erreichbar
/// sein soll, ob er andere steuern soll, und wo sich die Geräte finden.
///
/// Die Entscheidungen dahinter stehen in <c>setup/</c> und sind dort geprüft.
/// Hier stehen nur Knöpfe.
/// </summary>
public sealed class SettingsWindow : Form
{
    private const string TailscaleDownload = "https://tailscale.com/download/windows";

    private readonly ISetupProbe _probe;
    private readonly IAutostartHost _autostart;
    private readonly Selection _selection;

    private readonly ListView _steps = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.None,
        MultiSelect = false
    };

    private readonly Label _explanation = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Padding = new Padding(0, 6, 0, 6)
    };

    private readonly Button _act = new() { AutoSize = true, Enabled = false };
    private readonly TextBox _coordinator = new() { Dock = DockStyle.Fill };
    private readonly Label _coordinatorHint = new() { Dock = DockStyle.Fill, AutoSize = false };

    private readonly Dictionary<AutostartMode, RadioButton> _modes = new();

    private readonly ClientUpdate _update = new();
    private readonly Label _updateState = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Button _updateAct = new() { AutoSize = true };
    private ReleaseOffer? _offer;

    public SettingsWindow(ISetupProbe probe, IAutostartHost autostart, Selection selection)
    {
        _probe = probe;
        _autostart = autostart;
        _selection = selection.Normalized();

        Text = "RemoteDesktop — Einrichtung";
        Width = 640;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;

        Controls.Add(BuildLayout());

        LoadCoordinator();
        RefreshSteps();
        RefreshAutostart();
    }

    private Control BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(BuildSetupBox(), 0, 0);
        layout.Controls.Add(BuildAutostartBox(), 0, 1);
        layout.Controls.Add(BuildUpdateBox(), 0, 2);
        layout.Controls.Add(BuildCoordinatorBox(), 0, 3);

        return layout;
    }

    private Control BuildSetupBox()
    {
        var box = new GroupBox { Text = "Einrichtung", Dock = DockStyle.Fill };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };

        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _steps.Columns.Add("Schritt", -2);
        _steps.SelectedIndexChanged += (_, _) => ShowSelectedStep();

        _act.Click += async (_, _) => await ActOnSelectedStepAsync();

        inner.Controls.Add(_steps, 0, 0);
        inner.Controls.Add(_explanation, 0, 1);
        inner.Controls.Add(_act, 0, 2);

        box.Controls.Add(inner);

        return box;
    }

    private Control BuildAutostartBox()
    {
        var box = new GroupBox { Text = "Beim Anmelden starten", Dock = DockStyle.Fill, AutoSize = true };
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
            var button = new RadioButton
            {
                Text = mode.Describe(),
                AutoSize = true,

                // Was nicht installiert ist, lässt sich auch nicht starten. Der
                // Knopf wird gesperrt statt versteckt: dass es die Möglichkeit
                // gäbe, ist eine Auskunft für sich.
                Enabled = Allowed(mode)
            };

            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked)
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
    /// Agent und Client zusammen erneuern — über den Installer, nicht über
    /// einzeln kopierte Dateien. Beide stecken in demselben Paket, deshalb steht
    /// der Knopf auch nur einmal da.
    /// </summary>
    private Control BuildUpdateBox()
    {
        var box = new GroupBox { Text = "Updates", Dock = DockStyle.Fill, AutoSize = true };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
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

    private Control BuildCoordinatorBox()
    {
        var box = new GroupBox { Text = "Wo sich die Geräte finden", Dock = DockStyle.Fill, AutoSize = true };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(8)
        };

        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var save = new Button { Text = "Übernehmen", AutoSize = true };
        save.Click += (_, _) => SaveCoordinator();

        inner.Controls.Add(_coordinator, 0, 0);
        inner.Controls.Add(save, 1, 0);
        inner.Controls.Add(_coordinatorHint, 0, 1);
        inner.SetColumnSpan(_coordinatorHint, 2);

        box.Controls.Add(inner);

        return box;
    }

    private bool Allowed(AutostartMode mode) =>
        (!mode.Starts(AutostartMode.Agent) || _selection.Has(SetupComponent.Agent))
        && (!mode.Starts(AutostartMode.Client) || _selection.Has(SetupComponent.Client));

    private void RefreshSteps()
    {
        var steps = SetupSteps.For(_selection, _probe);

        _steps.Items.Clear();

        foreach (var step in steps)
        {
            _steps.Items.Add(new ListViewItem($"{(step.Done ? "✓" : "○")}  {step.Title}")
            {
                Tag = step,
                ForeColor = step.Done ? SystemColors.GrayText : SystemColors.ControlText
            });
        }

        var next = SetupSteps.Next(steps);

        // Der Blick fällt auf das, was noch aussteht — nicht auf eine Liste, in
        // der man sich selbst zurechtfinden muss.
        if (next is not null)
        {
            _steps.Items[steps.ToList().IndexOf(next)].Selected = true;
        }

        _steps.Focus();
    }

    private void ShowSelectedStep()
    {
        if (_steps.SelectedItems.Count == 0 || _steps.SelectedItems[0].Tag is not SetupStep step)
        {
            return;
        }

        _explanation.Text = step.Explanation;
        _act.Text = ActionLabel(step.Title);
        _act.Enabled = !step.Done && _act.Text.Length > 0;
    }

    private static string ActionLabel(string title) => title switch
    {
        "Tailscale installieren" => "Tailscale herunterladen",
        "Bei Tailscale anmelden" => "Jetzt anmelden",
        "Zertifikat holen" => "Zertifikat holen",

        // Der Dienst wird vom Installer eingerichtet, nicht von hier: dazu
        // braucht es Adminrechte, die dieses Fenster bewusst nicht hat.
        _ => string.Empty
    };

    private async Task ActOnSelectedStepAsync()
    {
        if (_steps.SelectedItems.Count == 0 || _steps.SelectedItems[0].Tag is not SetupStep step)
        {
            return;
        }

        _act.Enabled = false;

        try
        {
            await Task.Run(() => Perform(step.Title));
        }
        catch (Exception failure)
        {
            MessageBox.Show(
                $"Das hat nicht geklappt.\n\n{failure.Message}",
                "RemoteDesktop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        RefreshSteps();
    }

    private void Perform(string title)
    {
        switch (title)
        {
            case "Tailscale installieren":
                // Heruntergeladen statt mitgeliefert: eine mitgelieferte Fassung
                // veraltet im Repo, und Tailscale ist ein fremdes Programm mit
                // eigenem Aktualisierungsweg.
                Process.Start(new ProcessStartInfo(TailscaleDownload) { UseShellExecute = true })
                    ?.Dispose();
                break;

            case "Bei Tailscale anmelden":
                WindowsAutostart.Run("tailscale.exe", CurrentCoordinator().UpArguments());
                break;

            case "Zertifikat holen":
                WindowsAutostart.Run("tailscale.exe", ["cert", _probe.TailnetName]);
                break;
        }
    }

    private void RefreshAutostart()
    {
        var current = Autostart.Read(_autostart);

        if (_modes.TryGetValue(current, out var button))
        {
            button.Checked = true;
        }
    }

    private void ApplyAutostart(AutostartMode mode)
    {
        try
        {
            Autostart.Apply(_autostart, mode);
        }
        catch (Exception failure)
        {
            // Den Starttyp eines Dienstes zu ändern verlangt Adminrechte. Ohne
            // sie bleibt die Einstellung, wie sie war — das muss dastehen, sonst
            // glaubt jemand, er habe etwas eingestellt.
            MessageBox.Show(
                "Die Einstellung ließ sich nicht speichern. Für den Agent-Dienst braucht es "
                + $"Administratorrechte.\n\n{failure.Message}",
                "RemoteDesktop",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            RefreshAutostart();
        }
    }

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RemoteDesktopAgent",
        CoordinatorConfig.FileName);

    private Coordinator CurrentCoordinator() => CoordinatorConfig.Read(ReadConfigFile());

    private static string? ReadConfigFile() =>
        File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;

    private void LoadCoordinator()
    {
        var coordinator = CurrentCoordinator();

        _coordinator.Text = coordinator.Address;
        _coordinatorHint.Text = coordinator.IsTailscale
            ? "Vorgabe: der Dienst von Tailscale. Wer einen eigenen Koordinator betreibt, "
              + "trägt ihn hier ein."
            : "Eigener Koordinator.";
    }

    private void SaveCoordinator()
    {
        var coordinator = Coordinator.From(_coordinator.Text);
        var rejection = coordinator.Rejection;

        if (rejection is not null)
        {
            _coordinatorHint.Text = rejection;
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, CoordinatorConfig.Write(coordinator));

            _coordinatorHint.Text = "Gespeichert. Sie gilt beim nächsten Anmelden an Tailscale.";
        }
        catch (Exception failure)
        {
            _coordinatorHint.Text = $"Nicht gespeichert: {failure.Message}";
        }
    }
}
