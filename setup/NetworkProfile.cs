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
    Vpn,

    /// <summary>
    /// Headscale — derselbe Tailscale-Client, aber am eigenen Koordinator statt
    /// an dem der Firma.
    ///
    /// <para>
    /// **Warum es ein eigener Modus ist und kein Feld unter „Tailscale":** bis
    /// v1.3.0 stand die Koordinator-Adresse als Zusatzfeld im Tailscale-Schritt.
    /// Wer sie ausfüllte, hatte damit still etwas anderes gewählt, ohne dass das
    /// irgendwo stand — und bekam dieselben Schritte angeboten, obwohl einer
    /// davon bei Headscale gar nicht funktioniert: Zertifikate stellt der Dienst
    /// von Tailscale aus, ein Headscale-Server in aller Regel nicht.
    /// </para>
    /// </summary>
    Headscale
}

/// <summary>
/// Der gewählte Modus samt der Adresse, unter der dieser Rechner erreichbar ist.
/// </summary>
/// <param name="Address">
/// In **jedem** Modus Pflicht.
///
/// <para>
/// Bis v1.3.0 durfte sie bei Tailscale leer bleiben — dann kam der Name aus dem
/// Zertifikat. Wer aber keins geholt hatte, bekam ein selbst ausgestelltes, und
/// darin steht der Windows-Rechnername und nicht der Name im Tailnet. Genau der
/// landete im QR-Code, und das Handy suchte einen Rechner, den es unter dem
/// Namen nirgends gibt. Eine Adresse, die man weglassen darf, ist eine, die
/// irgendwann fehlt.
/// </para>
/// </param>
/// <param name="Coordinator">
/// Nur bei <see cref="NetworkKind.Headscale"/> eine Entscheidung. In allen
/// anderen Modi steht dort der Dienst von Tailscale, und
/// <see cref="Normalized"/> setzt ihn auch dorthin zurück.
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

    /// <summary>
    /// Ob dieser Modus den Tailscale-Client braucht. Headscale zählt dazu: es
    /// ist derselbe Client, nur an einem anderen Koordinator.
    /// </summary>
    public bool NeedsTailscale => Kind is NetworkKind.Tailscale or NetworkKind.Headscale;

    /// <summary>
    /// Ob sich in diesem Modus ein Zertifikat von der Gegenstelle holen lässt.
    ///
    /// <para>
    /// Nur bei Tailscale. <c>tailscale cert</c> lässt sich den Namen vom
    /// Koordinator beglaubigen, und das kann der Dienst von Tailscale; ein
    /// Headscale-Server bringt diese Stelle nicht mit. Dort stellt der Agent
    /// sich sein Zertifikat selbst aus, und das Handy bestätigt es einmal —
    /// unschön, aber ehrlich, und es funktioniert. Angeboten wird der Schritt
    /// bei Headscale deshalb gar nicht: ein Knopf, der fast immer scheitert,
    /// wäre kein Angebot, sondern eine Falle.
    /// </para>
    /// </summary>
    public bool CanFetchCertificate => Kind == NetworkKind.Tailscale;

    /// <summary>
    /// Die Adresse, auf die das Zertifikat lauten muss und die im QR-Code steht —
    /// <c>null</c>, solange keine eingetragen ist.
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
            if (Kind == NetworkKind.Headscale)
            {
                if (Coordinator.IsTailscale)
                {
                    return "Trage die Adresse deines Headscale-Servers ein — etwa "
                           + "https://headscale.example.org. Ohne sie wäre es kein Headscale, "
                           + "sondern Tailscale.";
                }

                if (Coordinator.Rejection is { } coordinator)
                {
                    return coordinator;
                }
            }

            return RejectAddress(Address);
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
            "Über einen anderen VPN-Anbieter. RemoteDesktop benutzt nur die Adresse, die dort "
            + "gilt; Verbindung und Einrichtung bleiben deine Sache.",
        NetworkKind.Headscale =>
            "Über Headscale — derselbe Tailscale-Client, aber an deinem eigenen Koordinator "
            + "statt an dem der Firma.",
        _ =>
            "Über Tailscale — verbindet deine Geräte direkt miteinander, auch von unterwegs, "
            + "ohne dass du am Router etwas freigeben musst."
    };

    /// <summary>Der Name des Modus, wie er in der Oberfläche steht.</summary>
    public string Name() => Kind switch
    {
        NetworkKind.Lan => "Heimnetz",
        NetworkKind.Vpn => "Anderer VPN-Anbieter",
        NetworkKind.Headscale => "Headscale",
        _ => "Tailscale"
    };

    /// <summary>
    /// Kleinbuchstaben und ohne Klammern: so steht der Name später im Zertifikat
    /// und im QR-Code. Wer „PC.Fritz.Box" einträgt, soll nicht an der
    /// Groß-/Kleinschreibung scheitern.
    ///
    /// <para>
    /// Der Koordinator fällt außerhalb von Headscale auf die Vorgabe zurück. Ein
    /// stehengebliebener Eintrag aus einem Modus, den man wieder verlassen hat,
    /// wäre sonst genau das, was <c>tailscale up</c> beim nächsten Mal an den
    /// falschen Server schickt.
    /// </para>
    /// </summary>
    public NetworkProfile Normalized() => this with
    {
        Address = Address.Trim().Trim('[', ']').ToLowerInvariant(),
        Coordinator = Kind == NetworkKind.Headscale ? Coordinator : Coordinator.Default
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

            // Vor v1.3.0 gab es Headscale nur als Zusatzfeld unter „Tailscale":
            // wer eine eigene Koordinator-Adresse eintrug, betrieb damit
            // Headscale, ohne dass es irgendwo so hieß. Genau das ist es, also
            // heißt es beim Lesen jetzt auch so — sonst verlöre ein Update die
            // Unterscheidung, die es gerade einführt.
            if (kind == NetworkKind.Tailscale && !coordinator.IsTailscale)
            {
                kind = NetworkKind.Headscale;
            }

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
        "headscale" => NetworkKind.Headscale,
        _ => NetworkKind.Tailscale
    };
}
