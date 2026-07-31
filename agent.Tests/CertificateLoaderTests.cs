using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Woher der Agent weiß, wie er im Tailnet heißt.
///
/// <para>
/// Diese Prüfung gibt es, weil genau hier schon einmal etwas schiefging: der
/// QR-Code der Kopplung trug <c>Environment.MachineName</c>, also den
/// <i>Windows</i>-Namen. Ein Rechner darf „DAVID" heißen und im Tailnet
/// trotzdem „pc" sein — der gescannte Code sah gültig aus und führte ins Leere.
/// </para>
///
/// <para>
/// Das Zertifikat ist die einzige Quelle, die nicht danebenliegen kann: es ist
/// derselbe Name, den TLS beim Verbinden vorzeigt. Passt er nicht, scheitert die
/// Verbindung ohnehin.
/// </para>
/// </summary>
public class CertificateLoaderTests
{
    /// <summary>
    /// Ein Zertifikat, wie <c>tailscale cert</c> es ablegt: der volle
    /// MagicDNS-Name steht als Subject Alternative Name darin.
    /// </summary>
    private static X509Certificate2 MitNamen(params string[] dnsNamen) =>
        Bauen("CN=irgendwas", dnsNamen);

    private static X509Certificate2 Bauen(string subject, params string[] dnsNamen)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);

        if (dnsNamen.Length > 0)
        {
            var namen = new SubjectAlternativeNameBuilder();

            foreach (var name in dnsNamen)
            {
                namen.AddDnsName(name);
            }

            request.CertificateExtensions.Add(namen.Build());
        }

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public void Der_volle_MagicDNS_Name_kommt_aus_dem_Zertifikat()
    {
        // Arrange
        using var certificate = MitNamen("pc.tailnet.ts.net");

        // Assert — der kurze Name allein reichte nicht: bei https://pc:8443
        // passt der vorgezeigte Name nicht zum Zertifikat.
        Assert.Equal("pc.tailnet.ts.net", CertificateLoader.DnsName(certificate));
    }

    [Fact]
    public void Der_Name_hat_nichts_mit_dem_Windows_Namen_zu_tun()
    {
        // Arrange — genau der Fall, der in der Praxis auftrat.
        using var certificate = MitNamen("pc.tailnet.ts.net");

        // Assert
        Assert.NotEqual(
            Environment.MachineName.ToLowerInvariant(),
            CertificateLoader.DnsName(certificate));
    }

    [Fact]
    public void Der_Alternativname_schlaegt_den_Antragsteller()
    {
        // Arrange — tailscale legt den MagicDNS-Namen als SAN ab; der CN ist
        // nebensächlich. Läse man den CN, käme im QR ein falscher Name heraus.
        using var certificate = Bauen("CN=irgendwas", "pc.tailnet.ts.net");

        // Assert
        Assert.Equal("pc.tailnet.ts.net", CertificateLoader.DnsName(certificate));
    }

    [Fact]
    public void Ohne_jeden_Namen_gibt_es_null_und_keinen_leeren_Text()
    {
        // Arrange — weder SAN noch CN. Dann zeigt das Fenster keinen QR-Code,
        // und gekoppelt wird über die sechs Ziffern. Ein leerer Name ergäbe
        // einen QR, der auf nichts zeigt.
        using var certificate = Bauen(string.Empty);

        // Assert
        Assert.Null(CertificateLoader.DnsName(certificate));
    }

    [Fact]
    public void Der_Name_taugt_unverändert_für_den_QR_Code()
    {
        // Arrange — die beiden gehören zusammen: was hier herauskommt, geht dort
        // hinein. Ein Name, den PairingUri ablehnt, wäre erst am Handy
        // aufgefallen.
        using var certificate = MitNamen("pc.tailnet.ts.net");

        // Act
        var uri = Auth.PairingUri.Build(CertificateLoader.DnsName(certificate)!, 8443, "123456");

        // Assert
        Assert.Equal(
            "remotedesktop://pair?host=pc.tailnet.ts.net&port=8443&code=123456", uri);
    }
}
