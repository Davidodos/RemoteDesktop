using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Steckbrief ist das, was von der Gegenkopplung übrig bleibt, wenn man ihr
/// die Frist nimmt. Er kommt ungeprüft aus einem fremden Rumpf — was hier
/// durchgeht, steht danach in einer Geräteliste.
/// </summary>
public class DeviceProfileTests
{
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string AgentFingerprint = new('b', 16);

    [Fact]
    public void Ein_vollstaendiger_Steckbrief_kommt_durch()
    {
        var profile = DeviceProfile.Sanitize(
            " 192.168.178.33 ", 8443, " Arbeitsrechner ", Fingerprint.ToUpperInvariant(),
            AgentFingerprint, null);

        Assert.NotNull(profile);
        Assert.Equal("192.168.178.33", profile.Host);
        Assert.Equal(8443, profile.Port);
        Assert.Equal("Arbeitsrechner", profile.Name);

        // Kleingeschrieben, damit zwei Schreibweisen desselben Werts nicht als
        // zwei verschiedene Stellen durchgehen.
        Assert.Equal(Fingerprint, profile.CaFingerprint);
        Assert.Equal(AgentFingerprint, profile.AgentFingerprint);
    }

    [Fact]
    public void Ohne_Adresse_beschreibt_er_nichts()
    {
        Assert.Null(DeviceProfile.Sanitize("  ", 8443, "PC", null, null, null));
        Assert.Null(DeviceProfile.Sanitize(new string('x', 256), 8443, "PC", null, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(70000)]
    public void Ein_unmoeglicher_Port_wird_verworfen(int? port)
    {
        Assert.Null(DeviceProfile.Sanitize("pc.example", port, "PC", null, null, null));
    }

    [Fact]
    public void Ohne_Namen_steht_die_Adresse_da()
    {
        // Ein leerer Eintrag in der Geräteliste wäre schlimmer als ein
        // technischer: er ließe sich später niemandem zuordnen.
        var profile = DeviceProfile.Sanitize("pc.example", 8443, "   ", null, null, null);

        Assert.Equal("pc.example", profile!.Name);

        var lang = DeviceProfile.Sanitize("pc.example", 8443, new string('n', 65), null, null, null);

        Assert.Equal("pc.example", lang!.Name);
    }

    [Fact]
    public void Halbe_Fingerabdruecke_werden_weggelassen_statt_uebernommen()
    {
        var profile = DeviceProfile.Sanitize(
            "pc.example", 8443, "PC", "kurz", "auch-kurz", null);

        Assert.NotNull(profile);
        Assert.Null(profile.CaFingerprint);
        Assert.Null(profile.AgentFingerprint);
    }

    [Fact]
    public void Ein_Schluessel_der_keiner_ist_kommt_nicht_in_die_Liste()
    {
        var profile = DeviceProfile.Sanitize(
            "pc.example", 8443, "PC", null, null, "kein Schlüssel");

        Assert.NotNull(profile);
        Assert.Null(profile.ClientKey);
    }
}
