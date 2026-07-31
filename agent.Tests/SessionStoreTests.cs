using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die offenen Sitzungen. Sie liegen nur im Arbeitsspeicher — nichts auf der
/// Platte darf einen Zugang öffnen.
/// </summary>
public class SessionStoreTests
{
    private readonly TestClock _clock = new();

    [Fact]
    public void Ein_frisches_Token_findet_seine_Sitzung()
    {
        // Arrange
        var sessions = new SessionStore(_clock);

        // Act
        var token = sessions.Open(Client("handy", AgentScopes.Screen));

        // Assert
        var session = sessions.Find(token);
        Assert.NotNull(session);
        Assert.Equal("handy", session.ClientId);
        Assert.True(session.Allows(AgentScopes.Screen));
        Assert.False(session.Allows(AgentScopes.Power));
    }

    [Fact]
    public void Zwei_Sitzungen_bekommen_verschiedene_Tokens()
    {
        // Arrange
        var sessions = new SessionStore(_clock);

        // Act
        var first = sessions.Open(Client("handy", AgentScopes.Screen));
        var second = sessions.Open(Client("laptop", AgentScopes.Screen));

        // Assert
        Assert.NotEqual(first, second);
        Assert.Equal("handy", sessions.Find(first)!.ClientId);
        Assert.Equal("laptop", sessions.Find(second)!.ClientId);
    }

    [Fact]
    public void Ein_erfundenes_Token_findet_nichts()
    {
        // Arrange
        var sessions = new SessionStore(_clock);
        sessions.Open(Client("handy", AgentScopes.Screen));

        // Assert
        Assert.Null(sessions.Find("ausgedacht"));
        Assert.Null(sessions.Find(string.Empty));
    }

    [Fact]
    public void Nach_zwoelf_Stunden_ist_Schluss()
    {
        // Arrange
        var sessions = new SessionStore(_clock);
        var token = sessions.Open(Client("handy", AgentScopes.Screen));

        // Act
        _clock.Advance(SessionStore.Lifetime);

        // Assert
        Assert.Null(sessions.Find(token));
    }

    [Fact]
    public void Ein_Widerruf_schliesst_alle_Sitzungen_dieses_Clients()
    {
        // Arrange
        var sessions = new SessionStore(_clock);
        var phone = sessions.Open(Client("handy", AgentScopes.Screen));
        var second = sessions.Open(Client("handy", AgentScopes.Screen));
        var laptop = sessions.Open(Client("laptop", AgentScopes.Screen));

        // Act
        sessions.CloseAll("handy");

        // Assert — das verlorene Handy ist sofort draußen, der Laptop merkt nichts.
        Assert.Null(sessions.Find(phone));
        Assert.Null(sessions.Find(second));
        Assert.NotNull(sessions.Find(laptop));
    }

    private static PairedClient Client(string id, params string[] scopes) => new(
        id, id, "unwichtig", scopes, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
