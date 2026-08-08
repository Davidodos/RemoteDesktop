using RemoteDesktopClient.Ui;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Geräte koppeln und wieder aussperren.
///
/// <para>
/// Beides geht nur hier, weil der Agent diese beiden Endpunkte auf die
/// Loopback-Adresse beschränkt. Wer koppeln oder widerrufen will, muss am
/// Rechner sitzen — über das Netz wäre es genau der Weg, den die Kopplung
/// verhindern soll.
/// </para>
///
/// <para>
/// Das Widerrufen fragt inzwischen *in* der Karte nach und nicht mehr in einem
/// Meldungsfenster. Ein Fenster, das sich über das Fenster legt, ist die
/// Bauweise, von der dieser Umbau wegwill — und die Rückfrage ist hier so
/// deutlich wie dort.
/// </para>
/// </summary>
public sealed class DevicesPage : PageView
{
    private readonly LocalAgent _agent;

    private readonly TextBlock _code = new(
        "— — —   — — —", Theme.Code, Theme.Text, centered: true);

    /// <summary>
    /// Kantenlänge des QR-Bildes in logischen Punkten.
    ///
    /// <para>
    /// **Der Befund dahinter:** die Kantenlänge kam aus der Höhe des
    /// Bildfeldes (<c>_qr.Height - 24</c>). Steht das Feld gerade auf 0 —
    /// und das tut es, solange es noch nie angeordnet wurde —, ergibt das
    /// <c>-24</c>, und die Erzeugung wirft. Genau das stand am echten Gerät
    /// im Fenster. Eine feste Größe kann das nicht passieren; nebenbei ist
    /// der Code auf einem hochauflösenden Bildschirm damit doppelt so scharf.
    /// </para>
    /// </summary>
    private const int QrSide = 200;

    private readonly PictureBox _qr = new()
    {
        SizeMode = PictureBoxSizeMode.CenterImage,
        BackColor = Theme.Field,
        Visible = false
    };

    private readonly TextBlock _hint = new(
        "Auf „Code anzeigen“ klicken, dann am Handy scannen oder eintippen.");

    /// <summary>
    /// Die Liste wird bei jedem Anzeigen neu gefüllt, die Karte darum aber
    /// nicht neu gebaut — sonst verschwände der eben erzeugte Kopplungscode,
    /// während jemand ihn abtippt.
    /// </summary>
    private readonly Card _paired = new("Gekoppelte Geräte");

    private string? _pendingRevoke;

    /// <summary>
    /// Woran die Seite erkennt, dass sich an der Liste nichts geändert hat.
    /// Sie sieht im Takt des Fensters nach — ohne diesen Vergleich baute sie
    /// die Zeilen alle zwei Sekunden neu, mitsamt dem Knopf, den gerade jemand
    /// drückt.
    /// </summary>
    private string? _shown;

    public DevicesPage(LocalAgent agent)
        : base("Geräte", "Welche Geräte diesen Rechner steuern dürfen.")
    {
        _agent = agent;

        var issue = new ThemedButton("Code anzeigen", ButtonTone.Primary);

        issue.Click += async (_, _) => await IssueAsync();

        var pairing = new Card("Neues Gerät koppeln");

        pairing.Body.Add(_code);
        pairing.Body.Add(_qr);
        pairing.Body.Add(_hint);
        pairing.Body.Add(Row.Buttons(issue));

        Body.Add(pairing);
        Body.Add(_paired);
    }

    public override Task RefreshAsync() => FillAsync(_paired);

    /// <summary>
    /// Ein frisch gekoppeltes Handy soll auftauchen, während man noch auf diese
    /// Seite schaut — und nicht erst, wenn man einmal woandershin und zurück
    /// geklickt hat.
    /// </summary>
    public override bool LiveRefresh => true;

