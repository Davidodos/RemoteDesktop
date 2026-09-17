using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die Zugangsprüfung. Sie entscheidet bei jedem einzelnen Aufruf, und ein
/// Loch hier ist ein Loch überall — deshalb wird auch geprüft, was sie
/// <em>nicht</em> durchlässt.
/// </summary>
public class ClientAuthTests
{
    private readonly TestClock _clock = new();
    private readonly SessionStore _sessions;
    private readonly ClientAuth _auth;

    public ClientAuthTests()
    {
        _sessions = new SessionStore(_clock);
        _auth = new ClientAuth(_sessions);
    }

    [Fact]
    public void Ohne_Berechtigung_kommt_niemand_durch()
    {
        Assert.Equal(AuthOutcome.NoCredential, _auth.Authorize(null, "/api/info").Outcome);
        Assert.Equal(AuthOutcome.NoCredential, _auth.Authorize(string.Empty, "/api/info").Outcome);
    }

    [Fact]
    public void Ein_erfundenes_Token_kommt_nicht_durch()
    {
        // Assert — es gibt kein geteiltes Geheimnis mehr, das für alles gilt;
        // nur eine offene Sitzung zählt.
        Assert.Equal(
            AuthOutcome.UnknownCredential,
            _auth.Authorize("einaltestokenmitmindestens32zeichen", "/api/info").Outcome);
    }

    [Fact]
    public void Ein_Sitzungstoken_oeffnet_nur_die_erlaubten_Pfade()
    {
        // Arrange — dieser Client darf das Bild sehen, aber nichts abschalten.
        var token = _sessions.Open(ClientWith(AgentScopes.Screen));

        // Act
        var screen = _auth.Authorize(token, "/ws/screen");
        var power = _auth.Authorize(token, "/api/power");

        // Assert
        Assert.True(screen.IsAllowed);
        Assert.Equal(AuthOutcome.MissingScope, power.Outcome);
        Assert.Equal(AgentScopes.Power, power.RequiredScope);
    }

    [Fact]
    public void Auskunft_ueber_den_Rechner_braucht_kein_besonderes_Recht()
    {
        // Arrange — ohne /api/info käme die App nicht einmal zu ihrer Oberfläche.
        var token = _sessions.Open(ClientWith(AgentScopes.Screen));

        // Assert
        Assert.True(_auth.Authorize(token, "/api/info").IsAllowed);
    }

    [Fact]
    public void Eine_abgelaufene_Sitzung_zaehlt_nicht_mehr()
    {
        // Arrange
        var token = _sessions.Open(ClientWith(AgentScopes.Screen));

        // Act
        _clock.Advance(SessionStore.Lifetime);

        // Assert
        Assert.Equal(AuthOutcome.UnknownCredential, _auth.Authorize(token, "/ws/screen").Outcome);
    }

    [Fact]
    public void Ein_unbekannter_Pfad_wird_abgelehnt_statt_durchgelassen()
    {
        // Arrange
        var token = _sessions.Open(ClientWith(AgentScopes.All.ToArray()));

        // Assert — ein neuer Endpoint, bei dem jemand die Zuordnung vergisst,
        // fällt so beim ersten Aufruf auf, statt offen dazustehen.
        Assert.Equal(AuthOutcome.UnknownPath, _auth.Authorize(token, "/api/neu").Outcome);
    }

    /// <summary>Ein Client-Eintrag mit genau den angegebenen Rechten.</summary>
    private static PairedClient ClientWith(params string[] scopes) => new(
        "abc123",
        "Handy",
        "unwichtig",
        scopes,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);
}
