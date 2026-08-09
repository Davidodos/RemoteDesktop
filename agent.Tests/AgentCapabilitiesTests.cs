using RemoteDesktopAgent.Auth;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

public class AgentCapabilitiesTests
{
    [Fact]
    public void Windows_meldet_alles_ausser_Dateien()
    {
        Assert.Equal(
            ["screen", "input", "keys", "media", "power", "actions", "wake"],
            AgentCapabilities.Windows);
    }

    /// <summary>
    /// Fähigkeit und Recht sind zwei Begriffe, aber wo sie dasselbe meinen,
    /// müssen sie auch gleich heißen: die App vergleicht beide Listen
    /// miteinander, bevor sie eine Seite zeigt. Ein Tippfehler auf einer Seite
    /// bliebe sonst unbemerkt, bis jemand vor einer leeren Oberfläche steht.
    /// </summary>
    [Theory]
    [InlineData(AgentCapabilities.Screen, AgentScopes.Screen)]
    [InlineData(AgentCapabilities.Input, AgentScopes.Input)]
    [InlineData(AgentCapabilities.Media, AgentScopes.Media)]
    [InlineData(AgentCapabilities.Power, AgentScopes.Power)]
    [InlineData(AgentCapabilities.Actions, AgentScopes.Actions)]
    [InlineData(AgentCapabilities.Wake, AgentScopes.Wake)]
    public void Faehigkeit_und_Recht_heissen_gleich(string capability, string scope)
    {
        Assert.Equal(scope, capability);
    }

    /// <summary>
    /// „keys“ ist kein Recht: wer Eingaben schicken darf, darf auch Tasten
    /// schicken. Es sagt nur, ob am anderen Ende überhaupt etwas ankommt.
    /// </summary>
    [Fact]
    public void Keys_ist_kein_Recht()
    {
        Assert.False(AgentScopes.IsKnown(AgentCapabilities.Keys));
    }
}
