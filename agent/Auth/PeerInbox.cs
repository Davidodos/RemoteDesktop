using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Die Steckbriefe, die beim Koppeln hier abgegeben wurden — der Posteingang für
/// die Oberfläche dieses Rechners.
///
/// <para>
/// **Warum sie liegen bleiben:** wer koppelt, ist die *Client*-Seite; sie hält
/// den privaten Geräteschlüssel und die Geräteliste. Beim Agent liegt beides
/// nicht. Er nimmt den Steckbrief also nur an und legt ihn hin, bis das Fenster
/// dieses Rechners danach sieht.
/// </para>
///
/// <para>
/// **Und warum auf Platte:** ein Steckbrief ist kein Geheimnis und hat keine
/// Frist. Er darf einen Neustart überleben — genau das war der Fehler des
/// Vorgängers, der einen Kopplungscode aufhob und ihn nach fünf Minuten wertlos
/// werden ließ. Wer den Rechner erst morgen wieder anfasst, findet das Gerät
/// morgen in seiner Liste.
/// </para>
///
/// <para>
/// Gelesen wird einmal: beim Abholen ist der Eingang leer. Sonst käme ein Gerät,
/// das jemand aus seiner Liste entfernt hat, beim nächsten Start von allein
/// zurück.
/// </para>
/// </summary>
public sealed class PeerInbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly string _path;
    private readonly object _gate = new();
    private List<DeviceProfile> _peers;

    public PeerInbox(string path)
    {
        _path = path;
        _peers = Read(path);
    }

    /// <summary>
    /// Legt einen Steckbrief hin. Ein zweiter desselben Geräts ersetzt den
    /// ersten: nach einer erneuten Kopplung gilt nur noch der neue.
    /// </summary>
    public void Add(DeviceProfile peer)
    {
        lock (_gate)
        {
            _peers = [.. _peers.Where(existing => Key(existing) != Key(peer)), peer];
            Write();
        }
    }

    /// <summary>Holt alles ab und leert dabei den Eingang.</summary>
    public IReadOnlyList<DeviceProfile> TakeAll()
    {
        lock (_gate)
        {
            var taken = _peers;

            if (taken.Count == 0)
            {
                return [];
            }

            _peers = [];
            Write();

            return taken;
        }
    }

    /// <summary>
    /// Woran zwei Einträge dasselbe Gerät sind. Der Fingerabdruck des Agents,
    /// solange es einen gibt — er überlebt einen Namens- und Adresswechsel.
    /// </summary>
    private static string Key(DeviceProfile peer) =>
        peer.AgentFingerprint ?? $"{peer.Host}:{peer.Port}";

    private void Write()
    {
        // Erst daneben schreiben, dann umbenennen — wie in der ClientStore. Ein
        // Absturz mitten im Schreiben soll den Eingang leeren können, aber nicht
        // eine halbe Datei hinterlassen, die beim nächsten Start alles kostet.
        var temporary = _path + ".tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        File.WriteAllText(temporary, JsonSerializer.Serialize(_peers, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>
    /// Eine fehlende Datei ist der Normalfall. Eine kaputte bleibt hier —
    /// anders als bei den gekoppelten Clients — folgenlos: was hier steht, ist
    /// eine Bequemlichkeit, und dafür soll kein Agent den Start verweigern.
    /// </summary>
    private static List<DeviceProfile> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var content = File.ReadAllText(path);

            return string.IsNullOrWhiteSpace(content)
                ? []
                : JsonSerializer.Deserialize<List<DeviceProfile>>(content, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }
}
