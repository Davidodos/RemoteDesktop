using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace RemoteDesktopSetup.Tests;

/// <summary>
/// Nachsehen, was im Zertifikat steht.
///
/// Der Befund, den das verhindert: die Einrichtung fragte nur, *ob* eine Datei
/// dalag. Am echten Gerät meldete sie daraufhin „abgeschlossen", und das Handy
/// bekam trotzdem die Rückfrage nach einem selbst ausgestellten Zertifikat.
/// </summary>
public class AgentCertificateTests
{
    private static X509Certificate2 Certificate(
        IEnumerable<string>? names = null,
        IEnumerable<string>? addresses = null,
        int validDays = 90)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            "CN=RemoteDesktop-Test", key, HashAlgorithmName.SHA256);

        var alternatives = new SubjectAlternativeNameBuilder();

        foreach (var name in names ?? [])
        {
            alternatives.AddDnsName(name);
        }

        foreach (var address in addresses ?? [])
        {
            alternatives.AddIpAddress(IPAddress.Parse(address));
        }

        if (names is not null || addresses is not null)
        {
            request.CertificateExtensions.Add(alternatives.Build());
        }

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(validDays));
    }

    [Fact]
    public void Die_Namen_kommen_aus_der_Erweiterung_und_nicht_aus_dem_Antragsteller()
    {
        // Nur `subjectAltName` zählt beim Verbinden. Der Antragsteller wird von
        // keinem aktuellen Client mehr angesehen.
        using var certificate = Certificate(["pc.example.ts.net"]);

        var gelesen = AgentCertificate.From(certificate);

        Assert.Equal(["pc.example.ts.net"], gelesen.Names);
        Assert.True(gelesen.Covers("pc.example.ts.net"));
        Assert.True(gelesen.Covers("PC.Example.TS.NET"));
        Assert.False(gelesen.Covers("laptop.example.ts.net"));
    }

    [Fact]
    public void Ohne_Erweiterung_gilt_der_Antragsteller()
    {
        using var certificate = Certificate();

        Assert.Equal(["remotedesktop-test"], AgentCertificate.From(certificate).Names);
    }

    [Fact]
    public void IP_Adressen_zaehlen_mit()
    {
        // Im Heimnetz tippt man die IP und nicht den Namen. Ohne sie hielte die
        // Prüfung jedes Heimnetz-Zertifikat für unpassend.
        using var certificate = Certificate(["pc"], ["192.168.178.20"]);

        var gelesen = AgentCertificate.From(certificate);

        Assert.True(gelesen.Covers("192.168.178.20"));
        Assert.False(gelesen.Covers("192.168.178.21"));
    }

    [Fact]
    public void Ein_Platzhalter_deckt_genau_eine_Ebene()
    {
        using var certificate = Certificate(["*.example.ts.net"]);

        var gelesen = AgentCertificate.From(certificate);

        Assert.True(gelesen.Covers("pc.example.ts.net"));
        Assert.False(gelesen.Covers("a.b.example.ts.net"));
        Assert.False(gelesen.Covers("example.ts.net"));
    }

    [Fact]
    public void Eine_leere_Adresse_wird_von_nichts_gedeckt()
    {
        using var certificate = Certificate(["pc.example.ts.net"]);

        var gelesen = AgentCertificate.From(certificate);

        Assert.False(gelesen.Covers(null));
        Assert.False(gelesen.Covers("   "));
    }

    [Fact]
    public void Ein_abgelaufenes_Zertifikat_ist_keins()
    {
        // Der Agent zeigte es trotzdem vor, und jede Verbindung scheiterte
        // daran — während im Fenster „eingerichtet" stand.
        using var certificate = Certificate(["pc.example.ts.net"], validDays: 30);

        var gelesen = AgentCertificate.From(certificate);

        Assert.True(gelesen.IsValidAt(DateTimeOffset.UtcNow));
        Assert.False(gelesen.IsValidAt(DateTimeOffset.UtcNow.AddDays(31)));
    }

    [Fact]
    public void Was_geschrieben_wurde_wird_aus_der_Datei_wieder_gelesen()
    {
        using var certificate = Certificate(["pc.example.ts.net"]);

        var path = Path.Combine(Path.GetTempPath(), $"rd-test-{Guid.NewGuid():N}.crt");

        try
        {
            File.WriteAllText(path, certificate.ExportCertificatePem());

            Assert.True(AgentCertificate.Read(path)?.Covers("pc.example.ts.net"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Eine_kaputte_oder_fehlende_Datei_ist_dasselbe_wie_keine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-test-{Guid.NewGuid():N}.crt");

        Assert.Null(AgentCertificate.Read(path));

        try
        {
            File.WriteAllText(path, "das ist kein Zertifikat");

            Assert.Null(AgentCertificate.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Aus zwei Ja/Nein-Fragen wird ein Autostart-Modus.
/// </summary>
public class AutostartFromTests
{
    [Theory]
    [InlineData(false, false, AutostartMode.None)]
    [InlineData(false, true, AutostartMode.None)]
    [InlineData(true, false, AutostartMode.Client)]
    [InlineData(true, true, AutostartMode.Both)]
    public void Die_beiden_Fragen_ergeben_den_Modus(
        bool withWindows, bool withAgent, AutostartMode expected)
    {
        // Wer die erste Frage verneint, hat keine zweite — die Antwort darauf
        // darf dann auch nichts mehr ändern.
        Assert.Equal(expected, AutostartModes.From(withWindows, withAgent));
    }

    [Fact]
    public void Was_gesetzt_wurde_laesst_sich_wieder_als_zwei_Fragen_lesen()
    {
        foreach (var withWindows in new[] { true, false })
        {
            foreach (var withAgent in new[] { true, false })
            {
                var mode = AutostartModes.From(withWindows, withAgent);

                Assert.Equal(withWindows, mode.WithWindows());
                Assert.Equal(withWindows && withAgent, mode.Starts(AutostartMode.Agent));
            }
        }
    }
}
