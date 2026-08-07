using System.Net;
using System.Text.RegularExpressions;

namespace RemoteDesktopSetup;

/// <summary>
/// Wie Handy und Rechner zueinander finden.
///
/// **Tailscale ist eine Möglichkeit, keine Voraussetzung.** Wer den Rechner nur
/// aus dem eigenen WLAN steuern will, braucht überhaupt kein VPN — und wer schon
/// eines betreibt, soll seines behalten dürfen. Beides war bis Release v1.0.0
/// nicht vorgesehen: der Agent beendete sich ohne Tailscale-Zertifikat sofort
/// wieder.
/// </summary>
public enum NetworkKind
{
    /// <summary>
    /// Dasselbe Netz, kein VPN. Der Rechner steht zuhause, das Handy hängt im
    /// WLAN — mehr braucht es nicht.
    /// </summary>
    Lan,

    /// <summary>Tailscale. Die Empfehlung, sobald es auch von unterwegs gehen soll.</summary>
    Tailscale,

    /// <summary>
    /// Ein fremdes VPN — WireGuard, OpenVPN, ZeroTier, was auch immer.
    /// RemoteDesktop richtet es nicht ein und prüft es nicht; es benutzt nur die
    /// Adresse, die dort gilt. Die Anleitung dazu steht in <c>docs/NETZ.md</c>.
    /// </summary>
    Vpn
}

/// <summary>
/// Der gewählte Modus samt der Adresse, unter der dieser Rechner erreichbar ist.
/// </summary>
/// <param name="Address">
/// Bei <see cref="NetworkKind.Lan"/> und <see cref="NetworkKind.Vpn"/> Pflicht.
///
/// Bei Tailscale freiwillig: normalerweise steht der Name im Zertifikat, das
/// <c>tailscale cert</c> ausstellt. Wer keins geholt hat, bekommt ein selbst
/// ausgestelltes — und darin steht der Windows-Rechnername, nicht der Name im
/// Tailnet. Genau der landete dann im QR-Code, und das Handy suchte einen
/// Rechner, den es unter dem Namen nirgends gibt. Steht hier etwas, gilt es.
/// </param>
public sealed partial record NetworkProfile(NetworkKind Kind, string Address, Coordinator Coordinator)
{
    /// <summary>
    /// Die Vorgabe für eine Installation, über die noch niemand entschieden hat.
    ///
    /// Tailscale und nicht LAN: eine bestehende Installation, die vor V3
    /// eingerichtet wurde, hat keinen Eintrag in der Datei und läuft über
    /// Tailscale. Sie darf durch ein Update nicht stumm den Modus wechseln.
    /// </summary>
    public static NetworkProfile Default { get; } =
        new(NetworkKind.Tailscale, string.Empty, Coordinator.Default);

    /// <summary>Ob in diesem Modus überhaupt Tailscale eingerichtet werden muss.</summary>
    public bool NeedsTailscale => Kind == NetworkKind.Tailscale;

    /// <summary>Ob der Nutzer die Adresse selbst nennen muss.</summary>
    public bool NeedsOwnAddress => Kind is NetworkKind.Lan or NetworkKind.Vpn;

    /// <summary>
    /// Die Adresse, auf die das Zertifikat lauten muss und die im QR-Code steht —
    /// <c>null</c>, solange keine eingetragen ist.
    ///
    /// Sie gilt in jedem Modus. Bei Tailscale ist sie freiwillig, schlägt aber
    /// den Namen aus dem Zertifikat: ein selbst ausgestelltes trägt den
    /// Windows-Rechnernamen, und unter dem findet das Handy im Tailnet nichts.
    /// </summary>
    public string? AdvertisedAddress =>
        Address.Trim().Length > 0 ? Address.Trim() : null;

    /// <summary>
    /// Warum dieses Profil nicht taugt — <c>null</c>, wenn es taugt.
    ///
    /// Geprüft wird vor allem, was Menschen tatsächlich eintragen: die ganze
    /// Adresse mit <c>https://</c> davor und <c>:8443</c> dahinter. Beides ergäbe
    /// einen Zertifikatsnamen, den kein Client je vorzeigt, und der Fehler fiele
    /// erst beim Verbinden auf.
    /// </summary>
    public string? Rejection
    {
        get
        {
            if (Coordinator.Rejection is { } coordinator && Kind == NetworkKind.Tailscale)
            {
                return coordinator;
            }

            if (NeedsOwnAddress)
            {
                return RejectAddress(Address);
            }

            // Bei Tailscale darf das Feld leer bleiben — was aber daraufsteht,
            // muss taugen: es geht unverändert ins Zertifikat und in den QR-Code.
            return AdvertisedAddress is null ? null : RejectAddress(Address);
        }
    }

