namespace RemoteDesktopClient;

/// <summary>
/// Die lokale Verwaltung: Kopplungscode anzeigen und Geräte widerrufen.
///
/// Beides geht nur hier, weil der Agent diese beiden Endpunkte auf die
/// Loopback-Adresse beschränkt. Wer koppeln oder widerrufen will, muss am
/// Rechner sitzen — über das Netz wäre es genau der Weg, den die Kopplung
/// verhindern soll.
/// </summary>
public sealed class PairingWindow : Form
{
    private readonly LocalAgent _agent;

    private readonly Label _code = new()
    {
        Dock = DockStyle.Top,
        Height = 70,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font(FontFamily.GenericSansSerif, 28, FontStyle.Bold),
        Text = "——————"
    };

    private readonly Label _hint = new()
    {
        Dock = DockStyle.Top,
        Height = 40,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Auf „Code anzeigen“ klicken, dann am Handy scannen oder eintippen."
    };

    /// <summary>
    /// Der QR-Code zum selben Kopplungscode. Er steht erst nach dem Klick da und
    /// belegt vorher keinen Platz — ein leerer weißer Kasten sähe aus wie ein
    /// Fehler.
    /// </summary>
    private readonly PictureBox _qr = new()
    {
        Dock = DockStyle.Top,
        Height = 240,
        SizeMode = PictureBoxSizeMode.CenterImage,
        Visible = false
    };

    private readonly ListView _clients = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false
    };

    public PairingWindow(LocalAgent agent)
    {
        _agent = agent;

        Text = "Geräte koppeln";
        Width = 640;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;

        _clients.Columns.Add("Gerät", 180);
        _clients.Columns.Add("Rechte", 240);
        _clients.Columns.Add("Zuletzt gesehen", 160);

        var issue = new Button { Text = "Code anzeigen", Dock = DockStyle.Left, Width = 160 };
        var revoke = new Button { Text = "Widerrufen", Dock = DockStyle.Right, Width = 160 };
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 40 };

        buttons.Controls.Add(issue);
        buttons.Controls.Add(revoke);

        issue.Click += async (_, _) => await IssueCodeAsync();
        revoke.Click += async (_, _) => await RevokeSelectedAsync();

        // Umgekehrte Reihenfolge: bei DockStyle.Top landet das zuletzt
        // hinzugefügte Element ganz oben.
        Controls.Add(_clients);
        Controls.Add(buttons);
        Controls.Add(_qr);
        Controls.Add(_hint);
        Controls.Add(_code);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshClientsAsync();
    }

    private async Task IssueCodeAsync()
    {
        try
        {
            var issued = await _agent.IssueCodeAsync(CancellationToken.None);

            // Mit Leerzeichen in der Mitte — sechs Ziffern am Stück liest
            // niemand fehlerfrei vom Bildschirm ab.
            _code.Text = $"{issued.Code[..3]} {issued.Code[3..]}";
            _hint.Text = $"Gilt {issued.ExpiresInSeconds / 60} Minuten und nur ein einziges Mal.";

            ShowQr(issued.PairingUri);
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    /// <summary>
    /// Zeichnet den QR-Code, wenn der Agent einen Link mitgeschickt hat.
    ///
    /// Tut er das nicht — etwa weil auf diesem Rechner noch eine ältere Fassung
    /// läuft —, bleibt es beim abgetippten Code. Das ist kein Fehler, sondern
    /// der Weg, der vorher der einzige war.
    /// </summary>
    private void ShowQr(string? pairingUri)
    {
        _qr.Image?.Dispose();
        _qr.Image = null;

        if (string.IsNullOrWhiteSpace(pairingUri))
        {
            _qr.Visible = false;
            return;
        }

        try
        {
            _qr.Image = QrImage.Render(pairingUri, _qr.Height - 20);
            _qr.Visible = true;
        }
        catch (Exception ex)
        {
            // Ein Code, der sich nicht zeichnen lässt, darf die Kopplung nicht
            // aufhalten — die sechs Ziffern darüber stehen ja da.
            _qr.Visible = false;
            _hint.Text = $"Der QR-Code ließ sich nicht erzeugen: {ex.Message}";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qr.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RefreshClientsAsync()
    {
        try
        {
            var clients = await _agent.ListClientsAsync(CancellationToken.None);

            _clients.Items.Clear();

            foreach (var client in clients)
            {
                var item = new ListViewItem(client.Label) { Tag = client.Id };

                item.SubItems.Add(string.Join(", ", client.Scopes));
                item.SubItems.Add(client.LastSeenAt.ToLocalTime().ToString("g"));

                _clients.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async Task RevokeSelectedAsync()
    {
        if (_clients.SelectedItems.Count == 0 ||
            _clients.SelectedItems[0].Tag is not string id)
        {
            return;
        }

        var label = _clients.SelectedItems[0].Text;

        var confirmed = MessageBox.Show(
            $"„{label}“ verliert damit sofort den Zugang zu diesem Rechner.",
            "Gerät widerrufen",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirmed != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _agent.RevokeAsync(id, CancellationToken.None);
            await RefreshClientsAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    /// <summary>
    /// Der häufigste Fall ist nicht ein Fehler im Code, sondern ein Agent, der
    /// gar nicht läuft. Das soll die Meldung sagen.
    /// </summary>
    private static void ShowFailure(Exception ex)
    {
        MessageBox.Show(
            "Der Agent auf diesem Rechner antwortet nicht.\n\n" +
            "Läuft der Dienst RemoteDesktopAgent?\n\n" +
            ex.Message,
            "Keine Verbindung zum Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
