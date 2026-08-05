using System.Net.NetworkInformation;
using System.Net.Sockets;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Das Netzprofil auf der Platte: dieselbe Datei, die auch der Agent liest.
///
/// Sie liegt in seinem Datenordner, und der gehört Administratoren und dem
/// System — daneben liegt der private Schlüssel des Agents. Lesen darf jeder,
/// schreiben nur über den Sprung auf Adminrechte.
/// </summary>
public static class NetworkStore
{
    public static string Path => System.IO.Path.Combine(
        Elevation.DataDirectory, NetworkConfig.FileName);

    public static NetworkProfile Read() =>
        NetworkConfig.Read(File.Exists(Path) ? File.ReadAllText(Path) : null);

    /// <summary>
    /// Schreibt das Profil. Der Umweg über eine vorbereitete Datei im
    /// Temp-Ordner ist Absicht: JSON auf einer Kommandozeile wäre eine Einladung
    /// an jedes Anführungszeichen, etwas anderes zu bedeuten.
    /// </summary>
    public static RunResult Write(NetworkProfile profile)
    {
        var prepared = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"remotedesktop-netz-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(prepared, NetworkConfig.Write(profile.Normalized()));

            return Elevation.Run(AdminTask.WriteNetwork, prepared);
        }
        catch (Exception failure)
        {
            return new RunResult(-1, string.Empty, failure.Message);
        }
        finally
        {
            try
            {
                File.Delete(prepared);
            }
            catch (IOException)
            {
                // Eine Datei im Temp-Ordner, die liegen bleibt, ist kein Grund,
                // dem Nutzer etwas zu melden.
            }
        }
    }

    /// <summary>
    /// Die Adresse, unter der dieser Rechner im Heimnetz vermutlich zu finden
    /// ist — als Vorschlag, nicht als Wahrheit.
    ///
    /// Genommen wird die Schnittstelle mit einem Standard-Gateway: ein Rechner
    /// hat meist mehrere Adressen (WLAN, Dock, virtuelle Adapter), und nur die
    /// mit einem Gateway hängt am Router.
    /// </summary>
    public static string? Guess()
    {
        foreach (var device in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (device.OperationalStatus != OperationalStatus.Up ||
                device.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = device.GetIPProperties();

            var hasGateway = properties.GatewayAddresses.Any(
                entry => entry.Address.AddressFamily == AddressFamily.InterNetwork
                         && !entry.Address.ToString().StartsWith("0.", StringComparison.Ordinal));

            if (!hasGateway)
            {
                continue;
            }

            var address = properties.UnicastAddresses
                .Select(entry => entry.Address)
                .FirstOrDefault(entry => entry.AddressFamily == AddressFamily.InterNetwork);

            if (address is not null)
            {
                return address.ToString();
            }
        }

        return null;
    }
}
