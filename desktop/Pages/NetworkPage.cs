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
    private readonly ThemedButton _suggest = new("Vorschlag");
    private readonly TextBlock _explanation = new(string.Empty);

    private NetworkKind _chosen = NetworkKind.Lan;

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
            "Anderer VPN-Anbieter",
            "Du hast schon eins — WireGuard, Headscale oder etwas Ähnliches.");

        _kinds.Chosen += Choose;
        _suggest.Click += async (_, _) => await SuggestAsync();

        Body.Add(ModeCard());
        Body.Add(AddressCard());
    }

    public override Task RefreshAsync()
    {
        var profile = NetworkStore.Read();

        // Headscale steht nicht mehr zur Wahl — RemoteDesktop braucht dort nur
        // die Adresse, und das ist „Anderer VPN-Anbieter".
        _chosen = profile.Kind == NetworkKind.Headscale ? NetworkKind.Vpn : profile.Kind;
        _kinds.Select(_chosen);
        _address.Value = profile.Address;

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
        card.Body.Add(Row.Buttons(save));

        return card;
    }

    private void Choose(NetworkKind kind)
    {
        _chosen = kind;
        ApplyMode();
    }

    /// <summary>
    /// Zu sehen ist, was zum gewählten Modus gehört — und sonst nichts.
    /// </summary>
    private void ApplyMode()
    {
        // Die Adresse gilt in jedem Modus und ist in jedem Pflicht: sie ist der
        // Name, den der Agent bei den gekoppelten Geräten hinterlegt und der im
        // QR-Code steht. Vorschlagen lässt sie sich nur dort, wo es etwas
        // abzufragen gibt — im fremden VPN weiß RemoteDesktop nichts.
        _suggest.Enabled = _chosen != NetworkKind.Vpn;

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
        var profile = new NetworkProfile(_chosen, _address.Value, Coordinator.Default).Normalized();

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
}
