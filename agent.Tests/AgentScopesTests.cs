using System.Reflection;
using RemoteDesktopAgent.Api;
using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die Zuordnung Pfad → Recht. Sie ist eine Whitelist: was hier fehlt, wird
/// abgelehnt.
/// </summary>
public class AgentScopesTests
{
    [Theory]
    [InlineData("/ws/screen", AgentScopes.Screen)]
    [InlineData("/api/webrtc/offer", AgentScopes.Screen)]
    [InlineData("/api/webrtc/abc/monitor", AgentScopes.Screen)]
    [InlineData("/ws/input", AgentScopes.Input)]
    [InlineData("/api/media", AgentScopes.Media)]
    [InlineData("/api/media/sessions", AgentScopes.Media)]
    [InlineData("/api/power", AgentScopes.Power)]
    public void Jeder_Endpoint_verlangt_sein_Recht(string path, string expected)
    {
        // Act
        var known = AgentScopes.TryResolve(path, out var scope);

        // Assert
        Assert.True(known);
        Assert.Equal(expected, scope);
    }

    [Fact]
    public void Die_Auskunft_ueber_den_Rechner_verlangt_keins()
    {
        // Act
        var known = AgentScopes.TryResolve("/api/info", out var scope);

        // Assert
        Assert.True(known);
        Assert.Null(scope);
    }

    [Fact]
    public void Ein_unbekannter_Pfad_bleibt_unbekannt()
    {
        // Assert — der Aufrufer lehnt dann ab. Ein „null heißt erlaubt" wäre
        // hier genau die Lücke, die man später sucht.
        Assert.False(AgentScopes.TryResolve("/api/aktionen", out _));
        Assert.False(AgentScopes.TryResolve("/", out _));
    }

    [Fact]
    public void Ein_aehnlich_beginnender_Pfad_erbt_das_Recht_nicht()
    {
        // Assert — ein reiner Präfixvergleich würde /api/powerful als
        // /api/power durchgehen lassen.
        Assert.False(AgentScopes.TryResolve("/api/powerful", out _));
        Assert.False(AgentScopes.TryResolve("/api/informationen", out _));
    }

    [Fact]
    public void Die_Rechte_sind_genau_die_sechs_aus_dem_Plan()
    {
        // Assert
        Assert.Equal(
            ["screen", "input", "media", "power", "actions", "wake"],
            AgentScopes.All);

        Assert.All(AgentScopes.All, scope => Assert.True(AgentScopes.IsKnown(scope)));
        Assert.False(AgentScopes.IsKnown("alles"));
    }
}

/// <summary>
/// Die Wege der eigenen Oberfläche liegen unterhalb von <c>/api/pair</c> — und
/// das ist absichtlich ohne Ausweis erreichbar, weil der Kopplungsaufruf die
/// Berechtigung erst erzeugt. Ohne einen eigenen Eintrag käme jeder im Netz an
/// einen Kopplungscode, könnte sich selbst eintragen lassen oder die Steckbriefe
/// der gekoppelten Geräte abholen.
/// </summary>
public class LocalOnlyReachabilityTests
{
    [Theory]
    [InlineData("/api/pair/code")]
    [InlineData("/api/pair/self")]
    [InlineData("/api/pair/peers")]
    [InlineData("/api/pair/grant")]
    [InlineData("/api/clients")]
    public void Nur_am_Rechner_selbst(string path)
    {
        var localOnly = typeof(ClientAuthMiddleware)
            .GetField("LocalOnly", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as string[];

        Assert.Contains(path, localOnly!);
    }

    /// <summary>
    /// Der Widerruf von außen liegt **nicht** unter <c>/api/pair/…</c>. Alles
    /// darunter ist ohne Ausweis erreichbar, weil der Kopplungsaufruf die
    /// Berechtigung erst erzeugt — ein Widerruf ohne Ausweis wäre ein Weg, die
    /// Kopplung eines fremden Geräts über das Netz zu beenden.
    /// </summary>
    [Fact]
    public void Sich_selbst_austragen_verlangt_einen_Ausweis()
    {
        var withoutCredential = typeof(ClientAuthMiddleware)
            .GetField("WithoutCredential", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as string[];

        Assert.All(
            withoutCredential!,
            entry => Assert.False(
                "/api/unpair".Equals(entry, StringComparison.OrdinalIgnoreCase)
                || "/api/unpair".StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase)));

        // Und ein Recht verlangt er nicht: wer sich austrägt, gibt etwas auf.
        Assert.True(AgentScopes.TryResolve("/api/unpair", out var scope));
        Assert.Null(scope);
    }
}
