using System.Security.Cryptography.X509Certificates;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Agent stellt sich selbst ein Zertifikat aus, wenn keins von Tailscale
/// dasteht.
///
/// Der Befund dahinter: bis Release v1.0.0 beendete sich der Dienst in genau
/// diesem Fall sofort wieder. Von außen sah das aus wie ein toter Rechner, und
/// wer den PC nur aus dem eigenen WLAN steuern wollte, hatte keinen Weg daran
/// vorbei.
/// </summary>
public class SelfSignedCertificateTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Aus_dem_Nichts_entsteht_ein_brauchbares_Zertifikat()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["192.168.178.20"], Jetzt);

        Assert.True(server.HasPrivateKey);
        Assert.Contains("192.168.178.20", SelfSignedCertificate.SubjectNames(server));
    }

    [Fact]
    public void Eine_IP_kommt_als_IP_hinein_und_nicht_als_Name()
    {
        // Ein Client, der https://192.168.178.20:8443 aufruft, prüft
        // ausschließlich die IP-Einträge. Ein DNS-Eintrag „192.168.178.20"
        // sähe im Zertifikat richtig aus und würde trotzdem abgelehnt.
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["pc.fritz.box", "192.168.178.20"], Jetzt);

        var namen = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();

        Assert.Equal(["pc.fritz.box"], namen.EnumerateDnsNames());
        Assert.Equal(["192.168.178.20"], namen.EnumerateIPAddresses().Select(ip => ip.ToString()));
    }

    [Fact]
    public void Die_CA_darf_nur_beglaubigen_und_nichts_bedienen()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);

        var grenzen = ca.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        var nutzung = ca.Extensions.OfType<X509KeyUsageExtension>().Single();

        Assert.True(grenzen.CertificateAuthority);

        // Sie unterschreibt genau ein Zertifikat, nämlich das dieses Rechners.
        // Keine weiteren CAs, selbst wenn jemand ihren Schlüssel bekäme.
        Assert.Equal(0, grenzen.PathLengthConstraint);
        Assert.True(nutzung.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign));
        Assert.False(nutzung.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment));
    }

    [Fact]
    public void Das_Serverzertifikat_ist_keine_CA()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["pc"], Jetzt);

        Assert.False(server.Extensions.OfType<X509BasicConstraintsExtension>()
            .Single().CertificateAuthority);

        Assert.Contains(
            "1.3.6.1.5.5.7.3.1",
            server.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Single().EnhancedKeyUsages.OfType<System.Security.Cryptography.Oid>()
                .Select(oid => oid.Value));
    }

    [Fact]
    public void Es_wird_von_der_eigenen_CA_unterschrieben()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["pc"], Jetzt);

        Assert.Equal(ca.Subject, server.Issuer);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = Jetzt.UtcDateTime.AddDays(1);

        // Das ist der Punkt: ein Client, der die CA kennt, muss dieses
        // Zertifikat annehmen. Ohne diese Prüfung fiele ein Fehler in der Kette
        // erst am Handy auf.
        Assert.True(chain.Build(server), string.Join(
            " · ", chain.ChainStatus.Select(status => status.StatusInformation)));
    }

    [Fact]
    public void Es_ueberlebt_seine_CA_nicht()
    {
        // Ein Zertifikat, das seinen Aussteller überlebt, ist ab dessen Ablauf
        // wertlos und sieht trotzdem gültig aus.
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var spaet = SelfSignedCertificate.Issue(
            ca, ["pc"], Jetzt + SelfSignedCertificate.AuthorityLifetime - TimeSpan.FromDays(10));

        Assert.True(spaet.NotAfter <= ca.NotAfter);
    }

    [Fact]
    public void Solange_alles_passt_wird_nicht_erneuert()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["pc", "192.168.178.20"], Jetzt);

        Assert.False(SelfSignedCertificate.NeedsRenewal(
            server, ["pc", "192.168.178.20"], Jetzt.AddDays(100)));
    }

    [Fact]
    public void Kurz_vor_Ablauf_wird_vorsorglich_erneuert()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["pc"], Jetzt);

        var kurzDavor = Jetzt + SelfSignedCertificate.ServerLifetime
                        - SelfSignedCertificate.RenewBefore + TimeSpan.FromDays(1);

        Assert.True(SelfSignedCertificate.NeedsRenewal(server, ["pc"], kurzDavor));
    }

    [Fact]
    public void Eine_neue_Adresse_verlangt_ein_neues_Zertifikat()
    {
        // Der häufige Fall: ein Laptop wechselt den Router und bekommt eine
        // andere IP. Ohne Erneuerung passt der vorgezeigte Name nicht mehr, und
        // die Verbindung scheitert mit einer Meldung über Zertifikate, mit der
        // niemand etwas anfangen kann.
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);
        using var server = SelfSignedCertificate.Issue(ca, ["pc", "192.168.178.20"], Jetzt);

        Assert.True(SelfSignedCertificate.NeedsRenewal(
            server, ["pc", "192.168.178.20", "10.0.0.5"], Jetzt.AddDays(1)));
    }

    [Fact]
    public void Der_Fingerabdruck_ist_der_der_Datei()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);

        var abdruck = SelfSignedCertificate.Fingerprint(ca);

        Assert.Equal(64, abdruck.Length);
        Assert.Equal(abdruck.ToLowerInvariant(), abdruck);

        // Derselbe Wert, den ein Client über die Datei bildet, die er bekommt.
        using var nurOeffentlich = new X509Certificate2(ca.Export(X509ContentType.Cert));
        Assert.Equal(abdruck, SelfSignedCertificate.Fingerprint(nurOeffentlich));
    }

    [Fact]
    public void Ein_Rechnername_mit_Komma_zerlegt_den_Antragsteller_nicht()
    {
        // Komma und Gleichheitszeichen trennen in einem Antragsteller die
        // Bestandteile. Ungeprüft ergäbe „PC,O=fremd" einen Namen, den niemand
        // vorhergesehen hat.
        using var ca = SelfSignedCertificate.CreateAuthority("PC,O=fremd", Jetzt);

        Assert.DoesNotContain("O=fremd", ca.Subject);
    }

    [Fact]
    public void Ohne_Namen_gibt_es_kein_Zertifikat()
    {
        using var ca = SelfSignedCertificate.CreateAuthority("PC", Jetzt);

        Assert.Throws<ArgumentException>(() => SelfSignedCertificate.Issue(ca, [], Jetzt));
    }
}

