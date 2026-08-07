using RemoteDesktopClient.Ui;
using RemoteDesktopSetup;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Wie das Handy diesen Rechner erreicht — Heimnetz, Tailscale oder ein eigenes
/// VPN.
///
/// <para>
/// Die Wahl gehört hierher und nicht in den Installer: sie ändert sich, wenn der
/// Rechner umzieht oder jemand doch von unterwegs ranwill. Was die Modi bedeuten
/// und wie man ein fremdes VPN einrichtet, steht in <c>docs/NETZ.md</c>.
/// </para>
/// </summary>
public sealed class NetworkPage : PageView
{
    private const string Guide =
        "https://github.com/Davidodos/RemoteDesktop/blob/master/docs/NETZ.md";

    private readonly WindowsProbe _probe;
    private readonly ChoiceGroup<NetworkKind> _kinds = new();
    private readonly ThemedTextBox _address = new("z. B. 192.168.178.33");
    private readonly TextBlock _addressHint = new(string.Empty);
    private readonly ThemedTextBox _coordinator = new(Coordinator.Default.Address);
    private readonly ThemedTextBox _trustHost = new("Adresse des anderen Rechners");
    private readonly ThemedButton _suggest = new("Vorschlag");
    private readonly TextBlock _explanation = new(string.Empty);

    /// <summary>
    /// Der Platz für die Rückfrage beim Vertrauen. Er ist meist leer und wird
    /// gefüllt, statt die Karte neu zu bauen — die Karte enthält Felder, in die
    /// jemand gerade getippt hat.
    /// </summary>
    private readonly Stack _confirm = new() { Gap = 10 };

    private NetworkKind _chosen = NetworkKind.Lan;
    private FetchedCertificate? _fetched;

    public NetworkPage(WindowsProbe probe)
        : base("Netz", "Auf welchem Weg dein Handy diesen Rechner findet.")
    {
        _probe = probe;

        _kinds.Add(
            NetworkKind.Lan,
            "Heimnetz",
            "Handy und Rechner hängen am selben Router. Nichts zu installieren.");

        _kinds.Add(
            NetworkKind.Tailscale,
            "Tailscale",
            "Auch von unterwegs. Braucht das Programm Tailscale auf beiden Seiten.");

        _kinds.Add(
            NetworkKind.Vpn,
            "Eigenes VPN",
            "Du hast schon eins — WireGuard auf der Fritzbox oder etwas Ähnliches.");

        _kinds.Chosen += Choose;
        _suggest.Click += async (_, _) => await SuggestAsync();

        Body.Add(ModeCard());
        Body.Add(AddressCard());
        Body.Add(TrustCard());
    }

    public override Task RefreshAsync()
    {
        var profile = NetworkStore.Read();

        _chosen = profile.Kind;
        _kinds.Select(profile.Kind);
        _address.Value = profile.Address;
        _coordinator.Value = profile.Coordinator.Address;

        ApplyMode();

        return Task.CompletedTask;
    }

    private Card ModeCard()
    {
        var card = new Card("Wie dein Handy diesen Rechner erreicht");

        var guide = new ThemedButton("Anleitung öffnen");
        guide.Click += (_, _) => OverviewPage.Open(Guide);

        card.Body.Add(_kinds);
        card.Body.Add(_explanation);
        card.Body.Add(Row.Buttons(guide));

        return card;
    }

    private Card AddressCard()
    {
        var card = new Card("Adresse dieses Rechners");
        var save = new ThemedButton("Übernehmen", ButtonTone.Primary);

        save.Click += (_, _) => Save();

        card.Body.Add(_addressHint);
        card.Body.Add(Row.Fill(_address, _suggest));

        card.Body.Add(new TextBlock(
            "Eigener Koordinator (Headscale) — nur, wenn du Tailscale nicht über "
            + "deren Server betreibst.", Theme.Body, Theme.TextDim));

        card.Body.Add(_coordinator);
        card.Body.Add(Row.Buttons(save));

        return card;
    }

    /// <summary>
    /// Einem anderen Rechner vertrauen, der sich sein Zertifikat selbst
    /// ausgestellt hat.
    ///
    /// Es steht hier und nicht bei den Teilen: es geht nicht um diesen Rechner,
    /// sondern um einen, den man von hier aus steuern will.
    /// </summary>
    private Card TrustCard()
    {
        var card = new Card("Einem anderen Rechner vertrauen");
        var fetch = new ThemedButton("Zertifikat holen");

        fetch.Click += async (_, _) => await FetchAsync();

        card.Body.Add(new TextBlock(
            "Hat der andere Rechner sich sein Zertifikat selbst ausgestellt, kennt "
            + "Windows den Aussteller nicht. Hier wird er einmalig eingetragen — "
            + "nachdem du den Fingerabdruck mit dem verglichen hast, der dort im "
            + "Fenster steht."));

        card.Body.Add(Row.Fill(_trustHost, fetch));
        card.Body.Add(_confirm);

        return card;
    }

    private void Choose(NetworkKind kind)
    {
        _chosen = kind;
        ApplyMode();
    }

