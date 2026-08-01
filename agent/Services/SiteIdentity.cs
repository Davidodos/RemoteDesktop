using System.Security.Cryptography;
using System.Text;

namespace RemoteDesktopAgent.Services;

/// <summary>Eine Netzwerkschnittstelle, so weit sie hier interessiert.</summary>
/// <param name="Name">Nur für Meldungen.</param>
/// <param name="Mac">Die eigene MAC — sie steht später im Magic Packet.</param>
/// <param name="GatewayMac">
/// Die MAC des Standard-Gateways dieser Schnittstelle, oder <c>null</c>, wenn
/// es keins gibt oder es nicht in der ARP-Tabelle steht.
/// </param>
public sealed record NetworkAdapter(string Name, string? Mac, string? GatewayMac);

/// <summary>Wo dieser Rechner steht und wie man ihn weckt.</summary>
/// <param name="SiteId">
/// <c>sha256</c> über die MAC des Standard-Gateways. <c>null</c>, wenn sie sich
/// nicht ermitteln ließ — dann kann niemand entscheiden, ob ein Waker im selben
/// Netz steht, und der Weckknopf bleibt aus.
/// </param>
/// <param name="Mac">Die eigene MAC, an die das Magic Packet gehen muss.</param>
public sealed record SiteInfo(string? SiteId, string? Mac);

/// <summary>
/// Die Standort-Kennung: <c>siteId = sha256(gatewayMac)</c>.
///
/// Sie beantwortet die einzige Frage, die beim Wecken zählt — steht der Waker
/// im selben Netz wie der schlafende Rechner? Gleiches LAN heißt gleiches
/// Gateway, unabhängig davon, welche IP der DHCP gerade vergeben hat. Subnetz
/// und Gateway-Adresse taugen dafür nicht: <c>192.168.178.1</c> gibt es
/// millionenfach, und zwei Standorte hätten dieselbe Kennung.
///
/// Gehasht statt roh gemeldet, weil die Kennung über das Netz geht und an jeden
/// gekoppelten Client: die MAC des eigenen Routers ist nichts, was man
/// herumreichen muss, wenn ein Vergleichswert reicht.
/// </summary>
public static class SiteIdentity
{
    /// <summary>
    /// Bringt eine MAC auf eine Form, die sich vergleichen lässt:
    /// Kleinbuchstaben, mit Doppelpunkten. <c>null</c> für alles, was keine
    /// MAC ist — auch für die Nulladresse, die manche Schnittstellen melden,
    /// wenn sie gar keine haben.
    /// </summary>
    public static string? NormalizeMac(string? mac)
    {
        if (mac is null)
        {
            return null;
        }

        var hex = new StringBuilder(12);

        foreach (var character in mac)
        {
            if (character is ':' or '-' or '.' or ' ')
            {
                continue;
            }

            if (!Uri.IsHexDigit(character))
            {
                return null;
            }

            hex.Append(char.ToLowerInvariant(character));
        }

        if (hex.Length != 12 || hex.ToString() == "000000000000")
        {
            return null;
        }

        var parts = Enumerable.Range(0, 6).Select(index => hex.ToString(index * 2, 2));

        return string.Join(':', parts);
    }

    /// <summary>
    /// Die Kennung eines Netzes aus der MAC seines Gateways. <c>null</c>, wenn
    /// die MAC unbrauchbar ist — eine erfundene Kennung wäre schlimmer als
    /// keine, weil dann fremde Standorte zusammenfielen.
    /// </summary>
    public static string? FromGatewayMac(string? gatewayMac)
    {
        var normalized = NormalizeMac(gatewayMac);

        if (normalized is null)
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Sucht unter den Schnittstellen die eine heraus, über die dieser Rechner
    /// am Netz hängt: die mit einem Gateway in der ARP-Tabelle.
    ///
    /// Es können mehrere sein — ein Laptop mit WLAN und Dock hat zwei, dazu
    /// kommen Tailscale, Hyper-V und was sonst noch virtuelle Adapter anlegt.
    /// Genommen wird die erste mit Gateway; ohne eine solche bleibt die
    /// Standort-Kennung leer, und die eigene MAC wird trotzdem gemeldet, damit
    /// ein Waker im selben Netz sie wenigstens von Hand bekommen könnte.
    /// </summary>
    public static SiteInfo Resolve(IEnumerable<NetworkAdapter> adapters)
    {
        var candidates = adapters.ToList();

        var withGateway = candidates.FirstOrDefault(
            adapter => FromGatewayMac(adapter.GatewayMac) is not null);

        if (withGateway is not null)
        {
            return new SiteInfo(FromGatewayMac(withGateway.GatewayMac), NormalizeMac(withGateway.Mac));
        }

        var anyMac = candidates.Select(adapter => NormalizeMac(adapter.Mac))
            .FirstOrDefault(mac => mac is not null);

        return new SiteInfo(null, anyMac);
    }
}