/// <summary>
/// Dieselbe Sache aus Sicht der Dateien: was liegt schon da, was entsteht neu.
/// </summary>
public class CertificateVaultTests : IDisposable
{
    private readonly string _ordner = Path.Combine(
        Path.GetTempPath(), "rd-vault-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_ordner))
        {
            Directory.Delete(_ordner, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private CertificateVault Tresor() => new(_ordner);

    [Fact]
    public void Beim_zweiten_Start_bleibt_alles_wie_es_war()
    {
        // Entstünde die CA bei jedem Start neu, müsste jedes gekoppelte Gerät
        // erneut bestätigen — und niemand wüsste, warum.
        using var ersteCa = Tresor().Authority("PC");
        using var ersterServer = Tresor().Server(ersteCa, ["pc"]);

        using var zweiteCa = Tresor().Authority("PC");
        using var zweiterServer = Tresor().Server(zweiteCa, ["pc"]);

        Assert.Equal(
            SelfSignedCertificate.Fingerprint(ersteCa),
            SelfSignedCertificate.Fingerprint(zweiteCa));

        Assert.Equal(
            SelfSignedCertificate.Fingerprint(ersterServer),
            SelfSignedCertificate.Fingerprint(zweiterServer));
    }

    [Fact]
    public void Eine_andere_Adresse_erneuert_nur_das_Serverzertifikat()
    {
        using var ca = Tresor().Authority("PC");
        using var alt = Tresor().Server(ca, ["pc"]);

        using var caDanach = Tresor().Authority("PC");
        using var neu = Tresor().Server(caDanach, ["pc", "192.168.178.20"]);

        Assert.Equal(
            SelfSignedCertificate.Fingerprint(ca), SelfSignedCertificate.Fingerprint(caDanach));

        Assert.NotEqual(
            SelfSignedCertificate.Fingerprint(alt), SelfSignedCertificate.Fingerprint(neu));
    }

    [Fact]
    public void Der_oeffentliche_Teil_der_CA_liegt_zum_Abholen_bereit()
    {
        using var ca = Tresor().Authority("PC");

        var datei = Path.Combine(_ordner, CertificateVault.AuthorityPublicFile);

        Assert.True(File.Exists(datei));

        using var geholt = new X509Certificate2(File.ReadAllBytes(datei));

        // Genau der Vergleich, den ein Handy anstellt, bevor es die Datei
        // bestätigt.
        Assert.Equal(
            SelfSignedCertificate.Fingerprint(ca), SelfSignedCertificate.Fingerprint(geholt));

        // Und ohne privaten Schlüssel — sonst gäbe der Agent seine Identität
        // an jeden heraus, der danach fragt.
        Assert.False(geholt.HasPrivateKey);
    }

    [Fact]
    public void Eine_beschaedigte_Datei_kostet_kein_Startversagen()
    {
        Directory.CreateDirectory(_ordner);
        File.WriteAllText(Path.Combine(_ordner, CertificateVault.AuthorityFile), "kaputt");

        using var ca = Tresor().Authority("PC");

        Assert.True(ca.HasPrivateKey);
    }
}

/// <summary>
/// Welches Zertifikat gewinnt und auf welche Namen es lauten muss.
/// </summary>
public class CertificateChoiceTests : IDisposable
{
    private readonly string _ordner = Path.Combine(
        Path.GetTempPath(), "rd-wahl-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_ordner))
        {
            Directory.Delete(_ordner, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ohne_Tailscale_Zertifikat_entsteht_ein_eigenes()
    {
        var gewaehlt = CertificateLoader.LoadOrCreate(
            @"C:\gibt\es\nicht.crt", @"C:\gibt\es\nicht.key",
            new CertificateVault(_ordner), "PC", ["192.168.178.20"]);

        Assert.True(gewaehlt.SelfIssued);
        Assert.NotNull(gewaehlt.Authority);
        Assert.Contains("192.168.178.20", SelfSignedCertificate.SubjectNames(gewaehlt.Certificate));
    }

    [Fact]
    public void Ohne_konfigurierten_Pfad_gilt_dasselbe()
    {
        // Vor V3 war ein fehlender Pfad ein Grund, gar nicht erst zu starten.
        var gewaehlt = CertificateLoader.LoadOrCreate(
            null, null, new CertificateVault(_ordner), "PC", ["pc"]);

        Assert.True(gewaehlt.SelfIssued);
    }

    [Fact]
    public void Ein_vorhandenes_Tailscale_Zertifikat_gewinnt()
    {
        // Es ist von einer öffentlichen Stelle ausgestellt — dann muss auf
        // keinem Handy jemand etwas bestätigen.
        Directory.CreateDirectory(_ordner);

        using var key = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            "CN=pc.tailnet.ts.net", key, System.Security.Cryptography.HashAlgorithmName.SHA256);

        var namen = new SubjectAlternativeNameBuilder();
        namen.AddDnsName("pc.tailnet.ts.net");
        request.CertificateExtensions.Add(namen.Build());

        using var vonTailscale = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var certPath = Path.Combine(_ordner, "cert.crt");
        var keyPath = Path.Combine(_ordner, "cert.key");

        File.WriteAllText(certPath, vonTailscale.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

        var gewaehlt = CertificateLoader.LoadOrCreate(
            certPath, keyPath, new CertificateVault(_ordner), "PC", ["pc"]);

        Assert.False(gewaehlt.SelfIssued);
        Assert.Null(gewaehlt.Authority);
        Assert.Equal("pc.tailnet.ts.net", CertificateLoader.DnsName(gewaehlt.Certificate));
    }

    [Fact]
    public void Die_eingetragene_Adresse_steht_vorn()
    {
        // Der erste Name ist der Antragsteller — und der, den der QR-Code trägt.
        var namen = CertificateLoader.Names("pc.fritz.box", "DAVID-PC", ["192.168.178.20"]);

        Assert.Equal("pc.fritz.box", namen[0]);
    }

    [Fact]
    public void Ohne_eingetragene_Adresse_gilt_der_Rechnername()
    {
        var namen = CertificateLoader.Names(null, "DAVID-PC", []);

        Assert.Equal("david-pc", namen[0]);
    }

    [Fact]
    public void Localhost_ist_immer_dabei()
    {
        // Das Einrichtungsfenster spricht den Agent auf demselben Rechner an.
        Assert.Contains("localhost", CertificateLoader.Names("pc", "PC", []));
        Assert.Contains("127.0.0.1", CertificateLoader.Names("pc", "PC", []));
    }

    [Fact]
    public void Derselbe_Name_zweimal_bleibt_einer()
    {
        var namen = CertificateLoader.Names("PC", "pc", ["192.168.178.20", "192.168.178.20"]);

        Assert.Equal(namen.Distinct().Count(), namen.Count);
    }
}
