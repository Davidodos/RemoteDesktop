using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die <c>clients.json</c>. Sie ist die einzige Wahrheit darüber, wer diesen
/// Rechner steuern darf — geht sie verloren, kommt niemand mehr herein.
/// </summary>
public class ClientStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"clients-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ohne_Datei_ist_die_Liste_leer()
    {
        // Act
        var store = new ClientStore(_path);

        // Assert — der erste Start ist kein Fehlerfall.
        Assert.Empty(store.List());
    }

    [Fact]
    public void Ein_Eintrag_uebersteht_den_Neustart()
    {
        // Arrange
        new ClientStore(_path).Add(Sample());

        // Act
        var reloaded = new ClientStore(_path);

        // Assert
        var client = Assert.Single(reloaded.List());
        Assert.Equal("abc123", client.Id);
        Assert.Equal(["screen", "input"], client.Scopes);
    }

    [Fact]
    public void In_der_Datei_steht_kein_Klartext_Token()
    {
        // Arrange
        new ClientStore(_path).Add(Sample());

        // Act
        var content = File.ReadAllText(_path);

        // Assert — nur der öffentliche Schlüssel. Wer die Datei liest, hat
        // nichts in der Hand.
        Assert.Contains("publicKey", content, StringComparison.Ordinal);
        Assert.DoesNotContain("token", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ein_Widerruf_wirkt_auch_nach_dem_Neustart()
    {
        // Arrange
        var store = new ClientStore(_path);
        store.Add(Sample());

        // Act
        var revoked = store.Revoke("abc123");

        // Assert
        Assert.True(revoked);
        Assert.Empty(new ClientStore(_path).List());
    }

    [Fact]
    public void Ein_unbekannter_Widerruf_meldet_das()
    {
        // Arrange
        var store = new ClientStore(_path);

        // Assert
        Assert.False(store.Revoke("gibtesnicht"));
    }

    [Fact]
    public void Zweimal_dieselbe_Kennung_bleibt_ein_Eintrag()
    {
        // Arrange
        var store = new ClientStore(_path);
        store.Add(Sample());

        // Act
        store.Add(Sample() with { Label = "Handy neu" });

        // Assert
        Assert.Equal("Handy neu", Assert.Single(store.List()).Label);
    }

    [Fact]
    public void Der_letzte_Besuch_wird_festgehalten()
    {
        // Arrange
        var store = new ClientStore(_path);
        store.Add(Sample());

        var seenAt = new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

        // Act
        store.Touch("abc123", seenAt);

        // Assert
        Assert.Equal(seenAt, new ClientStore(_path).Find("abc123")!.LastSeenAt);
    }

    [Fact]
    public void Eine_kaputte_Datei_wird_gemeldet_statt_verschluckt()
    {
        // Arrange
        File.WriteAllText(_path, "{ das ist kein JSON");

        // Assert — still mit leerer Liste weiterzulaufen hieße: alle
        // gekoppelten Geräte sind plötzlich fremd.
        Assert.ThrowsAny<Exception>(() => new ClientStore(_path));
    }

    private static PairedClient Sample() => new(
        "abc123",
        "Handy",
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE",
        ["screen", "input"],
        new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero));
}
