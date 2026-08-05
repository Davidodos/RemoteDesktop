using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Wo die selbst ausgestellten Zertifikate liegen und wann sie neu entstehen.
///
/// Getrennt von <see cref="SelfSignedCertificate"/>, weil dort nur Kryptografie
/// steht und hier nur Dateien. Die Trennung ist der Grund, warum sich beides auf
/// einem Linux-Container prüfen lässt.
/// </summary>
public sealed class CertificateVault(string directory, TimeProvider? time = null)
{
    /// <summary>Die eigene CA — der Anker, dem ein Client einmal vertraut.</summary>
    public const string AuthorityFile = "agentca.pfx";

    /// <summary>Ihr öffentlicher Teil, so wie ihn ein Client zum Bestätigen bekommt.</summary>
    public const string AuthorityPublicFile = "agentca.crt";

    /// <summary>Das Zertifikat, das der Agent beim Verbinden vorzeigt.</summary>
    public const string ServerFile = "agent.pfx";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Lädt die CA oder legt sie an. Sie entsteht genau einmal je Rechner:
    /// entstünde sie neu, müsste jedes gekoppelte Gerät erneut bestätigen.
    /// </summary>
    public X509Certificate2 Authority(string machineName)
    {
        var path = Path.Combine(directory, AuthorityFile);
        var existing = TryLoad(path);

        // Auch eine abgelaufene CA wird nicht stillschweigend ersetzt: das
        // Vertrauen der Clients hängt an ihr, und ein stiller Tausch sähe für
        // sie aus wie ein untergeschobener Rechner. Sie wird erneuert, und die
        // Clients müssen einmal erneut bestätigen — sichtbar statt heimlich.
        if (existing is not null && _time.GetUtcNow() < existing.NotAfter)
        {
            return existing;
        }

        existing?.Dispose();

        var created = SelfSignedCertificate.CreateAuthority(machineName, _time.GetUtcNow());

        Save(path, created);
        File.WriteAllBytes(
            Path.Combine(directory, AuthorityPublicFile),
            created.Export(X509ContentType.Cert));

        return created;
    }

    /// <summary>
    /// Das Serverzertifikat für die angegebenen Namen — vorhanden, erneuert oder
    /// neu, je nachdem, was nötig ist.
    /// </summary>
    public X509Certificate2 Server(X509Certificate2 authority, IReadOnlyList<string> names)
    {
        var path = Path.Combine(directory, ServerFile);
        var existing = TryLoad(path);

        if (existing is not null &&
            !SelfSignedCertificate.NeedsRenewal(existing, names, _time.GetUtcNow()))
        {
            return existing;
        }

        existing?.Dispose();

        var issued = SelfSignedCertificate.Issue(authority, names, _time.GetUtcNow());

        Save(path, issued);

        return issued;
    }

    /// <summary>
    /// Ein unlesbares oder beschädigtes Zertifikat ist wie keins: es wird neu
    /// ausgestellt. Ein Abbruch stünde hier für „der Dienst startet nicht, weil
    /// eine Datei kaputt ist" — genau der Zustand, den diese Phase abschafft.
    /// </summary>
    private static X509Certificate2? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return new X509Certificate2(
                File.ReadAllBytes(path),
                password: (string?)null,
                keyStorageFlags: StorageFlags);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Save(string path, X509Certificate2 certificate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
    }

    /// <summary>
    /// Unter Windows läuft der Agent als Dienst unter SYSTEM und Kestrel kommt
    /// mit einem ephemeren Schlüssel nicht zurecht — deshalb der Maschinen-Store.
    /// Auf anderen Systemen (also im Testlauf) bleibt das folgenlos.
    /// </summary>
    private static X509KeyStorageFlags StorageFlags =>
        OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet
            : X509KeyStorageFlags.DefaultKeySet;
}
