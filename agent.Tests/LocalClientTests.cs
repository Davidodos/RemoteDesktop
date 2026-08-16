using RemoteDesktopAgent.Auth;
using RemoteDesktopSetup;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Ausweis der eigenen Oberfläche. Ohne ihn bliebe jede Kopplung einseitig:
/// die Gegenseite bekäme in der Antwort nichts, was sie in ihre eigene Liste
/// eintragen könnte.
/// </summary>
public class LocalClientTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"localclient-{Guid.NewGuid():N}");

    private string KeyPath => ClientKeyFile.In(_folder);

    [Fact]
    public void Ohne_Datei_gibt_es_keinen()
    {
        Assert.Null(new LocalClient(KeyPath).PublicKey);
    }

    [Fact]
    public void Angelegt_und_nach_einem_Neustart_noch_da()
    {
        var local = new LocalClient(KeyPath);

        Assert.Null(local.Ensure());

        var first = local.PublicKey;

        Assert.NotNull(first);
        Assert.Equal(first, new LocalClient(KeyPath).PublicKey);
    }

    [Fact]
    public void Ein_zweiter_Aufruf_stellt_keinen_neuen_aus()
    {
        var local = new LocalClient(KeyPath);

        local.Ensure();

        var first = local.PublicKey;

        local.Ensure();

        // Ein zweiter Schlüssel wäre schlimmer als keiner: jede bestehende
        // Kopplung zeigte auf den ersten, und die Anmeldung liefe ab dann in
        // ein 401, das wie ein Fehler der Gegenstelle aussieht.
        Assert.Equal(first, local.PublicKey);
    }

    [Fact]
    public void Was_das_Fenster_anlegt_findet_der_Agent()
    {
        // Der ganze Zweck der Datei: beide lesen dieselbe. Hier legt sie der
        // eine an, und der andere kennt sie, ohne dass jemand etwas hinterlegt.
        var written = ClientKeyFile.LoadOrCreate(KeyPath);

        Assert.Equal(written.PublicKey, new LocalClient(KeyPath).PublicKey);
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
