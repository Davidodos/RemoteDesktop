using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Wofür die eigene Stelle Zertifikate unterschreiben darf — X.509 Name
/// Constraints (RFC 5280, 4.2.1.10), kritisch.
///
/// <para>
/// **Der Befund dahinter (Durchsicht B5):** die CA eines Rechners landet am
/// Handy im Zertifikatspeicher des Benutzers, dem auch Chrome vertraut. Ohne
/// Einschränkung könnte, wer den Schlüssel dieser Stelle hat, ein Zertifikat
/// für <c>google.de</c> unterschreiben, und das Handy nähme es an. Mit den
/// Einschränkungen hier gilt die Stelle nur für private Adressen, die
/// üblichen Heimnetz-Endungen, Tailscale und den eingetragenen Namen —
/// alles andere lehnt jeder Client ab, der die Erweiterung versteht, und
/// weil sie kritisch ist, auch jeder, der sie nicht versteht.
/// </para>
/// </summary>
public static class NameConstraints
{
    public static readonly Oid Id = new("2.5.29.30");

    /// <summary>
    /// Endungen, unter denen ein Rechner zu Hause heißt. Ohne Punkt davor: eine
    /// Einschränkung „fritz.box" erlaubt <c>pc.fritz.box</c> und <c>fritz.box</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultSuffixes =
        ["localhost", "local", "lan", "home", "internal", "fritz.box", "home.arpa", "ts.net"];

    /// <summary>Private Adressbereiche, Loopback und Link-Local — v4 und v6.</summary>
    public static readonly IReadOnlyList<(IPAddress Network, int Prefix)> DefaultRanges =
    [
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10)
    ];

    private static readonly Asn1Tag Permitted = new(TagClass.ContextSpecific, 0, isConstructed: true);
    private static readonly Asn1Tag DnsName = new(TagClass.ContextSpecific, 2);
    private static readonly Asn1Tag IpAddress = new(TagClass.ContextSpecific, 7);

    /// <summary>
    /// Die Erweiterung für eine neue Stelle: die Vorgaben plus alles, was
    /// <paramref name="names"/> darüber hinaus nennt — ein Name als Name, eine
    /// Adresse außerhalb der Bereiche als einzelne Adresse.
    /// </summary>
    public static X509Extension Build(IEnumerable<string> names)
    {
        var dns = new List<string>(DefaultSuffixes);
        var ranges = new List<(IPAddress Network, int Prefix)>(DefaultRanges);

        foreach (var raw in names)
        {
            var name = Normalize(raw);

            if (name.Length == 0)
            {
                continue;
            }

            if (IPAddress.TryParse(name, out var address))
            {
                if (!ranges.Any(range => Covers(range.Network, range.Prefix, address)))
                {
                    ranges.Add((address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
                }
            }
            else if (!dns.Any(suffix => MatchesSuffix(name, suffix)))
            {
                dns.Add(name);
            }
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        using (writer.PushSequence(Permitted))
        {
            foreach (var name in dns)
            {
                using (writer.PushSequence())
                {
                    writer.WriteCharacterString(UniversalTagNumber.IA5String, name, DnsName);
                }
            }

            foreach (var (network, prefix) in ranges)
            {
                using (writer.PushSequence())
                {
                    writer.WriteOctetString(WithMask(network, prefix), IpAddress);
                }
            }
        }

        return new X509Extension(Id, writer.Encode(), critical: true);
    }

    /// <summary>
    /// Ob die Stelle Zertifikate für diese Namen unterschreiben darf.
    ///
    /// Eine Stelle ohne die Erweiterung — von vor v1.4 — darf alles; sie
    /// bleibt stehen, damit bestehende Kopplungen gültig bleiben. Erst wer neu
    /// koppelt, bekommt eine eingeschränkte.
    /// </summary>
    public static bool Permits(X509Certificate2 authority, IEnumerable<string> names)
    {
        var extension = authority.Extensions[Id.Value!];

        if (extension is null)
        {
            return true;
        }

        var (dns, ranges) = Read(extension.RawData);

        foreach (var raw in names)
        {
            var name = Normalize(raw);

            if (name.Length == 0)
            {
                continue;
            }

            var allowed = IPAddress.TryParse(name, out var address)
                ? ranges.Any(range => Covers(range.Network, range.Prefix, address))
                : dns.Any(suffix => MatchesSuffix(name, suffix));

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static (List<string> Dns, List<(IPAddress Network, int Prefix)> Ranges) Read(byte[] raw)
    {
        var dns = new List<string>();
        var ranges = new List<(IPAddress, int)>();

        try
        {
            var reader = new AsnReader(raw, AsnEncodingRules.DER).ReadSequence();

            if (!reader.HasData || !reader.PeekTag().HasSameClassAndValue(Permitted))
            {
                return (dns, ranges);
            }

            var subtrees = reader.ReadSequence(Permitted);

            while (subtrees.HasData)
            {
                var subtree = subtrees.ReadSequence();
                var tag = subtree.PeekTag();

                if (tag.HasSameClassAndValue(DnsName))
                {
                    dns.Add(Normalize(subtree.ReadCharacterString(UniversalTagNumber.IA5String, DnsName)));
                }
                else if (tag.HasSameClassAndValue(IpAddress))
                {
                    var bytes = subtree.ReadOctetString(IpAddress);
                    var half = bytes.Length / 2;

                    if (half is 4 or 16)
                    {
                        ranges.Add((new IPAddress(bytes[..half]), PrefixOf(bytes[half..])));
                    }
                }
            }
        }
        catch (AsnContentException)
        {
            // Eine unlesbare Einschränkung erlaubt nichts — dann wird die
            // Stelle neu ausgestellt, sichtbar für die Clients.
        }

        return (dns, ranges);
    }

    private static bool MatchesSuffix(string name, string suffix) =>
        name.Equals(suffix, StringComparison.Ordinal)
        || name.EndsWith("." + suffix, StringComparison.Ordinal);

    private static bool Covers(IPAddress network, int prefix, IPAddress address)
    {
        if (network.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        var a = network.GetAddressBytes();
        var b = address.GetAddressBytes();

        for (var bit = 0; bit < prefix; bit++)
        {
            var mask = (byte)(0x80 >> (bit % 8));

            if ((a[bit / 8] & mask) != (b[bit / 8] & mask))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] WithMask(IPAddress network, int prefix)
    {
        var address = network.GetAddressBytes();
        var mask = new byte[address.Length];

        for (var bit = 0; bit < prefix; bit++)
        {
            mask[bit / 8] |= (byte)(0x80 >> (bit % 8));
        }

        return [.. address, .. mask];
    }

    private static int PrefixOf(byte[] mask)
    {
        var prefix = 0;

        foreach (var value in mask)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                if ((value & (1 << bit)) == 0)
                {
                    return prefix;
                }

                prefix++;
            }
        }

        return prefix;
    }

    private static string Normalize(string name) => name.Trim().Trim('[', ']').ToLowerInvariant();
}
