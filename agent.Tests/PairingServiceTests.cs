using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Kopplung und Anmeldung im Zusammenspiel.
///
/// Das ist die Stelle, an der ein Fehler die Kontrolle über den Rechner
/// verschenkt: wer hier hereinkommt, darf Tastatur, Maus und Bildschirm.
/// </summary>
public class PairingServiceTests : IDisposable
{
    private readonly TestClock _clock = new();
    private readonly TestClient _client = new();
    private readonly string _clientsPath = Path.Combine(
        Path.GetTempPath(), $"clients-{Guid.NewGuid():N}.json");

    private readonly PairingCodes _codes;
    private readonly ClientStore _store;
    private readonly SessionStore _sessions;
    private readonly PairingService _pairing;

    public PairingServiceTests()
    {
        _codes = new PairingCodes(_clock);
        _store = new ClientStore(_clientsPath);
        _sessions = new SessionStore(_clock);
        _pairing = new PairingService(_store, _codes, new ChallengeStore(_clock), _sessions, _clock);
    }

    public void Dispose()
    {
        _client.Dispose();
        File.Delete(_clientsPath);
        GC.SuppressFinalize(this);
    }

    // ---- Kopplung ---------------------------------------------------------

    [Fact]
    public void Mit_richtigem_Code_wird_gekoppelt()
    {
        // Act
        var result = Pair();

        // Assert
        Assert.Equal(PairOutcome.Ok, result.Outcome);
        Assert.Equal("Handy", result.Client!.Label);
        Assert.Equal(AgentScopes.All, result.Client.Scopes);
    }

    [Fact]
    public void Ein_falscher_Code_koppelt_nicht()
    {
        // Arrange
        _codes.Issue();

        // Act
        var result = _pairing.Pair("000000-falsch", "Handy", _client.PublicKey, null);

        // Assert
        Assert.Equal(PairOutcome.BadCode, result.Outcome);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Ein_unbrauchbarer_Schluessel_wird_abgelehnt()
    {
        // Arrange
        var code = _codes.Issue();

        // Act
        var result = _pairing.Pair(code, "Handy", "kein Schlüssel", null);

        // Assert
        Assert.Equal(PairOutcome.BadPublicKey, result.Outcome);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Ein_erfundenes_Recht_wird_abgelehnt()
    {
        // Arrange
        var code = _codes.Issue();

        // Act
        var result = _pairing.Pair(code, "Handy", _client.PublicKey, ["screen", "alles"]);

        // Assert — sonst stünde in der clients.json ein Recht, das nie jemand
        // prüft, und der Eintrag sähe mächtiger aus, als er ist.
        Assert.Equal(PairOutcome.BadScope, result.Outcome);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Ohne_Namen_wird_nicht_gekoppelt()
    {
        // Arrange
        var code = _codes.Issue();

        // Act
        var result = _pairing.Pair(code, "   ", _client.PublicKey, null);

        // Assert
        Assert.Equal(PairOutcome.BadLabel, result.Outcome);
    }

    [Fact]
    public void Ein_gescheiterter_Versuch_verbraucht_den_Code_trotzdem()
    {
        // Arrange
        var code = _codes.Issue();

        // Act — erst mit unbrauchbarem Schlüssel, dann mit gutem.
        _pairing.Pair(code, "Handy", "Unfug", null);
        var second = _pairing.Pair(code, "Handy", _client.PublicKey, null);

        // Assert — wer den Code errät, soll nicht durch einen kaputten Schlüssel
        // einen zweiten Versuch geschenkt bekommen.
        Assert.Equal(PairOutcome.BadCode, second.Outcome);
    }

    [Fact]
    public void Dasselbe_Geraet_erneut_gekoppelt_ersetzt_seinen_Eintrag()
    {
        // Arrange
        Pair();

        // Act
        var again = Pair(scopes: ["screen"]);

        // Assert — die Kennung kommt aus dem Schlüssel, deshalb bleibt es ein
        // Eintrag statt zweier Karteileichen.
        Assert.Equal(PairOutcome.Ok, again.Outcome);
        Assert.Single(_store.List());
        Assert.Equal(["screen"], _store.List()[0].Scopes);
    }

    // ---- Anmeldung --------------------------------------------------------

    [Fact]
    public void Eine_richtige_Unterschrift_oeffnet_eine_Sitzung()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var nonce = _pairing.Challenge(id)!;

        // Act
        var result = _pairing.OpenSession(id, nonce, _client.Sign(nonce));

        // Assert
        Assert.Equal(SessionOutcome.Ok, result.Outcome);
        Assert.NotNull(_sessions.Find(result.Token!));
    }

    [Fact]
    public void Eine_manipulierte_Challenge_faellt_durch()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var nonce = _pairing.Challenge(id)!;
        var signature = _client.Sign(nonce);

        // Act — dieselbe Unterschrift, aber eine andere Challenge.
        var tampered = _pairing.Challenge(id)!;
        var result = _pairing.OpenSession(id, tampered, signature);

        // Assert — ohne diese Prüfung genügte eine einmal mitgeschnittene
        // Unterschrift, um sich beliebig oft anzumelden.
        Assert.Equal(SessionOutcome.BadSignature, result.Outcome);
    }

    [Fact]
    public void Die_Unterschrift_eines_fremden_Schluessels_faellt_durch()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var nonce = _pairing.Challenge(id)!;

        using var stranger = new TestClient();

        // Act
        var result = _pairing.OpenSession(id, nonce, stranger.Sign(nonce));

        // Assert
        Assert.Equal(SessionOutcome.BadSignature, result.Outcome);
    }

