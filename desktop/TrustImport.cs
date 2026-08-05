using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopClient;

/// <summary>Was beim Holen eines fremden Zertifikats herauskommt.</summary>
public sealed record FetchedCertificate(X509Certificate2 Certificate, string Fingerprint)
{
    /// <summary>Der Fingerabdruck in Zweiergruppen — so vergleicht ihn ein Mensch.</summary>
    public string Readable =>
        string.Join(':', Enumerable.Range(0, Fingerprint.Length / 2)
            .Select(index => Fingerprint.Substring(index * 2, 2)));
}

/// <summary>
/// Einem Rechner vertrauen, der sich sein Zertifikat selbst ausgestellt hat.
///
/// <para>
/// Nötig, seit Tailscale nicht mehr Voraussetzung ist: im Heimnetz und im
/// eigenen VPN gibt es keine öffentliche Stelle, die ein Zertifikat für
/// <c>192.168.178.20</c> ausstellen würde. Ohne diesen Schritt scheitert die
/// Verbindung, bevor überhaupt ein Ausweis geprüft wird — und die Meldung
/// darüber kann niemand einordnen.
/// </para>
///
/// <para>
/// Das Zertifikat wird unverschlüsselt geholt, weil es anders nicht geht: die
/// verschlüsselte Verbindung ist ja gerade die, die ohne dieses Zertifikat nicht
/// zustande kommt. Es enthält kein Geheimnis. Was es echt macht, ist der
/// Fingerabdruck — und den vergleicht ein Mensch mit dem, der am anderen Rechner
/// im Fenster steht.
/// </para>
/// </summary>
public static class TrustImport
{
    /// <summary>Der Port, auf dem ein Agent ausschließlich sein CA-Zertifikat anbietet.</summary>
    public const int TrustPort = 8442;

    /// <summary>Holt das Zertifikat. Geprüft wird danach vom Menschen, nicht hier.</summary>
    public static async Task<FetchedCertificate> FetchAsync(
        string host, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var raw = await http.GetByteArrayAsync(
            $"http://{host.Trim().Trim('[', ']')}:{TrustPort}/ca.crt", cancellationToken);

        if (raw.Length == 0)
        {
            throw new InvalidOperationException("Der Rechner hat eine leere Datei geliefert.");
        }

        var certificate = new X509Certificate2(raw);

        if (certificate.Extensions.OfType<X509BasicConstraintsExtension>()
                .FirstOrDefault()?.CertificateAuthority != true)
        {
            // Ein Serverzertifikat im Stammspeicher wäre wirkungslos und
            // stünde trotzdem für immer dort.
            throw new InvalidOperationException(
                "Das ist keine Zertifizierungsstelle — es gehört nicht in den Speicher.");
        }

        return new FetchedCertificate(
            certificate,
            Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant());
    }

    /// <summary>
    /// Legt es unter den vertrauenswürdigen Stammzertifikaten **dieses
    /// Benutzers** ab.
    ///
    /// Nicht für den ganzen Rechner: eine Stelle, der man vertraut, gilt für
    /// alles, was danach kommt. Diese Entscheidung darf einer für sich treffen
    /// und nicht für alle, die diesen Rechner benutzen — und sie braucht so auch
    /// keine Adminrechte.
    /// </summary>
    public static void Trust(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);

        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        store.Close();
    }
}
