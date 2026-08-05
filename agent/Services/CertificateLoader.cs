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
    /// <summary>
    /// Das Zertifikat, mit dem der Agent lauscht, samt der Frage, woher es
    /// stammt.
    /// </summary>
    /// <param name="Authority">
    /// Die eigene CA — <c>null</c>, wenn das Zertifikat von Tailscale kommt.
    /// Dann kennt jeder Browser den Aussteller schon, und es gibt nichts zu
    /// bestätigen.
    /// </param>
    public sealed record Chosen(X509Certificate2 Certificate, X509Certificate2? Authority)
    {
        public bool SelfIssued => Authority is not null;
    }

    /// <summary>
    /// Das Zertifikat für diesen Start: das von Tailscale, wenn es dasteht,
    /// sonst ein selbst ausgestelltes.
    ///
    /// <para>
    /// Tailscale gewinnt, wo es vorliegt — es ist von einer öffentlichen Stelle
    /// ausgestellt, und dann muss auf keinem Handy jemand etwas bestätigen. Die
    /// Reihenfolge ist der ganze Unterschied zwischen „Tailscale ist eine
    /// Möglichkeit" und „Tailscale ist Voraussetzung".
    /// </para>
    /// </summary>
    public static Chosen LoadOrCreate(
        string? certificatePath,
        string? keyPath,
        CertificateVault vault,
        string machineName,
        IReadOnlyList<string> names)
    {
        if (!string.IsNullOrWhiteSpace(certificatePath) &&
            !string.IsNullOrWhiteSpace(keyPath) &&
            File.Exists(certificatePath) &&
            File.Exists(keyPath))
        {
            return new Chosen(Load(certificatePath, keyPath), null);
        }

        var authority = vault.Authority(machineName);

        return new Chosen(vault.Server(authority, names), authority);
    }

    /// <summary>
    /// Auf welche Namen das selbst ausgestellte Zertifikat lauten muss.
    ///
    /// Die eingetragene Adresse steht vorn, weil der erste Name der
    /// Antragsteller ist und damit der, den der QR-Code trägt. Danach kommt,
    /// was ohnehin gilt: der Rechnername, <c>localhost</c> für das
    /// Einrichtungsfenster auf demselben Rechner, und jede IP-Adresse, unter der
    /// dieser Rechner gerade zu erreichen ist — im Heimnetz tippt man die IP,
    /// nicht den Namen.
    /// </summary>
    public static IReadOnlyList<string> Names(
        string? advertised, string machineName, IEnumerable<string> localAddresses)
    {
        var names = new List<string>();

        void Add(string? candidate)
        {
            var value = (candidate ?? string.Empty).Trim().Trim('[', ']').ToLowerInvariant();

            if (value.Length > 0 && !names.Contains(value, StringComparer.Ordinal))
            {
                names.Add(value);
            }
        }

        Add(advertised);
        Add(machineName);
        Add("localhost");

        foreach (var address in localAddresses)
        {
            Add(address);
        }

        Add("127.0.0.1");

        return names;
    }

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
