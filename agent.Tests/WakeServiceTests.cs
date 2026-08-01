using Microsoft.Extensions.Logging.Abstractions;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Merkt sich, was hinausgegangen wäre, statt einen Broadcast ins Netz zu
/// schicken.
/// </summary>
public sealed class AufgezeichneterSender : IMagicPacketSender
{
    public List<string> Gesendet { get; } = [];

    /// <summary>Wenn gesetzt, scheitert das Senden — für den Fehlerpfad.</summary>
    public Exception? Fehler { get; set; }

    public Task SendAsync(string mac, CancellationToken cancellationToken)
    {
        if (Fehler is not null)
        {
            throw Fehler;
        }

        Gesendet.Add(mac);
        return Task.CompletedTask;
    }
}

public class WakeServiceTests
{
    private readonly AufgezeichneterSender _sender = new();
    private readonly TestClock _clock = new();

    private WakeService Create() =>
        new(_sender, _clock, NullLogger<WakeService>.Instance);

    [Fact]
    public async Task Eine_gueltige_MAC_geht_hinaus()
    {
        var outcome = await Create().WakeAsync("AA:BB:CC:DD:EE:FF", default);

        Assert.Equal(WakeOutcome.Sent, outcome);
        Assert.Equal(["aa:bb:cc:dd:ee:ff"], _sender.Gesendet);
    }

    [Fact]
    public async Task Was_keine_MAC_ist_geht_gar_nicht_erst_hinaus()
    {
        var outcome = await Create().WakeAsync("kein-rechner", default);

        Assert.Equal(WakeOutcome.BadMac, outcome);
        Assert.Empty(_sender.Gesendet);
    }

    /// <summary>
    /// Ohne Begrenzung taugte der Agent als Paket-Verstärker: ein gekoppelter
    /// Client könnte ihn beliebig oft Broadcasts aussenden lassen.
    /// </summary>
    [Fact]
    public async Task Nach_zehn_Versuchen_in_einer_Minute_ist_Schluss()
    {
        var wake = Create();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.Equal(WakeOutcome.Sent, await wake.WakeAsync("aa:bb:cc:dd:ee:ff", default));
        }

        Assert.Equal(WakeOutcome.TooMany, await wake.WakeAsync("aa:bb:cc:dd:ee:ff", default));
        Assert.Equal(10, _sender.Gesendet.Count);
    }

    [Fact]
    public async Task Eine_Minute_spaeter_geht_es_weiter()
    {
        var wake = Create();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await wake.WakeAsync("aa:bb:cc:dd:ee:ff", default);
        }

        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(WakeOutcome.Sent, await wake.WakeAsync("aa:bb:cc:dd:ee:ff", default));
    }

    /// <summary>
    /// Ein Fehler beim Senden nennt Schnittstellen und Adressen. Nach außen
    /// geht nur, dass es nicht ging — die Meldung steht im Log.
    /// </summary>
    [Fact]
    public async Task Ein_Fehler_beim_Senden_wird_gemeldet_und_nicht_geworfen()
    {
        _sender.Fehler = new InvalidOperationException("Netzwerk weg");

        Assert.Equal(WakeOutcome.Failed, await Create().WakeAsync("aa:bb:cc:dd:ee:ff", default));
    }
}