    /// <summary>
    /// Was zu diesem Modus gehört, wird bedienbar; der Rest wird gesperrt statt
    /// versteckt. Dass es die Möglichkeit gäbe, ist selbst eine Auskunft.
    /// </summary>
    private void ApplyMode()
    {
        // Die Adresse gilt in jedem Modus. Bei Tailscale ist sie freiwillig,
        // aber sie ist der einzige Weg, den Namen im Tailnet in den QR-Code zu
        // bekommen, solange kein Zertifikat von Tailscale danebenliegt: ein
        // selbst ausgestelltes trägt den Windows-Rechnernamen, und unter dem
        // findet das Handy nichts.
        _address.Enabled = true;
        _suggest.Enabled = _chosen is NetworkKind.Lan or NetworkKind.Tailscale;
        _coordinator.Enabled = _chosen == NetworkKind.Tailscale;

        _address.Placeholder = _chosen == NetworkKind.Tailscale
            ? "z. B. pc.tailnet-1234.ts.net"
            : "z. B. 192.168.178.33";

        _addressHint.Retext(_chosen switch
        {
            NetworkKind.Tailscale =>
                "Der Name dieses Rechners im Tailnet. Genau er steht später im QR-Code, "
                + "und genau ihn muss das Handy auflösen können. „Vorschlag“ liest ihn "
                + "aus Tailscale aus.",
            NetworkKind.Vpn =>
                "Unter dieser Adresse trägt sich der Agent bei den gekoppelten Geräten "
                + "ein — die, die in deinem VPN gilt.",
            _ =>
                "Unter dieser Adresse trägt sich der Agent bei den gekoppelten Geräten "
                + "ein. Im Heimnetz ist es die Adresse vom Router."
        });

        if (_chosen == NetworkKind.Lan && _address.Value.Trim().Length == 0)
        {
            _address.Value = NetworkStore.Guess() ?? string.Empty;
        }

        _explanation.Retext(
            new NetworkProfile(_chosen, _address.Value, Coordinator.Default).Describe());
    }

    /// <summary>
    /// Der Vorschlag kommt aus dem Modus: im Heimnetz die eigene IP, bei
    /// Tailscale der Name aus <c>tailscale status</c>. Beides wird abgefragt und
    /// nicht geraten — im eigenen VPN weiß RemoteDesktop nichts, deshalb ist der
    /// Knopf dort aus.
    /// </summary>
    private async Task SuggestAsync()
    {
        if (_chosen == NetworkKind.Tailscale)
        {
            // Der Aufruf von tailscale.exe darf das Fenster nicht anhalten.
            _probe.Forget();

            var name = await Task.Run(() => _probe.TailnetName);

            if (name.Length == 0)
            {
                Report(
                    "Tailscale meldet für diesen Rechner keinen Namen — läuft es, und ist "
                    + "dieser Rechner angemeldet?",
                    Tone.Bad);

                return;
            }

            _address.Value = name;
            Report($"Gefunden: {name}.", Tone.Good);

            return;
        }

        var guess = NetworkStore.Guess();

        if (guess is null)
        {
            Report("Hier ist gerade keine Netzwerkverbindung zu finden.", Tone.Bad);

            return;
        }

        _address.Value = guess;
        Report($"Gefunden: {guess}.", Tone.Good);
    }

    private void Save()
    {
        var profile = new NetworkProfile(
            _chosen, _address.Value, Coordinator.From(_coordinator.Value)).Normalized();

        if (profile.Rejection is { } rejection)
        {
            _explanation.Retext(rejection);
            Report(rejection, Tone.Bad);

            return;
        }

        var result = NetworkStore.Write(profile);

        Report(
            result.Ok
                ? "Gespeichert. Der Agent übernimmt es beim nächsten Start — "
                  + "unter „Übersicht“ einmal beenden und starten."
                : $"Nicht gespeichert: {result.Message}",
            result.Ok ? Tone.Good : Tone.Bad);
    }

    /// <summary>
    /// Holen, zeigen, fragen — in dieser Reihenfolge. Der Fingerabdruck steht in
    /// der Rückfrage, weil er das Einzige ist, was den Schritt sicher macht:
    /// derselbe Wert steht am anderen Rechner im Fenster. Stimmen sie nicht
    /// überein, sitzt jemand dazwischen.
    /// </summary>
    private async Task FetchAsync()
    {
        var host = _trustHost.Value.Trim();

        if (host.Length == 0)
        {
            Report("Trage die Adresse des Rechners ein, dem du vertrauen willst.", Tone.Bad);

            return;
        }

        try
        {
            _fetched = await TrustImport.FetchAsync(host);
        }
        catch (Exception failure)
        {
            _confirm.Clear();
            Report($"Nicht geklappt: {failure.Message}", Tone.Bad);

            return;
        }

        var value = new ThemedTextBox { Value = _fetched.Readable, ReadOnly = true };
        value.UseMonospace();

        var accept = new ThemedButton("Stimmt überein — vertrauen", ButtonTone.Primary);
        var cancel = new ThemedButton("Abbrechen");

        accept.Click += (_, _) => Accept(host);

        cancel.Click += (_, _) =>
        {
            _confirm.Clear();
            Report("Nichts geändert.");
        };

        _confirm.Clear();

        _confirm.Add(new TextBlock(
            $"Dieses Zertifikat gehört angeblich zu „{host}“. Vergleiche den Wert mit "
            + "dem, der am anderen Rechner unter „Übersicht“ steht. Nur wenn beide "
            + "übereinstimmen, gehört es dorthin.", Theme.Body, Theme.Text));

        _confirm.Add(value);
        _confirm.Add(Row.Buttons(accept, cancel));

        Report("Vergleiche den Fingerabdruck, bevor du bestätigst.", Tone.Working);
    }

    private void Accept(string host)
    {
        if (_fetched is not { } certificate)
        {
            return;
        }

        try
        {
            TrustImport.Trust(certificate.Certificate);
            Report($"„{host}“ wird jetzt vertraut.", Tone.Good);
        }
        catch (Exception failure)
        {
            Report($"Nicht geklappt: {failure.Message}", Tone.Bad);
        }

        _confirm.Clear();
    }
}
