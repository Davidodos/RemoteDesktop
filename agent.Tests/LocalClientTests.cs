using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Ausweis der eigenen Oberfläche. Ohne ihn bliebe jede Kopplung einseitig:
/// die Gegenseite bekäme in der Antwort nichts, was sie in ihre eigene Liste
/// eintragen könnte.
/// </summary>
public class LocalClientTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"localclient-{Guid.NewGuid():N}");

    private string Path_ => System.IO.Path.Combine(_folder, "localclient.json");

    [Fact]
    public void Vor_dem_ersten_Start_des_Fensters_gibt_es_keinen()
    {
        Assert.Null(new LocalClient(Path_).PublicKey);
    }

    [Fact]
    public void Hinterlegt_und_nach_einem_Neustart_noch_da()
    {
        using var client = new TestClient();

        Assert.True(new LocalClient(Path_).Remember(client.PublicKey));
        Assert.Equal(client.PublicKey, new LocalClient(Path_).PublicKey);
    }

    [Fact]
    public void Was_kein_Schluessel_ist_wird_abgelehnt()
    {
        var local = new LocalClient(Path_);

        // Abgelehnt und nicht gespeichert: er landete sonst als Karteileiche im
        // Steckbrief, den dieser Rechner an jede Gegenseite ausliefert.
        Assert.False(local.Remember("kein Schlüssel"));
        Assert.False(local.Remember(null));
        Assert.Null(local.PublicKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