    [Fact]
    public void Eine_Challenge_gilt_nur_einmal()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var nonce = _pairing.Challenge(id)!;
        var signature = _client.Sign(nonce);

        // Act
        var first = _pairing.OpenSession(id, nonce, signature);
        var second = _pairing.OpenSession(id, nonce, signature);

        // Assert
        Assert.Equal(SessionOutcome.Ok, first.Outcome);
        Assert.Equal(SessionOutcome.BadChallenge, second.Outcome);
    }

    [Fact]
    public void Eine_abgelaufene_Challenge_faellt_durch()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var nonce = _pairing.Challenge(id)!;
        var signature = _client.Sign(nonce);

        // Act
        _clock.Advance(ChallengeStore.Lifetime);

        // Assert
        Assert.Equal(SessionOutcome.BadChallenge, _pairing.OpenSession(id, nonce, signature).Outcome);
    }

    [Fact]
    public void Ein_unbekannter_Client_bekommt_keine_Challenge()
    {
        // Act & Assert
        Assert.Null(_pairing.Challenge("gibtesnicht"));
        Assert.Equal(
            SessionOutcome.UnknownClient,
            _pairing.OpenSession("gibtesnicht", "AAAA", "AAAA").Outcome);
    }

    [Fact]
    public void Die_Anmeldung_haelt_fest_wann_der_Client_zuletzt_da_war()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var before = _store.Find(id)!.LastSeenAt;

        // Act
        _clock.Advance(TimeSpan.FromHours(3));
        var nonce = _pairing.Challenge(id)!;
        _pairing.OpenSession(id, nonce, _client.Sign(nonce));

        // Assert
        Assert.True(_store.Find(id)!.LastSeenAt > before);
    }

    // ---- Widerruf ---------------------------------------------------------

    [Fact]
    public void Ein_widerrufener_Client_kommt_nicht_mehr_herein()
    {
        // Arrange
        var id = Pair().Client!.Id;
        var nonce = _pairing.Challenge(id)!;
        var token = _pairing.OpenSession(id, nonce, _client.Sign(nonce)).Token!;

        // Act
        var revoked = _pairing.Revoke(id);

        // Assert — Eintrag weg und die laufende Sitzung sofort mit. Sonst
        // liefe das verlorene Handy noch zwölf Stunden weiter.
        Assert.True(revoked);
        Assert.Empty(_store.List());
        Assert.Null(_sessions.Find(token));
        Assert.Null(_pairing.Challenge(id));
    }

    [Fact]
    public void Ein_unbekannter_Client_laesst_sich_nicht_widerrufen()
    {
        // Act & Assert
        Assert.False(_pairing.Revoke("gibtesnicht"));
    }

    private PairResult Pair(IReadOnlyList<string>? scopes = null) =>
        _pairing.Pair(_codes.Issue(), "Handy", _client.PublicKey, scopes);
}
