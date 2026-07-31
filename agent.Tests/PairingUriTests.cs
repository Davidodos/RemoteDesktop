using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Inhalt des QR-Codes ist eine Schnittstelle zwischen zwei Programmen, die
/// getrennt aktualisiert werden: dieser Rechner erzeugt ihn, das Handy liest ihn
/// mit <c>app/src/lib/pairingUri.ts</c>. Läuft eine Seite weg, koppelt niemand
/// mehr — und zwar ohne dass es vorher irgendwo auffällt. Deshalb prüfen diese
/// Tests denselben Vertrag, den die Tests dort auf der anderen Seite prüfen.
/// </summary>
public class PairingUriTests
{
    [Fact]
    public void Der_Uri_enthaelt_Rechner_Port_und_Code()
    {
        // Act
        var uri = PairingUri.Build("arbeitsrechner", 8443, "123456");

        // Assert — Reihenfolge und Schreibweise sind Teil des Vertrags, nicht
        // Geschmackssache: die Gegenseite liest sie mit URLSearchParams.
        Assert.Equal("remotedesktop://pair?host=arbeitsrechner&port=8443&code=123456", uri);
    }

    [Fact]
    public void Ein_abweichender_Port_steht_drin()
    {
        // Der Agent lauscht fast immer auf 8443, aber „fast immer" ist kein
        // Grund, den Port wegzulassen — die Gegenseite fiele sonst still auf
        // die Vorgabe zurück und liefe ins Leere.
        Assert.Contains("port=9443", PairingUri.Build("laptop", 9443, "000001"));
    }

    [Fact]
    public void Sonderzeichen_im_Rechnernamen_werden_kodiert()
    {
        // Act
        var uri = PairingUri.Build("büro pc", 8443, "123456");

        // Assert — ein rohes Leerzeichen machte aus dem QR-Code eine Adresse,
        // die die Gegenseite gar nicht erst parst.
        Assert.Contains("host=b%C3%BCro%20pc", uri);
        Assert.DoesNotContain(" ", uri);
    }

    [Fact]
    public void Der_Rechnername_wird_kleingeschrieben()
    {
        // Environment.MachineName liefert unter Windows Großbuchstaben,
        // MagicDNS kennt aber nur Kleinschreibung. Wer den Namen abtippt, merkt
        // das nicht; wer ihn scannt, liefe in einen Namen, den es nicht gibt.
        Assert.Contains("host=pc-david", PairingUri.Build("PC-DAVID", 8443, "123456"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Rechnernamen_gibt_es_keinen_Uri(string host)
    {
        // Ein QR-Code ohne Rechnernamen sähe gültig aus und wäre wertlos. Lieber
        // zeigt das Fenster gar keinen an.
        Assert.Throws<ArgumentException>(() => PairingUri.Build(host, 8443, "123456"));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("")]
    public void Ein_Code_der_nicht_sechs_Ziffern_hat_wird_abgelehnt(string code)
    {
        // Dieselbe Prüfung wie auf der Leseseite. Sie hier zu wiederholen ist
        // keine Doppelung, sondern der Grund, warum ein Fehler am Rechner
        // auffällt und nicht erst am Handy.
        Assert.Throws<ArgumentException>(() => PairingUri.Build("pc", 8443, code));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Ein_unmoeglicher_Port_wird_abgelehnt(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PairingUri.Build("pc", port, "123456"));
    }
}
