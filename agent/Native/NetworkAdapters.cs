using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using RemoteDesktopAgent.Services;

namespace RemoteDesktopAgent.Native;

/// <summary>
/// Liest die echten Netzwerkschnittstellen samt der MAC ihres Gateways.
///
/// Die MAC des Gateways steht nirgends in der verwalteten API — sie steht in
/// der ARP-Tabelle des Systems. <c>SendARP</c> aus der <c>iphlpapi.dll</c>
/// beantwortet sie: liegt ein Eintrag vor, kommt er sofort zurück, sonst
/// schickt Windows selbst eine ARP-Anfrage. Das ist der einzige Win32-Aufruf,
/// den die Standort-Kennung braucht.
/// </summary>
public static class NetworkAdapters
{
    /// <summary>Kein Eintrag und keine Antwort — der übliche Fall bei VPN-Adaptern.</summary>
    private const int NoError = 0;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destinationIp, uint sourceIp, byte[] macAddress, ref uint macLength);

    /// <summary>
    /// Alle Schnittstellen, die tatsächlich laufen und ein Gateway haben
    /// könnten. Loopback und abgeschaltete Adapter fallen weg — sie würden die
    /// Auswahl in <see cref="SiteIdentity.Resolve"/> nur verwässern.
    /// </summary>
    public static IReadOnlyList<NetworkAdapter> List()
    {
        var adapters = new List<NetworkAdapter>();

        foreach (var device in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (device.OperationalStatus != OperationalStatus.Up ||
                device.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = device.GetIPProperties();

            var gateway = properties.GatewayAddresses
                .Select(entry => entry.Address)
                .FirstOrDefault(address => address.AddressFamily ==
                                           System.Net.Sockets.AddressFamily.InterNetwork);

            adapters.Add(new NetworkAdapter(
                device.Name,
                device.GetPhysicalAddress().ToString(),
                gateway is null ? null : LookupMac(gateway)));
        }

        // Eine Schnittstelle mit Gateway zuerst: Resolve nimmt die erste
        // brauchbare, und die Reihenfolge des Systems ist beliebig.
        return [.. adapters.OrderByDescending(adapter => adapter.GatewayMac is not null)];
    }

    /// <summary>
    /// Fragt die MAC zu einer IPv4-Adresse. <c>null</c>, wenn das System keine
    /// kennt — dann steht die Gegenstelle nicht im selben L2-Segment, und für
    /// die Standort-Kennung taugt sie ohnehin nicht.
    /// </summary>
    private static string? LookupMac(IPAddress address)
    {
        var buffer = new byte[6];
        var length = (uint)buffer.Length;

        try
        {
            var destination = BitConverter.ToUInt32(address.GetAddressBytes(), 0);

            if (SendARP(destination, 0, buffer, ref length) != NoError || length < 6)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Kein Windows — dann gibt es eben keine Standort-Kennung. Das ist
            // kein Grund, den Agent nicht zu starten.
            return null;
        }

        return string.Join(':', buffer.Select(value => value.ToString("x2")));
    }
}
