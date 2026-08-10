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
/// Das Angebot zur Gegenkopplung enthält einen gültigen Kopplungscode der
/// anderen Seite. Es liegt unterhalb von <c>/api/pair</c> — und das ist
/// absichtlich ohne Ausweis erreichbar, weil der Kopplungsaufruf die
/// Berechtigung erst erzeugt. Ohne einen eigenen Eintrag käme es damit jedem im
/// Netz zu.
/// </summary>
public class PendingPairingReachabilityTests
{
    [Fact]
    public void Das_Angebot_ist_nur_am_Rechner_selbst_zu_holen()
    {
        var localOnly = typeof(ClientAuthMiddleware)
            .GetField("LocalOnly", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as string[];

        Assert.Contains("/api/pair/pending", localOnly!);
    }
}
