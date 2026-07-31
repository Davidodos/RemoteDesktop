using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Die <c>clients.json</c> neben der <c>appsettings.json</c>: welche Clients
/// dieser Rechner kennt und was sie dürfen.
///
/// Der Agent vertraut ausschließlich dieser Datei. Auch wenn später ein
/// Kontodienst behauptet, ein Gerät gehöre dazu — steht es hier nicht drin,
/// kommt es nicht herein.
/// </summary>
public sealed class ClientStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

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

    private void Write()
    {
        var json = JsonSerializer.Serialize(_clients, JsonOptions);

        // Erst daneben schreiben, dann umbenennen: ein Absturz mitten im
        // Schreiben würde sonst die Liste aller zugelassenen Geräte zerstören —
        // und damit den Fernzugang zu dem Rechner, an dem man gerade nicht sitzt.
        var temporary = _path + ".tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>
    /// Eine fehlende Datei ist der Normalfall beim ersten Start. Eine kaputte
    /// Datei ist es nicht — deshalb fliegt die Ausnahme, statt still mit einer
    /// leeren Liste weiterzulaufen und alle gekoppelten Geräte auszusperren.
    /// </summary>
    private static List<PairedClient> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var content = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<PairedClient>>(content, JsonOptions)
               ?? throw new InvalidOperationException($"{path} enthält keine Client-Liste.");
    }
}
