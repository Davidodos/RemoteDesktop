using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Lädt das TLS-Zertifikat, das <c>tailscale cert</c> abgelegt hat.
///
/// Damit hat der Agent ein echtes, vom Browser akzeptiertes Zertifikat für
/// seinen <c>*.ts.net</c>-Namen. Nur so darf die von der NAS ausgelieferte PWA
/// per WSS direkt zum Agent verbinden, ohne dass der Browser das als Mixed
/// Content blockt.
/// </summary>
public static class CertificateLoader
{
    public static X509Certificate2 Load(string certificatePath, string keyPath)
    {
        if (!File.Exists(certificatePath))
        {
            throw new FileNotFoundException(
                $"Zertifikat nicht gefunden: {certificatePath}. " +
                "Erzeugen mit: tailscale cert <hostname>.<tailnet>.ts.net",
                certificatePath);
        }

        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException(
                $"Privater Schlüssel nicht gefunden: {keyPath}.", keyPath);
        }

        using var fromPem = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

        // Umweg über PFX: Kestrel kann unter Windows mit dem ephemeren
        // Schlüssel aus CreateFromPemFile nichts anfangen und wirft beim
        // Handshake. Der Re-Import hängt den Schlüssel korrekt an.
        return new X509Certificate2(
            fromPem.Export(X509ContentType.Pfx),
            password: (string?)null,
            keyStorageFlags: X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
    }

    /// <summary>
    /// Der Name, auf den das Zertifikat lautet — also der volle MagicDNS-Name
    /// dieses Rechners, etwa <c>pc.tailnet.ts.net</c>.
    ///
    /// <para>
    /// Das Zertifikat ist dafür die einzige verlässliche Quelle.
    /// <c>Environment.MachineName</c> ist der <i>Windows</i>-Name und hat mit dem
    /// Tailnet-Namen nichts zu tun — ein Rechner darf „DAVID" heißen und im
    /// Tailnet trotzdem „pc" sein. Und der kurze Name allein genügt auch nicht:
    /// bei <c>https://pc:8443</c> passt der vorgezeigte Name nicht zum
    /// Zertifikat, und der Handshake scheitert.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>null</c>, wenn das Zertifikat keinen Namen führt. Dann gibt es keinen
    /// QR-Code, aber der abgetippte Code funktioniert weiter.
    /// </returns>
    public static string? DnsName(X509Certificate2 certificate)
    {
        var name = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
