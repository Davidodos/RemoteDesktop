using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die Release-Antwort von GitHub ist das einzige fremde JSON, das der Agent
/// liest. Eine Fehlermeldung von dort ist ebenfalls gültiges JSON — deshalb
/// wird hier vor allem geprüft, dass alles Unpassende als „nichts gefunden"
/// endet und nicht als Ausnahme.
/// </summary>
public class GitHubReleaseTests
{
    private const string Antwort =
        """
        {
          "tag_name": "v1.2.0",
          "assets": [
            { "name": "manifest.json",
              "browser_download_url": "https://example.invalid/manifest.json" },
            { "name": "manifest.json.sig",
              "browser_download_url": "https://example.invalid/manifest.json.sig" },
            { "name": "RemoteDesktopAgent.exe",
              "browser_download_url": "https://example.invalid/agent.exe" }
          ]
        }
        """;

    [Fact]
    public void Die_Anhaenge_werden_ueber_ihren_Namen_gefunden()
    {
        var release = GitHubRelease.Parse(Antwort);

        Assert.NotNull(release);
        Assert.Equal("v1.2.0", release!.Tag);
        Assert.Equal("https://example.invalid/manifest.json", release.Download(GitHubRelease.ManifestAsset));
        Assert.Equal("https://example.invalid/manifest.json.sig", release.Download(GitHubRelease.SignatureAsset));
        Assert.Equal("https://example.invalid/agent.exe", release.Download("RemoteDesktopAgent.exe"));
    }

    [Fact]
    public void Ein_nicht_vorhandener_Anhang_ergibt_null()
    {
        Assert.Null(GitHubRelease.Parse(Antwort)!.Download("remotedesktop.apk"));
    }

    [Theory]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("""{ "assets": "keine Liste" }""")]
    [InlineData("[]")]
    [InlineData("kein JSON")]
    public void Alles_andere_ergibt_kein_Release(string json)
    {
        Assert.Null(GitHubRelease.Parse(json));
    }

    /// <summary>
    /// Ein Anhang ohne Adresse wird übersprungen, statt die ganze Antwort
    /// unbrauchbar zu machen — die übrigen reichen für ein Update.
    /// </summary>
    [Fact]
    public void Ein_unvollstaendiger_Anhang_kostet_nicht_die_ganze_Liste()
    {
        var release = GitHubRelease.Parse(
            """
            { "tag_name": "v1", "assets": [
              { "name": "kaputt" },
              { "name": "manifest.json", "browser_download_url": "https://example.invalid/m" } ] }
            """);

        Assert.NotNull(release);
        Assert.Equal("https://example.invalid/m", release!.Download(GitHubRelease.ManifestAsset));
        Assert.Null(release.Download("kaputt"));
    }
}
