using System.Security.Cryptography;

namespace RemoteDesktopSetup;

/// <summary>
/// Das Geheimnis für die Endpunkte, die nur am Rechner selbst erreichbar sind:
/// Kopplungscode, Gegenrichtung, Clientliste.
///
/// <para>
/// **Der Befund dahinter (18.09.2026):** diese Endpunkte prüften nur, ob die
/// Verbindung von 127.0.0.1 kommt. Das kann jeder lokale Prozess — und der
/// bekam damit einen Kopplungscode oder trug sich gleich selbst ein. Jetzt
/// gehört ein Geheimnis dazu, das im Profil des Benutzers liegt: der Agent
/// legt es beim Start an, das Fenster liest es und schickt es mit. Ein
/// zweiter Benutzer desselben Rechners kommt nicht daran.
/// </para>
///
/// <para>
/// Wer zuerst kommt, legt es an — Agent oder Fenster —, beide lesen dieselbe
/// Datei. Genau wie beim Ausweis (<see cref="ClientKeyFile"/>).
/// </para>
/// </summary>
public static class LocalSecretFile
{
    public const string FileName = AgentPaths.LocalSecretFileName;

    private const int Bytes = 32;

    public static string In(string userDirectory) => Path.Combine(userDirectory, FileName);

    /// <summary>Das Geheimnis, oder <c>null</c>, wenn dort keins liegt.</summary>
    public static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();

            return value.Length == Bytes * 2 && value.All(Uri.IsHexDigit) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string LoadOrCreate(string path)
    {
        if (Read(path) is { } existing)
        {
            return existing;
        }

        var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(Bytes)).ToLowerInvariant();

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var temporary = path + ".tmp";

        File.WriteAllText(temporary, created);
        File.Move(temporary, path, overwrite: true);

        return created;
    }

    /// <summary>Vergleich in fester Zeit — ein früher Abbruch verriete die Stelle.</summary>
    public static bool Matches(string? presented, string? expected)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }
}
