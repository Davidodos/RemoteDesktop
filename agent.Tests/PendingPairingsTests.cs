using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Das Angebot zur Gegenkopplung: einmal abzuholen, kurz gültig, und was nicht
/// vollständig ist, wird gar nicht erst aufgehoben.
/// </summary>
public class PendingPairingsTests
{
    private readonly TestClock _time = new();

    private PendingPairings Store() => new(_time);

    private static BackPairing Offer() =>
        PendingPairings.Sanitize("192.168.178.31", 8443, "123456", new string('a', 64), "Pixel")!;

    [Fact]
    public void Ein_Angebot_laesst_sich_genau_einmal_abholen()
    {
        var store = Store();
        store.Offer(Offer());

        Assert.Equal("192.168.178.31", store.Take()?.Host);

        // Ein zweiter Griff muss leer ausgehen: sonst versuchte die Oberfläche
        // bei jedem Nachsehen erneut, einen längst eingelösten Code zu benutzen.
        Assert.Null(store.Take());
    }

    [Fact]
    public void Ohne_Angebot_kommt_nichts()
    {
        Assert.Null(Store().Take());
    }

    [Fact]
    public void Ein_zweites_Angebot_ersetzt_das_erste()
    {
        var store = Store();

        store.Offer(Offer());
        store.Offer(PendingPairings.Sanitize("192.168.178.44", 8443, "654321", null, null)!);

        Assert.Equal("192.168.178.44", store.Take()?.Host);
    }

    [Fact]
    public void Ein_abgelaufenes_Angebot_wird_nicht_mehr_herausgegeben()
    {
        var store = Store();
        store.Offer(Offer());

        _time.Advance(PendingPairings.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(store.Take());
    }

    [Theory]
    [InlineData("", 8443, "123456")]
    [InlineData("192.168.178.31", 0, "123456")]
    [InlineData("192.168.178.31", 70000, "123456")]
    [InlineData("192.168.178.31", 8443, "12345")]
    [InlineData("192.168.178.31", 8443, "abcdef")]
    public void Unvollstaendiges_wird_verworfen(string host, int port, string code)
    {
        // Die Kopplung selbst gelingt trotzdem — nur eben in eine Richtung. Ein
        // halbes Angebot aufzuheben hieße, später an einer Stelle zu scheitern,
        // an der niemand mehr weiß, woher es kam.
        Assert.Null(PendingPairings.Sanitize(host, port, code, null, null));
    }

    [Fact]
    public void Ein_unbrauchbarer_Fingerabdruck_kostet_nur_ihn_selbst()
    {
        var offer = PendingPairings.Sanitize("192.168.178.31", 8443, "123456", "kaputt", null);

        Assert.NotNull(offer);
        Assert.Null(offer!.CaFingerprint);
    }
}
