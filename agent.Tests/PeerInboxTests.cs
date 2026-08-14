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
        Assert.Empty(new PeerInbox(Path_).List());
    }

    [Fact]
    public void Lesen_leert_den_Eingang_nicht()
    {
        var inbox = new PeerInbox(Path_);

        inbox.Add(Peer("Handy"));

        // Der Kern der Sache: geht nach dem Lesen etwas schief, ist der
        // Steckbrief sonst endgültig weg — und am Bildschirm steht „noch kein
        // Gerät gekoppelt" ohne zweiten Versuch.
        Assert.Single(inbox.List());
        Assert.Single(inbox.List());
    }

    [Fact]
    public void Vergessen_wird_auf_Zuruf()
    {
        var inbox = new PeerInbox(Path_);

        inbox.Add(Peer("Handy"));

        var id = PeerInbox.Key(inbox.List().Single());

        inbox.Forget([id]);

        // Sonst käme ein Gerät, das jemand aus seiner Liste entfernt hat, beim
        // nächsten Nachsehen von allein zurück.
        Assert.Empty(inbox.List());
    }

    [Fact]
    public void Eine_unbekannte_Kennung_raeumt_nichts_weg()
    {
        var inbox = new PeerInbox(Path_);

        inbox.Add(Peer("Handy"));
        inbox.Forget(["gibt-es-nicht"]);

        Assert.Single(inbox.List());
    }

    [Fact]
    public void Was_hier_liegt_ueberlebt_einen_Neustart()
    {
        new PeerInbox(Path_).Add(Peer("Handy"));

        // Das ist der ganze Grund für die Datei: der Vorgänger hielt einen
        // Kopplungscode im Arbeitsspeicher, und wer den Rechner erst am nächsten
        // Tag anfasste, fand nichts mehr vor.
        var peers = new PeerInbox(Path_).List();

        Assert.Equal("Handy", Assert.Single(peers).Name);
    }

    [Fact]
    public void Dasselbe_Geraet_zweimal_bleibt_ein_Eintrag()
    {
        var inbox = new PeerInbox(Path_);

        inbox.Add(Peer("Handy", new string('b', 16)));
        inbox.Add(Peer("Davids Handy", new string('b', 16)));

        var peers = inbox.List();

        Assert.Equal("Davids Handy", Assert.Single(peers).Name);
    }

    [Fact]
    public void Eine_kaputte_Datei_kostet_den_Start_nicht()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "{kaputt");

        // Anders als bei den gekoppelten Clients: was hier steht, ist eine
        // Bequemlichkeit. Dafür soll kein Agent das Starten verweigern.
        Assert.Empty(new PeerInbox(Path_).List());
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
