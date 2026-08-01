using System.Net;
using System.Net.Sockets;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Das Magic Packet für Wake-on-LAN — sechs Bytes <c>0xFF</c>, danach die
/// MAC-Adresse sechzehnmal hintereinander.
///
/// Portiert aus <c>hub/src/wol.ts</c>, wo es seit Phase 6 stand. Es liegt jetzt
/// auch im Agent, weil ein wacher Rechner den schlafenden im selben Netz wecken
/// kann und dafür kein Dienst auf der NAS nötig ist. Bewusst selbst gebaut und
/// nicht als Paket: es sind zwölf Zeilen, und der Agent hat volle Kontrolle
/// über den Rechner — jede Abhängigkeit weniger ist dort etwas wert.
/// </summary>
public static class MagicPacket
{
    /// <summary>
    /// Übliche WOL-Ports. Netzwerkkarten hören mal auf dem einen, mal auf dem
    /// anderen; ein einzelnes verlorenes UDP-Paket hieße sonst, dass der
    /// Rechner einfach nicht aufwacht.
    /// </summary>
    private static readonly int[] Ports = [7, 9];

    private const int SyncStreamLength = 6;
    private const int MacRepeatCount = 16;
    private const int MacByteLength = 6;

    public static byte[] Build(string mac)
    {
        var bytes = ParseMac(mac);
        var packet = new byte[SyncStreamLength + MacRepeatCount * MacByteLength];

        Array.Fill(packet, (byte)0xFF, 0, SyncStreamLength);

        for (var repeat = 0; repeat < MacRepeatCount; repeat++)
        {
            bytes.CopyTo(packet, SyncStreamLength + repeat * MacByteLength);
        }

        return packet;
    }

    /// <exception cref="ArgumentException">Wenn es keine MAC-Adresse ist.</exception>
    public static byte[] ParseMac(string mac)
    {
        var normalized = SiteIdentity.NormalizeMac(mac)
                         ?? throw new ArgumentException($"Ungültige MAC-Adresse: {mac}", nameof(mac));

        return Convert.FromHexString(normalized.Replace(":", string.Empty));
    }

    /// <summary>
    /// Schickt das Paket als Broadcast an beide Ports.
    ///
    /// Broadcast statt gezielt an eine IP: der schlafende Rechner hat keine —
    /// es läuft kein IP-Stack, und auf ARP antwortet niemand.
    /// </summary>
    public static async Task SendAsync(
        string mac, string broadcastAddress, CancellationToken cancellationToken = default)
    {
        var packet = Build(mac);
        var target = IPAddress.Parse(broadcastAddress);

        using var socket = new UdpClient { EnableBroadcast = true };

        foreach (var port in Ports)
        {
            await socket.SendAsync(packet, new IPEndPoint(target, port), cancellationToken);
        }
    }
}
