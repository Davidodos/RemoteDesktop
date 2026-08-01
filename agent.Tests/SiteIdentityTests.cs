using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die Standort-Kennung entscheidet, wer einen schlafenden Rechner wecken darf.
/// Zwei Fehler wären teuer: zwei verschiedene Netze mit derselben Kennung (dann
/// ginge das Magic Packet ins Leere und niemand wüsste warum) und dasselbe Netz
/// mit zwei Kennungen (dann bliebe der Knopf grundlos aus).
/// </summary>
public class SiteIdentityTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff")]
    [InlineData("AABBCCDDEEFF")]
    [InlineData("aabb.ccdd.eeff")]
    public void Dieselbe_Adresse_in_jeder_Schreibweise_ergibt_dieselbe_Form(string mac)
    {
        Assert.Equal("aa:bb:cc:dd:ee:ff", SiteIdentity.NormalizeMac(mac));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aa:bb:cc:dd:ee")]
    [InlineData("aa:bb:cc:dd:ee:ff:00")]
    [InlineData("zz:bb:cc:dd:ee:ff")]
    public void Was_keine_MAC_ist_wird_nicht_zurechtgebogen(string? mac)
    {
        Assert.Null(SiteIdentity.NormalizeMac(mac));
    }

    /// <summary>
    /// Manche Adapter melden lauter Nullen, wenn sie gar keine Hardware-Adresse
    /// haben. Als Standort-Kennung wäre das die eine, die überall gleich ist —
    /// und damit genau der Fehler, den es zu vermeiden gilt.
    /// </summary>
    [Fact]
    public void Die_Nulladresse_gilt_nicht_als_MAC()
    {
        Assert.Null(SiteIdentity.NormalizeMac("00:00:00:00:00:00"));
    }

    [Fact]
    public void Gleiches_Gateway_ergibt_dieselbe_Kennung_unabhaengig_von_der_Schreibweise()
    {
        Assert.Equal(
            SiteIdentity.FromGatewayMac("AA:BB:CC:DD:EE:FF"),
            SiteIdentity.FromGatewayMac("aabbccddeeff"));
    }

    [Fact]
    public void Ein_anderes_Gateway_ergibt_eine_andere_Kennung()
    {
        Assert.NotEqual(
            SiteIdentity.FromGatewayMac("aa:bb:cc:dd:ee:ff"),
            SiteIdentity.FromGatewayMac("aa:bb:cc:dd:ee:00"));
    }

    /// <summary>
    /// Die Kennung geht über das Netz an jeden gekoppelten Client. Dass die MAC
    /// des eigenen Routers dabei nicht im Klartext mitfährt, ist der Zweck des
    /// Hashens — deshalb wird es hier auch geprüft.
    /// </summary>
    [Fact]
    public void Die_Kennung_verraet_die_MAC_nicht()
    {
        var siteId = SiteIdentity.FromGatewayMac("aa:bb:cc:dd:ee:ff");

        Assert.NotNull(siteId);
        Assert.Equal(64, siteId!.Length);
        Assert.DoesNotContain("aabbccddeeff", siteId);
        Assert.DoesNotContain("aa:bb", siteId);
    }

    /// <summary>
    /// Der Waker rechnet dasselbe (<c>waker/src/site.test.ts</c>, „rechnet
    /// genauso wie der Agent"). Beide Tests halten denselben festen Wert —
    /// weicht eine Seite ab, fällt es hier auf und nicht erst daran, dass der
    /// Weckknopf beim Nutzer grundlos aus bleibt.
    /// </summary>
    [Fact]
    public void Der_Agent_rechnet_dieselbe_Kennung_wie_der_Waker()
    {
        Assert.Equal(
            "c1582e87c802221899199e286ead9a7ed13eb3b5e3827be6cc149fb82a9e04f7",
            SiteIdentity.FromGatewayMac("aa:bb:cc:dd:ee:ff"));
    }

    [Fact]
    public void Ohne_brauchbares_Gateway_gibt_es_keine_Kennung()
    {
        Assert.Null(SiteIdentity.FromGatewayMac(null));
        Assert.Null(SiteIdentity.FromGatewayMac("kein Gateway"));
    }

    [Fact]
    public void Genommen_wird_die_Schnittstelle_mit_Gateway()
    {
        var site = SiteIdentity.Resolve([
            new NetworkAdapter("Tailscale", "aa:00:00:00:00:01", null),
            new NetworkAdapter("Ethernet", "bb:00:00:00:00:02", "cc:cc:cc:cc:cc:cc")
        ]);

        Assert.Equal(SiteIdentity.FromGatewayMac("cc:cc:cc:cc:cc:cc"), site.SiteId);
        Assert.Equal("bb:00:00:00:00:02", site.Mac);
    }

    /// <summary>
    /// Ohne Gateway gibt es keine Kennung — die eigene MAC wird trotzdem
    /// gemeldet. Sie ist auch dann richtig, und ohne sie könnte niemand diesen
    /// Rechner wecken, selbst wenn er im selben Netz steht.
    /// </summary>
    [Fact]
    public void Ohne_Gateway_bleibt_die_Kennung_leer_und_die_MAC_steht_trotzdem_da()
    {
        var site = SiteIdentity.Resolve([
            new NetworkAdapter("Tailscale", "aa:00:00:00:00:01", null)
        ]);

        Assert.Null(site.SiteId);
        Assert.Equal("aa:00:00:00:00:01", site.Mac);
    }

    [Fact]
    public void Ohne_jede_Schnittstelle_bleibt_beides_leer()
    {
        var site = SiteIdentity.Resolve([]);

        Assert.Null(site.SiteId);
        Assert.Null(site.Mac);
    }
}
