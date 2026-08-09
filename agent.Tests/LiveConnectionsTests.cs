using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Widerruf muss auch das treffen, was schon offen ist.
///
/// Am echten Gerät bekam ein entferntes Handy sofort die Meldung, es sei
/// entfernt worden — und steuerte trotzdem weiter, bis jemand die App schloss.
/// Genau diese Lücke messen diese Prüfungen.
/// </summary>
public class LiveConnectionsTests
{
    [Fact]
    public void Widerruf_beendet_die_offene_Verbindung_sofort()
    {
        var live = new LiveConnections();

        using var lease = live.Open("handy", CancellationToken.None);

        Assert.False(lease.Token.IsCancellationRequested);
        Assert.Equal(1, live.Close("handy"));
        Assert.True(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public void Widerruf_trifft_alle_Verbindungen_desselben_Geraets()
    {
        // Bild und Eingabe laufen über getrennte WebSockets. Nur einen davon zu
        // trennen hieße: kein Bild mehr, aber die Maus geht weiter.
        var live = new LiveConnections();

        using var bild = live.Open("handy", CancellationToken.None);
        using var eingabe = live.Open("handy", CancellationToken.None);

        Assert.Equal(2, live.Close("handy"));
        Assert.True(bild.Token.IsCancellationRequested);
        Assert.True(eingabe.Token.IsCancellationRequested);
    }

    [Fact]
    public void Ein_fremdes_Geraet_bleibt_verbunden()
    {
        var live = new LiveConnections();

        using var meins = live.Open("handy", CancellationToken.None);
        using var fremdes = live.Open("tablet", CancellationToken.None);

        live.Close("handy");

        Assert.True(meins.Token.IsCancellationRequested);
        Assert.False(fremdes.Token.IsCancellationRequested);
    }

    [Fact]
    public void Eine_beendete_Verbindung_bleibt_nicht_in_der_Liste()
    {
        // Sonst wüchse sie mit jedem Verbindungsversuch, und ein Widerruf
        // meldete Verbindungen, die es längst nicht mehr gibt.
        var live = new LiveConnections();

        var lease = live.Open("handy", CancellationToken.None);

        Assert.Equal(1, live.CountFor("handy"));

        lease.Dispose();

        Assert.Equal(0, live.CountFor("handy"));
        Assert.Equal(0, live.Close("handy"));
    }

    [Fact]
    public void Der_Abbruch_der_Anfrage_wirkt_weiterhin()
    {
        // Der übliche Fall: das Handy legt selbst auf. Er darf durch die
        // Anmeldung nicht verlorengehen.
        var live = new LiveConnections();
        using var request = new CancellationTokenSource();

        using var lease = live.Open("handy", request.Token);

        request.Cancel();

        Assert.True(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public void Ohne_gekoppeltes_Geraet_wird_nichts_gefuehrt()
    {
        // Das alte Sammel-Token kennt kein Gerät. Es lässt sich deshalb auch
        // nicht widerrufen — und darf keinen Eintrag hinterlassen, den niemand
        // je wieder abräumt.
        var live = new LiveConnections();

        using var lease = live.Open(null, CancellationToken.None);

        Assert.Equal(0, live.CountFor(string.Empty));
        Assert.False(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public void Ein_unbekanntes_Geraet_zu_widerrufen_ist_kein_Fehler()
    {
        Assert.Equal(0, new LiveConnections().Close("gibtesnicht"));
    }
}
