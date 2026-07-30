using System.Text;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Macht aus der Windows-Kennung einer App einen lesbaren Namen.
///
/// Windows nennt die Quelle einer Medien-Sitzung entweder nach ihrer
/// EXE-Datei (<c>Spotify.exe</c>) oder als Store-Kennung
/// (<c>Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic</c>). Beides
/// unverändert auf ein Handydisplay zu schreiben, wäre unbrauchbar.
///
/// Reine Textumformung, deshalb ohne Windows-Abhängigkeit und vollständig
/// testbar.
/// </summary>
public static class MediaAppName
{
    public static string Describe(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "Unbekannt";
        }

        var value = identifier.Trim();

        // Store-Apps: alles vor dem Ausrufezeichen ist das Paket, dahinter die
        // Anwendung darin. Der hintere Teil ist der aussagekräftigere.
        var bang = value.LastIndexOf('!');

        if (bang >= 0 && bang < value.Length - 1)
        {
            value = value[(bang + 1)..];
        }

        // Herausgeber-Präfix und Paket-Hash wegwerfen.
        var underscore = value.IndexOf('_');

        if (underscore > 0)
        {
            value = value[..underscore];
        }

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        var dot = value.LastIndexOf('.');

        if (dot >= 0 && dot < value.Length - 1)
        {
            value = value[(dot + 1)..];
        }

        // Bleibt nach dem Zerlegen nur Interpunktion übrig, war die Kennung
        // nicht brauchbar — das kommt bei Apps vor, die sich unsauber anmelden.
        return value.Any(char.IsLetterOrDigit)
            ? SplitCamelCase(Capitalize(value))
            : "Unbekannt";
    }

    private static string Capitalize(string value) =>
        char.IsLower(value[0]) ? char.ToUpperInvariant(value[0]) + value[1..] : value;

    /// <summary>
    /// <c>ZuneMusic</c> → <c>Zune Music</c>. Folgen von Großbuchstaben bleiben
    /// zusammen, damit aus <c>VLC</c> nicht <c>V L C</c> wird.
    /// </summary>
    private static string SplitCamelCase(string value)
    {
        var result = new StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            var needsSpace = i > 0
                             && char.IsUpper(value[i])
                             && (char.IsLower(value[i - 1])
                                 || (i + 1 < value.Length && char.IsLower(value[i + 1])));

            if (needsSpace)
            {
                result.Append(' ');
            }

            result.Append(value[i]);
        }

        return result.ToString();
    }
}
