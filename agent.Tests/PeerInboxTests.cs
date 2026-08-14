using RemoteDesktopAgent.Auth;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Eingang ersetzt die Angebote mit Frist. Der Unterschied, auf den es
/// ankommt: was hier liegt, überlebt einen Neustart — und ist beim Abholen weg.
/// </summary>
public class PeerInboxTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"peers-{Guid.NewGuid():N}");

    private string Path_ => System.IO.Path.Combine(_folder, "peers.json");

    private static DeviceProfile Peer(string name, string? fingerprint = null) =>
        new("192.168.178.33", 8443, name, null, fingerprint, null);

    [Fact]
    public void Ein_leerer_Eingang_ist_der_Normalfall()
    {
        Assert.Empty(new PeerInbox(Path_).TakeAll());
    }

    [Fact]
    public void Abgeholt_wird_einmal()
    {
        var inbox = new PeerInbox(Path_);

        inbox.Add(Peer("Handy"));

        Assert.Single(inbox.TakeAll());

        // Sonst käme ein Gerät, das jemand aus seiner Liste entfernt hat, beim
        // nächsten Nachsehen von allein zurück.
        Assert.Empty(inbox.TakeAll());
    }

    [Fact]
    public void Was_hier_liegt_ueberlebt_einen_Neustart()
    {
        new PeerInbox(Path_).Add(Peer("Handy"));

        // Das ist der ganze Grund für die Datei: der Vorgänger hielt einen
        // Kopplungscode im Arbeitsspeicher, und wer den Rechner erst am nächsten
        // Tag anfasste, fand nichts mehr vor.
        var peers = new PeerInbox(Path_).TakeAll();

        Assert.Equal("Handy", Assert.Single(peers).Name);
    }

    [Fact]
    public void Dasselbe_Geraet_zweimal_bleibt_ein_Eintrag()
    {
        var inbox = new PeerInbox(Path_);

        inbox.Add(Peer("Handy", new string('b', 16)));
        inbox.Add(Peer("Davids Handy", new string('b', 16)));

        var peers = inbox.TakeAll();

        Assert.Equal("Davids Handy", Assert.Single(peers).Name);
    }

    [Fact]
    public void Eine_kaputte_Datei_kostet_den_Start_nicht()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "{kaputt");

        // Anders als bei den gekoppelten Clients: was hier steht, ist eine
        // Bequemlichkeit. Dafür soll kein Agent das Starten verweigern.
        Assert.Empty(new PeerInbox(Path_).TakeAll());
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
