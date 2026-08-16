using RemoteDesktopSetup;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Die <c>clients.json</c> neben der <c>appsettings.json</c>: welche Clients
/// dieser Rechner kennt und was sie dürfen.
///
/// Der Agent vertraut ausschließlich dieser Datei. Auch wenn später ein
/// Kontodienst behauptet, ein Gerät gehöre dazu — steht es hier nicht drin,
/// kommt es nicht herein.
///
/// <para>
/// Gelesen und geschrieben wird über <see cref="ClientsFile"/> — dieselbe
/// Klasse, die das Fenster bei gestopptem Agent benutzt. Der Unterschied liegt
/// hier: dieser Store hält die Liste im Speicher, weil jeder Aufruf gegen sie
/// geprüft wird.
/// </para>
/// </summary>
public sealed class ClientStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private List<PairedClient> _clients;

    public ClientStore(string path)
    {
        _path = path;
        _clients = Read(path);
    }

    public IReadOnlyList<PairedClient> List()
    {
        lock (_gate)
        {
            return [.. _clients];
        }
    }

    public PairedClient? Find(string id)
    {
        lock (_gate)
        {
            return _clients.FirstOrDefault(client => client.Id == id);
        }
    }

    public void Add(PairedClient client)
    {
        lock (_gate)
        {
            _clients = [.. _clients.Where(existing => existing.Id != client.Id), client];
            Write();
        }
    }

    /// <returns><c>false</c>, wenn es den Client gar nicht gab.</returns>
    public bool Revoke(string id)
    {
        lock (_gate)
        {
            var remaining = _clients.Where(client => client.Id != id).ToList();

            if (remaining.Count == _clients.Count)
            {
                return false;
            }

            _clients = remaining;
            Write();
            return true;
        }
    }

    /// <summary>
    /// Hält fest, wann der Client zuletzt eine Sitzung geöffnet hat. Das ist die
    /// einzige Grundlage, auf der man später entscheiden kann, welcher Eintrag
    /// ein vergessenes altes Gerät ist.
    /// </summary>
    public void Touch(string id, DateTimeOffset seenAt)
    {
        lock (_gate)
        {
            var client = _clients.FirstOrDefault(entry => entry.Id == id);

            if (client is null)
            {
                return;
            }

            _clients = [.. _clients.Select(entry =>
                entry.Id == id ? entry with { LastSeenAt = seenAt } : entry)];

            Write();
        }
    }

    private void Write() => ClientsFile.Write(_path, _clients);

    /// <summary>
    /// Eine fehlende Datei ist der Normalfall beim ersten Start. Eine kaputte
    /// Datei ist es nicht — deshalb fliegt die Ausnahme, statt still mit einer
    /// leeren Liste weiterzulaufen und alle gekoppelten Geräte auszusperren.
    /// </summary>
    private static List<PairedClient> Read(string path) => ClientsFile.Read(path);
}
