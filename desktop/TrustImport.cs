using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopClient;

/// <summary>Was beim Holen eines fremden Zertifikats herauskommt.</summary>
public sealed record FetchedCertificate(X509Certificate2 Certificate, string Fingerprint);

/// <summary>
/// Einem Gerät vertrauen, das sich sein Zertifikat selbst ausgestellt hat.
///
/// Gerät und nicht Rechner: seit V4 steht am anderen Ende ebenso gut ein Handy,
/// und es liefert seine Stelle auf demselben Weg — derselbe Port, dieselbe
/// Datei, dieselbe Prüfung. Hier ist ausdrücklich nichts auf Windows
/// festgenagelt.
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
/// Fingerabdruck aus der Kopplung — die Seite vergleicht ihn und trägt die
/// Stelle dann in <see cref="TrustedAuthorities"/> ein. In den Zertifikatspeicher
/// von Windows kommt seit v1.4 nichts mehr: was dort steht, gilt für jeden
/// Browser auf diesem Rechner, und das ist mehr, als eine Fernsteuerung braucht.
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
            throw new InvalidOperationException("Das Gerät hat eine leere Datei geliefert.");
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
}
