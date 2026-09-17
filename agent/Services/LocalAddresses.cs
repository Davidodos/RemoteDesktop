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
                if (IsUsable(entry))
                {
                    addresses.Add(entry.Address.ToString());
                }
            }
        }

        return addresses;
    }

    /// <summary>
    /// Keine Link-Local-Adressen: sie gelten nur auf demselben Kabel und
    /// stünden im Zertifikat als Namen, die nie jemand aufruft. Bei IPv6
    /// zusätzlich keine temporären: Windows würfelt sie täglich neu, und das
    /// Zertifikat liefe ihnen nur hinterher (Durchsicht C3).
    /// </summary>
    private static bool IsUsable(UnicastIPAddressInformation entry)
    {
        var address = entry.Address;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !address.ToString().StartsWith("169.254.", StringComparison.Ordinal);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6Teredo)
        {
            return false;
        }

        return !OperatingSystem.IsWindows() || entry.SuffixOrigin != SuffixOrigin.Random;
    }
}
