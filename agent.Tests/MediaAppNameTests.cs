using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Aufbereitung der App-Kennungen aus Windows. Was hier herauskommt, steht in
/// der App unter jedem laufenden Titel.
/// </summary>
public class MediaAppNameTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("vlc.exe", "Vlc")]
    [InlineData("chrome.exe", "Chrome")]
    public void Klassische_Programme_verlieren_ihre_Dateiendung(string input, string expected)
    {
        Assert.Equal(expected, MediaAppName.Describe(input));
    }

    [Fact]
    public void Store_Apps_werden_auf_den_Anwendungsteil_gekuerzt()
    {
        // Arrange — so meldet sich der Windows-Medienplayer.
        const string identifier = "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic";

        // Act + Assert
        Assert.Equal("Zune Music", MediaAppName.Describe(identifier));
    }

    [Fact]
    public void Der_Paket_Hash_verschwindet()
    {
        // Arrange
        const string identifier = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";

        // Act
        var name = MediaAppName.Describe(identifier);

        // Assert
        Assert.Equal("Spotify", name);
        Assert.DoesNotContain("_", name);
    }

    [Fact]
    public void Zusammengeschriebene_Namen_werden_getrennt()
    {
        Assert.Equal("Media Player", MediaAppName.Describe("MediaPlayer.exe"));
    }

    [Fact]
    public void Abkuerzungen_bleiben_zusammen()
    {
        // Assert — sonst stünde dort „V L C".
        Assert.Equal("VLC", MediaAppName.Describe("VLC.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ohne_Kennung_kommt_ein_Platzhalter(string? input)
    {
        Assert.Equal("Unbekannt", MediaAppName.Describe(input));
    }

    [Fact]
    public void Eine_Kennung_die_nur_aus_Trennern_besteht_ergibt_den_Platzhalter()
    {
        // Arrange — schon gesehen bei Apps, die sich unsauber registrieren.
        Assert.Equal("Unbekannt", MediaAppName.Describe("!"));
    }
}
