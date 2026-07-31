using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Kopplungscode ist die einzige Hürde zwischen einem fremden Gerät und der
/// vollen Kontrolle über den Rechner. Sechs Ziffern halten dem nur stand, wenn
/// Ablauf, Einmalverwendung und Fehlversuchsgrenze wirklich greifen.
/// </summary>
public class PairingCodesTests
{
    [Fact]
    public void Der_Code_hat_sechs_Ziffern()
    {
        // Arrange
        var codes = new PairingCodes(new TestClock());

        // Act
        var code = codes.Issue();

        // Assert
        Assert.Equal(6, code.Length);
        Assert.All(code, character => Assert.True(char.IsAsciiDigit(character)));
    }

    [Fact]
    public void Nach_fuenf_Minuten_gilt_der_Code_nicht_mehr()
    {
        // Arrange
        var clock = new TestClock();
        var codes = new PairingCodes(clock);
        var code = codes.Issue();

        // Act
        clock.Advance(TimeSpan.FromMinutes(5));

        // Assert
        Assert.False(codes.TryRedeem(code));
        Assert.Null(codes.RemainingLifetime());
    }

    [Fact]
    public void Kurz_vor_Ablauf_gilt_er_noch()
    {
        // Arrange
        var clock = new TestClock();
        var codes = new PairingCodes(clock);
        var code = codes.Issue();

        // Act
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));

        // Assert
        Assert.True(codes.TryRedeem(code));
    }

    [Fact]
    public void Ein_Code_funktioniert_kein_zweites_Mal()
    {
        // Arrange
        var codes = new PairingCodes(new TestClock());
        var code = codes.Issue();

        // Act
        var first = codes.TryRedeem(code);
        var second = codes.TryRedeem(code);

        // Assert — sonst könnte ein mitgelesener Code beliebig oft Geräte
        // einschleusen.
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void Nach_fuenf_Fehlversuchen_ist_der_Code_verbrannt()
    {
        // Arrange
        var codes = new PairingCodes(new TestClock());
        var code = codes.Issue();

        // Act — fünfmal daneben, dann der richtige Code.
        for (var attempt = 0; attempt < PairingCodes.MaxAttempts; attempt++)
        {
            Assert.False(codes.TryRedeem(NextTo(code)));
        }

        // Assert — eine Million Möglichkeiten rät man sonst in fünf Minuten durch.
        Assert.False(codes.TryRedeem(code));
    }

    [Fact]
    public void Ein_neuer_Code_verdraengt_den_alten()
    {
        // Arrange
        var codes = new PairingCodes(new TestClock());
        var first = codes.Issue();

        // Act
        var second = codes.Issue();

        // Assert — angezeigt wird immer nur der neueste; der alte darf nicht
        // als zweite Tür offen bleiben.
        Assert.False(codes.TryRedeem(first));
        Assert.True(codes.TryRedeem(second));
    }

    [Fact]
    public void Ohne_ausgegebenen_Code_geht_gar_nichts()
    {
        // Arrange
        var codes = new PairingCodes(new TestClock());

        // Assert
        Assert.False(codes.TryRedeem("123456"));
        Assert.Null(codes.RemainingLifetime());
    }

    [Fact]
    public void Eine_falsche_Laenge_wird_abgelehnt()
    {
        // Arrange
        var codes = new PairingCodes(new TestClock());
        var code = codes.Issue();

        // Act & Assert
        Assert.False(codes.TryRedeem(code + "0"));
        Assert.False(codes.TryRedeem(code[..5]));
        Assert.True(codes.TryRedeem(code));
    }

    /// <summary>Ein garantiert anderer Code gleicher Länge.</summary>
    private static string NextTo(string code) =>
        ((int.Parse(code) + 1) % 1_000_000).ToString("D6");
}
