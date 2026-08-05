using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Unter welchen IP-Adressen dieser Rechner gerade zu erreichen ist.
///
/// Sie gehören ins selbst ausgestellte Zertifikat, weil im Heimnetz niemand
/// einen Namen eintippt, sondern die IP. Ein Zertifikat ohne sie sähe richtig
/// aus und würde beim Verbinden abgelehnt.
/// </summary>
public static class LocalAddresses
{
    public static IReadOnlyList<string> List()
    {
        var addresses = new List<string>();

        foreach (var device in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (device.OperationalStatus != OperationalStatus.Up ||
                device.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var entry in device.GetIPProperties().UnicastAddresses)
            {
                // Nur IPv4 und keine Link-Local-Adressen: Letztere gelten nur
                // auf demselben Kabel und stünden im Zertifikat als Namen, die
                // nie jemand aufruft.
                if (entry.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !entry.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    addresses.Add(entry.Address.ToString());
                }
            }
        }

        return addresses;
    }
}
