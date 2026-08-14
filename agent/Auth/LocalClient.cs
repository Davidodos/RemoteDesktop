using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Der Ausweis der Oberfläche dieses Rechners — der öffentliche Schlüssel, mit
/// dem sich das Fenster nebenan bei fremden Geräten anmeldet.
///
/// <para>
/// **Wozu der Agent ihn kennt:** eine Kopplung geht immer in beide Richtungen.
/// Wer sich hier koppelt, soll ohne einen zweiten Aufruf auch von hier aus
/// erreichbar werden — dafür braucht er den Schlüssel dieser Oberfläche, und
/// bekommt ihn in der Antwort auf <c>/api/pair</c>. Der Agent hat ihn nicht
/// selbst: er gehört dem Fenster, das ihn beim Start hier hinterlegt.
/// </para>
///
/// <para>
/// Es ist ein öffentlicher Schlüssel. Er verrät nichts und erlaubt nichts —
/// Macht bekommt er erst dadurch, dass ihn die Gegenseite in ihre eigene
/// <c>clients.json</c> aufnimmt, und das tut sie nur nach einer bestandenen
/// Kopplung.
/// </para>
/// </summary>
public sealed class LocalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly string _path;
    private readonly object _gate = new();
    private string? _publicKey;

    public LocalClient(string path)
    {
        _path = path;
        _publicKey = Read(path);
    }

    /// <summary><c>null</c>, solange das Fenster hier noch nie gelaufen ist.</summary>
    public string? PublicKey
    {
        get
        {
            lock (_gate)
            {
                return _publicKey;
            }
        }
    }

    /// <returns><c>false</c>, wenn der Schlüssel keiner ist.</returns>
    public bool Remember(string? publicKey)
    {
        if (!PairingService.IsUsablePublicKey(publicKey ?? string.Empty))
        {
            return false;
        }

        lock (_gate)
        {
            if (_publicKey == publicKey)
            {
                return true;
            }

            _publicKey = publicKey;

            var temporary = _path + ".tmp";

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllText(
                temporary, JsonSerializer.Serialize(new Stored(publicKey!), JsonOptions));
            File.Move(temporary, _path, overwrite: true);

            return true;
        }
    }

    private static string? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), JsonOptions)
                ?.PublicKey;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Ohne den Schlüssel bleibt die Kopplung einseitig, und das Fenster
            // legt ihn beim nächsten Start ohnehin neu hin. Kein Grund, den
            // Agent am Starten zu hindern.
            return null;
        }
    }

    private sealed record Stored(string PublicKey);
}