    private async Task IssueAsync()
    {
        try
        {
            var issued = await _agent.IssueCodeAsync(CancellationToken.None);

            // Mit Abstand in der Mitte — sechs Ziffern am Stück liest niemand
            // fehlerfrei vom Bildschirm ab.
            _code.Retext($"{issued.Code[..3]}   {issued.Code[3..]}");

            _hint.Retext(
                $"Gilt {issued.ExpiresInSeconds / 60} Minuten und nur ein einziges Mal.");

            ShowQr(issued.PairingUri);
            Report("Der Code steht bereit.", Tone.Good);
        }
        catch (Exception failure)
        {
            Report(Unreachable(failure), Tone.Bad);
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
            var image = QrImage.Render(pairingUri, LogicalToDeviceUnits(QrSide));

            _qr.Image = image;

            // Das Feld richtet sich nach dem Bild und nicht umgekehrt: die
            // Kantenlänge ist ein glattes Vielfaches der Modulzahl und deshalb
            // selten genau die gewünschte.
            _qr.Height = image.Height + LogicalToDeviceUnits(24);
            _qr.Visible = true;

            Stack.Reflow(_qr);
        }
        catch (Exception failure)
        {
            // Ein Code, der sich nicht zeichnen lässt, darf die Kopplung nicht
            // aufhalten — die sechs Ziffern darüber stehen ja da.
            _qr.Visible = false;
            _hint.Retext($"Der QR-Code ließ sich nicht erzeugen: {failure.Message}");
        }
    }

    private async Task FillAsync(Card card)
    {
        IReadOnlyList<PairedClientInfo> clients = [];
        string? complaint = null;

        try
        {
            clients = await _agent.ListClientsAsync(CancellationToken.None);
        }
        catch (Exception failure)
        {
            complaint = Unreachable(failure);
        }

        var state = complaint ?? string.Join(
            '|',
            clients.Select(client =>
                $"{client.Id}:{client.Label}:{client.LastSeenAt:O}:{_pendingRevoke == client.Id}"));

        if (state == _shown)
        {
            return;
        }

        _shown = state;

        card.Body.Clear();

        if (complaint is not null)
        {
            card.Body.Add(new TextBlock(complaint, Theme.Body, Theme.Danger));

            return;
        }

        if (clients.Count == 0)
        {
            card.Body.Add(new TextBlock(
                "Noch keins. Oben einen Code erzeugen und am Handy eingeben."));

            return;
        }

        foreach (var client in clients)
        {
            card.Body.Add(Entry(card, client));
        }
    }

    /// <summary>
    /// Ein gekoppeltes Gerät als Zeile: Name, Rechte, zuletzt gesehen — und der
    /// Knopf, der es aussperrt.
    /// </summary>
    private Control Entry(Card card, PairedClientInfo client)
    {
        var pending = _pendingRevoke == client.Id;

        var text = new TextBlock(
            $"{client.Label}\n{string.Join(", ", client.Scopes)} · zuletzt "
            + $"{client.LastSeenAt.ToLocalTime():g}",
            Theme.Body,
            Theme.Text);

        var revoke = new ThemedButton(
            pending ? "Wirklich aussperren" : "Widerrufen", ButtonTone.Danger);

        revoke.Click += async (_, _) =>
        {
            if (!pending)
            {
                _pendingRevoke = client.Id;

                Report(
                    $"„{client.Label}“ verliert damit sofort den Zugang. "
                    + "Noch einmal klicken, wenn es so sein soll.",
                    Tone.Bad);

                await FillAsync(card);

                return;
            }

            _pendingRevoke = null;

            try
            {
                await _agent.RevokeAsync(client.Id, CancellationToken.None);
                Report($"„{client.Label}“ ist ausgesperrt.", Tone.Good);
            }
            catch (Exception failure)
            {
                Report(Unreachable(failure), Tone.Bad);
            }

            await FillAsync(card);
        };

        return Row.Fill(text, revoke);
    }

    /// <summary>
    /// Der häufigste Fall ist nicht ein Fehler im Code, sondern ein Agent, der
    /// gar nicht läuft. Das soll die Meldung sagen.
    /// </summary>
    private static string Unreachable(Exception failure) =>
        $"Der Agent auf diesem Rechner antwortet nicht — läuft der Dienst? ({failure.Message})";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qr.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
