using System.Diagnostics;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Wie das Handy diesen Rechner erreicht — Heimnetz, Tailscale oder ein eigenes
/// VPN.
///
/// Die Wahl gehört hierher und nicht in den Installer: sie ändert sich, wenn der
/// Rechner umzieht oder jemand doch von unterwegs ranwill. Was die Modi bedeuten
/// und wie man ein fremdes VPN einrichtet, steht in <c>docs/NETZ.md</c>.
/// </summary>
public sealed class NetworkView : UserControl
{
    private const string Guide =
        "https://github.com/Davidodos/RemoteDesktop/blob/master/docs/NETZ.md";

    private readonly Action<string> _report;

    private readonly Dictionary<NetworkKind, RadioButton> _kinds = [];
    private readonly TextBox _address = new() { Dock = DockStyle.Fill };
    private readonly TextBox _coordinator = new() { Dock = DockStyle.Fill };
    private readonly Label _hint = new() { Dock = DockStyle.Fill, AutoSize = false, Height = 64 };
    private readonly Button _suggest = new() { Text = "Vorschlag", AutoSize = true };

    private bool _loading;

    public NetworkView(Action<string> report)
    {
        _report = report;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, AutoScroll = true
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildKinds(), 0, 0);
        layout.Controls.Add(BuildAddress(), 0, 1);
        layout.Controls.Add(BuildCoordinator(), 0, 2);
        layout.Controls.Add(BuildActions(), 0, 3);
        layout.Controls.Add(_hint, 0, 4);

        Controls.Add(layout);
    }

    private Control BuildKinds()
    {
        var box = new GroupBox
        {
            Text = "Wie dein Handy diesen Rechner erreicht", Dock = DockStyle.Fill, AutoSize = true
        };

        var inner = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(8)
        };

        foreach (var (kind, title) in new[]
                 {
                     (NetworkKind.Lan, "Heimnetz — Handy und Rechner am selben Router"),
                     (NetworkKind.Tailscale, "Tailscale — auch von unterwegs"),
                     (NetworkKind.Vpn, "Eigenes VPN — ich habe schon eins")
                 })
        {
            var button = new RadioButton { Text = title, AutoSize = true };

            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked && !_loading)
                {
                    Apply(kind);
                }
            };

            _kinds[kind] = button;
            inner.Controls.Add(button);
        }

        box.Controls.Add(inner);

        return box;
    }

    private Control BuildAddress()
    {
        var box = new GroupBox
        {
            Text = "Adresse dieses Rechners", Dock = DockStyle.Fill, AutoSize = true
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true,
            Padding = new Padding(8)
        };

        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _suggest.Click += (_, _) =>
        {
            var guess = NetworkStore.Guess();

            if (guess is null)
            {
                _hint.Text = "Hier ist gerade keine Netzwerkverbindung zu finden.";
                return;
            }

            _address.Text = guess;
        };

        inner.Controls.Add(_address, 0, 0);
        inner.Controls.Add(_suggest, 1, 0);

        box.Controls.Add(inner);

        return box;
    }

    private Control BuildCoordinator()
    {
        var box = new GroupBox
        {
            Text = "Eigener Koordinator (Headscale)", Dock = DockStyle.Fill, AutoSize = true
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, AutoSize = true,
            Padding = new Padding(8)
        };

        inner.Controls.Add(_coordinator, 0, 0);
        box.Controls.Add(inner);

        return box;
    }

    private Control BuildActions()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };

        var save = new Button { Text = "Übernehmen", AutoSize = true };
        save.Click += (_, _) => Save();

        var guide = new Button { Text = "Anleitung öffnen", AutoSize = true };
        guide.Click += (_, _) =>
            Process.Start(new ProcessStartInfo(Guide) { UseShellExecute = true })?.Dispose();

        row.Controls.Add(save);
        row.Controls.Add(guide);

        return row;
    }

    /// <summary>Zeigt, was gespeichert ist.</summary>
    public void Show(NetworkProfile profile)
    {
        _loading = true;

        _kinds[profile.Kind].Checked = true;
        _address.Text = profile.Address;
        _coordinator.Text = profile.Coordinator.Address;

        _loading = false;

        Apply(profile.Kind);
    }

    /// <summary>
    /// Was zu diesem Modus gehört, wird gezeigt; der Rest wird gesperrt statt
    /// versteckt. Dass es die Möglichkeit gäbe, ist selbst eine Auskunft.
    /// </summary>
    private void Apply(NetworkKind kind)
    {
        var needsAddress = kind is NetworkKind.Lan or NetworkKind.Vpn;

        _address.Enabled = needsAddress;
        _suggest.Enabled = kind == NetworkKind.Lan;
        _coordinator.Enabled = kind == NetworkKind.Tailscale;

        if (needsAddress && _address.Text.Trim().Length == 0 && kind == NetworkKind.Lan)
        {
            _address.Text = NetworkStore.Guess() ?? string.Empty;
        }

        _hint.Text = new NetworkProfile(kind, _address.Text, Coordinator.Default).Describe();
    }

    private void Save()
    {
        var chosen = _kinds.First(entry => entry.Value.Checked).Key;

        var profile = new NetworkProfile(
            chosen, _address.Text, Coordinator.From(_coordinator.Text)).Normalized();

        if (profile.Rejection is { } rejection)
        {
            _hint.Text = rejection;
            _report(rejection);

            return;
        }

        var result = NetworkStore.Write(profile);

        _report(result.Ok
            ? "Gespeichert. Der Agent übernimmt es beim nächsten Start — "
              + "unter „Übersicht“ einmal beenden und starten."
            : $"Nicht gespeichert: {result.Message}");
    }
}
