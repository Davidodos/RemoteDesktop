using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Das Schlüsselpaar des Rechners und die Prüfung fremder Unterschriften.
/// </summary>
public class AgentIdentityTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"agentkey-{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Beim_ersten_Start_entsteht_ein_Schluesselpaar()
    {
        // Act
        var identity = AgentIdentity.LoadOrCreate(_path);

        // Assert
        Assert.True(File.Exists(_path));
        Assert.NotEmpty(identity.PublicKey);
        Assert.Equal(16, identity.Fingerprint.Length);
    }

    [Fact]
    public void Beim_zweiten_Start_bleibt_es_dasselbe()
    {
        // Arrange
        var first = AgentIdentity.LoadOrCreate(_path);

        // Act
        var second = AgentIdentity.LoadOrCreate(_path);

        // Assert — sonst hielte der Client den Rechner nach jedem Neustart für
        // einen anderen.
        Assert.Equal(first.PublicKey, second.PublicKey);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Zwei_Rechner_haben_verschiedene_Fingerabdruecke()
    {
        // Act
        var one = AgentIdentity.CreateTransient();
        var other = AgentIdentity.CreateTransient();

        // Assert
        Assert.NotEqual(one.Fingerprint, other.Fingerprint);
    }

    [Fact]
    public void Eine_echte_Unterschrift_besteht_die_Pruefung()
    {
        // Arrange
        using var client = new TestClient();
        var data = Convert.ToBase64String("Challenge"u8.ToArray());

        // Act
        var valid = AgentIdentity.VerifyClientSignature(
            client.PublicKey, Convert.FromBase64String(data), client.Sign(data));

        // Assert
        Assert.True(valid);
    }

    [Fact]
    public void Unfug_statt_Unterschrift_besteht_sie_nicht()
    {
        // Arrange
        using var client = new TestClient();

        // Assert — kaputte Eingaben sind kein Sonderfall, sondern „nicht
        // bestanden". Eine Ausnahme hier wäre ein 500 statt eines 401.
        Assert.False(AgentIdentity.VerifyClientSignature(client.PublicKey, [1, 2, 3], "kein Base64!"));
        Assert.False(AgentIdentity.VerifyClientSignature("kein Schlüssel", [1, 2, 3], "AAAA"));
    }
}
