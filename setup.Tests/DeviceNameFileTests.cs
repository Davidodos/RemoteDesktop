using RemoteDesktopSetup;
using Xunit;

namespace RemoteDesktopSetup.Tests;

/// <summary>
/// Der eigene Gerätename. Er steht in jeder fremden Geräteliste — was hier
/// durchrutscht, sieht später jemand auf einem anderen Gerät und kann es dort
/// nicht mehr erklären.
/// </summary>
public sealed class DeviceNameFileTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"rd-name-{Guid.NewGuid():N}");

    public DeviceNameFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void OhneDateiGiltDerWindowsName()
    {
        Assert.Equal(Environment.MachineName, DeviceNameFile.Read(_folder));
        Assert.False(DeviceNameFile.IsSet(_folder));
    }

    [Fact]
    public void GeschriebenesWirdGelesen()
    {
        DeviceNameFile.Write(_folder, "Wohnzimmer-PC");

        Assert.Equal("Wohnzimmer-PC", DeviceNameFile.Read(_folder));
        Assert.True(DeviceNameFile.IsSet(_folder));
    }

    /// <summary>
    /// Ein Name mit Zeilenumbruch zerlegte die Datei in zwei Zeilen — gelesen
    /// wurde danach die erste, und der Rest war stillschweigend weg.
    /// </summary>
    [Fact]
    public void SteuerzeichenUndRandflaechenFallenWeg()
    {
        Assert.Equal("Laptop", DeviceNameFile.Sanitize("  Lap\ntop \t"));
        Assert.Null(DeviceNameFile.Sanitize("   "));
        Assert.Null(DeviceNameFile.Sanitize(null));
    }

    /// <summary>
    /// Länger nimmt die Gegenseite ihn nicht an (<c>DeviceProfile.MAX_NAME</c>).
    /// Ein Name, den die eine Seite schreibt und die andere verwirft, wäre
    /// schlimmer als ein gekürzter.
    /// </summary>
    [Fact]
    public void ZuLangWirdGekuerzt()
    {
        var lang = new string('a', DeviceNameFile.MaxLength + 20);

        DeviceNameFile.Write(_folder, lang);

        Assert.Equal(DeviceNameFile.MaxLength, DeviceNameFile.Read(_folder).Length);
    }

    [Fact]
    public void LeererNameWirdAbgewiesen() =>
        Assert.Throws<ArgumentException>(() => DeviceNameFile.Write(_folder, "  "));

    /// <summary>
    /// Der Ordner muss nicht vorher da sein: das Fenster schreibt den Namen im
    /// ersten Schritt der Einrichtung, also bevor der Agent je gelaufen ist.
    /// </summary>
    [Fact]
    public void FehlenderOrdnerEntsteht()
    {
        var neu = Path.Combine(_folder, "tiefer", "data");

        DeviceNameFile.Write(neu, "Handy");

        Assert.Equal("Handy", DeviceNameFile.Read(neu));
    }
}
