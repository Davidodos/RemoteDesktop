namespace RemoteDesktopClient;

/// <summary>
/// Findet die gebaute React-App, die das Fenster anzeigen soll.
///
/// Zwei Fälle: installiert liegt sie als <c>app\</c> neben der .exe, beim
/// Entwickeln als <c>app\dist\</c> irgendwo über dem Ausgabeverzeichnis. Beide
/// zu kennen erspart es, für einen Testlauf jedes Mal zu installieren.
/// </summary>
public static class WebAppLocator
{
    private const string Entry = "index.html";

    /// <summary>Wie weit nach oben gesucht wird, bevor aufgegeben wird.</summary>
    private const int MaxLevels = 8;

    /// <returns>
    /// Das Verzeichnis mit der <c>index.html</c>, oder <c>null</c>, wenn keins
    /// zu finden war.
    /// </returns>
    public static string? Locate(string baseDirectory)
    {
        var installed = Path.Combine(baseDirectory, "app");

        if (File.Exists(Path.Combine(installed, Entry)))
        {
            return installed;
        }

        var current = new DirectoryInfo(baseDirectory);

        for (var level = 0; level < MaxLevels && current is not null; level++)
        {
            var candidate = Path.Combine(current.FullName, "app", "dist");

            if (File.Exists(Path.Combine(candidate, Entry)))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Was der Nutzer lesen soll, wenn nichts gefunden wurde. Ein leeres
    /// Fenster wäre hier das Schlimmste — es sähe aus wie ein Absturz.
    /// </summary>
    public const string MissingMessage =
        "Die Oberfläche wurde nicht gefunden.\n\n" +
        "Sie entsteht mit 'npm run build' im Ordner 'app' und muss als " +
        "Unterordner 'app' neben RemoteDesktopClient.exe liegen.";
}
