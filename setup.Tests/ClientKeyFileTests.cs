using System.Security.Cryptography;
using System.Text.Json;
using RemoteDesktopSetup;
using Xunit;

namespace RemoteDesktopSetup.Tests;

/// <summary>
/// Der Ausweis dieses Rechners als Client. Eine Datei, zwei Leser: der Agent
/// schickt den öffentlichen Teil beim Koppeln mit, das Fenster meldet sich
/// damit bei fremden Geräten an.
/// </summary>
public class ClientKeyFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"clientkey-{Guid.NewGuid():N}");

    private string Path_ => ClientKeyFile.In(_folder);

    [Fact]
    public void Ohne_Datei_gibt_es_nichts_zu_lesen()
    {
        Assert.Null(ClientKeyFile.Read(Path_));
    }

    [Fact]
    public void Angelegt_und_beim_zweiten_Mal_derselbe()
    {
        var first = ClientKeyFile.LoadOrCreate(Path_);

        // Ein zweiter Schlüssel wäre schlimmer als keiner: jede bestehende
        // Kopplung zeigte auf den ersten.
        Assert.Equal(first, ClientKeyFile.LoadOrCreate(Path_));
        Assert.Equal(first, ClientKeyFile.Read(Path_));
    }

    [Fact]
    public void Beide_Haelften_sind_ein_P256_Schluesselpaar()
    {
        var key = ClientKeyFile.LoadOrCreate(Path_);

        using var loaded = ECDsa.Create();

        loaded.ImportPkcs8PrivateKey(Convert.FromBase64String(key.PrivateKey), out _);

        // Der öffentliche Teil muss zum privaten passen — sonst meldet sich das
        // Fenster mit einer Unterschrift an, die zu einem anderen Ausweis gehört.
        Assert.Equal(
            key.PublicKey, Convert.ToBase64String(loaded.ExportSubjectPublicKeyInfo()));

        Assert.Equal(
            ECCurve.NamedCurves.nistP256.Oid.Value,
            loaded.ExportParameters(false).Curve.Oid.Value);
    }

    [Fact]
    public void Eine_halbe_Datei_zaehlt_als_keine()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, """{ "publicKey": "AAA=" }""");

        // Mit einem öffentlichen Schlüssel ohne privaten liefe jede Kopplung
        // durch und jede Anmeldung danach ins Leere.
        Assert.Null(ClientKeyFile.Read(Path_));

        Assert.NotNull(ClientKeyFile.LoadOrCreate(Path_).PrivateKey);
    }

    [Fact]
    public void Unlesbares_haelt_niemanden_auf()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "das ist kein JSON");

        Assert.Null(ClientKeyFile.Read(Path_));
    }

    [Fact]
    public void Die_Feldnamen_stehen_fest()
    {
        ClientKeyFile.LoadOrCreate(Path_);

        // Die Datei wird von zwei Programmen gelesen. Wer die Namen ändert,
        // ändert sie an einer Stelle und bricht die andere.
        using var document = JsonDocument.Parse(File.ReadAllText(Path_));

        Assert.True(document.RootElement.TryGetProperty("publicKey", out _));
        Assert.True(document.RootElement.TryGetProperty("privateKey", out _));
    }

    [Fact]
    public void Die_Kennung_kommt_aus_dem_Schluessel_selbst()
    {
        var key = ClientKeyFile.LoadOrCreate(Path_);

        var fingerprint = ClientKeyFile.Fingerprint(key.PublicKey);

        Assert.Equal(16, fingerprint.Length);
        Assert.All(fingerprint, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal(fingerprint, fingerprint.ToLowerInvariant());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