    /// <summary>Warum diese Adresse nicht taugt — <c>null</c>, wenn sie taugt.</summary>
    public static string? RejectAddress(string? address)
    {
        var trimmed = (address ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return "Trage die Adresse ein, unter der dieser Rechner erreichbar ist — "
                   + "etwa 192.168.178.20 oder pc.fritz.box.";
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return "Nur der Name oder die Adresse, ohne https:// davor.";
        }

        if (trimmed.Contains('/'))
        {
            return "Nur der Name oder die Adresse, ohne Pfad dahinter.";
        }

        // Eine nackte IPv6-Adresse enthält Doppelpunkte und sähe sonst aus wie
        // ein angehängter Port. Deshalb wird sie zuerst gefragt.
        if (IPAddress.TryParse(trimmed.Trim('[', ']'), out _))
        {
            return null;
        }

        if (trimmed.Contains(':'))
        {
            return "Ohne Port — der steht schon in den Einstellungen des Agents.";
        }

        return Hostname().IsMatch(trimmed)
            ? null
            : $"„{trimmed}“ ist weder ein Rechnername noch eine IP-Adresse.";
    }

    /// <summary>Ein Satz für die Oberfläche, kein Aufzählungsname.</summary>
    public string Describe() => Kind switch
    {
        NetworkKind.Lan =>
            "Nur im eigenen Netz — Handy und Rechner hängen am selben Router. "
            + "Kein VPN nötig, dafür geht es von unterwegs nicht.",
        NetworkKind.Vpn =>
            "Über dein eigenes VPN. RemoteDesktop benutzt nur die Adresse, die dort gilt; "
            + "Verbindung und Einrichtung bleiben deine Sache.",
        _ =>
            "Über Tailscale — verbindet deine Geräte direkt miteinander, auch von unterwegs, "
            + "ohne dass du am Router etwas freigeben musst."
    };

    /// <summary>
    /// Kleinbuchstaben und ohne Klammern: so steht der Name später im Zertifikat
    /// und im QR-Code. Wer „PC.Fritz.Box" einträgt, soll nicht an der
    /// Groß-/Kleinschreibung scheitern.
    /// </summary>
    public NetworkProfile Normalized() => this with
    {
        Address = Address.Trim().Trim('[', ']').ToLowerInvariant()
    };

    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)*$")]
    private static partial Regex Hostname();
}

/// <summary>
/// Die Datei, in der das Netzprofil steht — dieselbe <c>setup.json</c>, die
/// bisher nur den Koordinator führte.
///
/// Sie liegt bei den Daten des Agents und nicht neben der <c>.exe</c>: Agent und
/// Oberfläche lesen sie beide, und nur eine der beiden wird bei einem Update
/// ersetzt.
/// </summary>
public static class NetworkConfig
{
    public const string FileName = CoordinatorConfig.FileName;

    /// <summary>
    /// Liest das Profil. Alles, was nicht lesbar ist, ergibt die Vorgabe: eine
    /// beschädigte Textdatei darf weder die Einrichtung verhindern noch den
    /// Agent am Starten hindern.
    ///
    /// Eine Datei ohne <c>network</c> stammt aus der Zeit vor V3 und meint
    /// Tailscale.
    /// </summary>
    public static NetworkProfile Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return NetworkProfile.Default;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;

            var coordinator = root.TryGetProperty("coordinator", out var address)
                ? Coordinator.From(address.GetString())
                : Coordinator.Default;

            var kind = root.TryGetProperty("network", out var network)
                ? ParseKind(network.GetString())
                : NetworkKind.Tailscale;

            var own = root.TryGetProperty("address", out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;

            return new NetworkProfile(kind, own, coordinator);
        }
        catch (System.Text.Json.JsonException)
        {
            return NetworkProfile.Default;
        }
    }

    public static string Write(NetworkProfile profile) =>
        System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["network"] = profile.Kind.ToString().ToLowerInvariant(),
                ["address"] = profile.Address.Trim(),
                ["coordinator"] = profile.Coordinator.Address
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Ein unbekannter Wert ergibt Tailscale und keinen Fehler. Die Datei wird
    /// von Hand bearbeitbar angeboten; ein Tippfehler darf nicht den Dienst
    /// kosten.
    /// </summary>
    private static NetworkKind ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "lan" => NetworkKind.Lan,
        "vpn" => NetworkKind.Vpn,
        _ => NetworkKind.Tailscale
    };
}
