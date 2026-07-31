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
    private const string LegacyToken = "einaltestokenmitmindestens32zeichen";

    private readonly TestClock _clock = new();
    private readonly SessionStore _sessions;

    public ClientAuthTests()
    {
        _sessions = new SessionStore(_clock);
    }

    [Fact]
    public void Ohne_Berechtigung_kommt_niemand_durch()
    {
        // Arrange
        var auth = new ClientAuth(_sessions, LegacyToken);

        // Assert
        Assert.Equal(AuthOutcome.NoCredential, auth.Authorize(null, "/api/info").Outcome);
        Assert.Equal(AuthOutcome.NoCredential, auth.Authorize(string.Empty, "/api/info").Outcome);
    }

    [Fact]
    public void Das_alte_Token_gilt_weiter_und_darf_alles()
    {
        // Arrange — bis Phase 12 muss der alte Weg offenbleiben, sonst sperrt
        // man sich vom eigenen PC aus.
        var auth = new ClientAuth(_sessions, LegacyToken);

        // Assert
        Assert.True(auth.Authorize(LegacyToken, "/api/power").IsAllowed);
        Assert.True(auth.Authorize(LegacyToken, "/ws/input").IsAllowed);
    }

    [Fact]
    public void Ein_falsches_Token_kommt_nicht_durch()
    {
        // Arrange
        var auth = new ClientAuth(_sessions, LegacyToken);

        // Assert
        Assert.Equal(
            AuthOutcome.UnknownCredential,
            auth.Authorize(LegacyToken + "x", "/api/info").Outcome);
    }

    [Fact]
    public void Ein_zu_kurzes_Token_laesst_der_Agent_gar_nicht_erst_zu()
    {
        // Assert — der Agent hat volle Kontrolle über den PC.
        Assert.Throws<ArgumentException>(() => new ClientAuth(_sessions, "zukurz"));
    }

    [Fact]
    public void Ohne_altes_Token_zaehlen_nur_noch_Sitzungen()
    {
        // Arrange
        var auth = new ClientAuth(_sessions, null);

        // Assert
        Assert.False(auth.HasLegacyToken);
        Assert.Equal(AuthOutcome.UnknownCredential, auth.Authorize(LegacyToken, "/api/info").Outcome);
    }

    [Fact]
    public void Ein_Sitzungstoken_oeffnet_nur_die_erlaubten_Pfade()
    {
        // Arrange — dieser Client darf das Bild sehen, aber nichts abschalten.
        var auth = new ClientAuth(_sessions, null);
        var token = _sessions.Open(ClientWith(AgentScopes.Screen));

        // Act
        var screen = auth.Authorize(token, "/ws/screen");
        var power = auth.Authorize(token, "/api/power");

        // Assert
        Assert.True(screen.IsAllowed);
        Assert.Equal(AuthOutcome.MissingScope, power.Outcome);
        Assert.Equal(AgentScopes.Power, power.RequiredScope);
    }

    [Fact]
    public void Auskunft_ueber_den_Rechner_braucht_kein_besonderes_Recht()
    {
        // Arrange — ohne /api/info käme die App nicht einmal zu ihrer Oberfläche.
        var auth = new ClientAuth(_sessions, null);
        var token = _sessions.Open(ClientWith(AgentScopes.Screen));

        // Assert
        Assert.True(auth.Authorize(token, "/api/info").IsAllowed);
    }

    [Fact]
    public void Eine_abgelaufene_Sitzung_zaehlt_nicht_mehr()
    {
        // Arrange
        var auth = new ClientAuth(_sessions, null);
        var token = _sessions.Open(ClientWith(AgentScopes.Screen));

        // Act
        _clock.Advance(SessionStore.Lifetime);

        // Assert
        Assert.Equal(AuthOutcome.UnknownCredential, auth.Authorize(token, "/ws/screen").Outcome);
    }

    [Fact]
    public void Ein_unbekannter_Pfad_wird_abgelehnt_statt_durchgelassen()
    {
        // Arrange
        var auth = new ClientAuth(_sessions, LegacyToken);

        // Assert — ein neuer Endpoint, bei dem jemand die Zuordnung vergisst,
        // fällt so beim ersten Aufruf auf, statt offen dazustehen.
        Assert.Equal(AuthOutcome.UnknownPath, auth.Authorize(LegacyToken, "/api/neu").Outcome);
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
