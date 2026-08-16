namespace RemoteDesktopSetup;

/// <summary>
/// Das Kürzel, mit dem dieser Rechner einen anderen vollständig übernimmt.
///
/// <para>
/// **Warum es hier liegt und nicht im Speicher der Oberfläche.** Vergeben wird
/// es beim ersten Verbinden — das passiert in der React-App —, angezeigt und
/// geändert wird es im Fenster unter „Einstellungen", und das ist nativ. Läge
/// es im <c>localStorage</c> der WebView, käme die native Seite nicht daran;
/// läge es in einer eigenen nativen Ablage, stünde es an zwei Orten, die nur so
/// lange übereinstimmen, wie niemand einen davon anfasst. Eine Datei, die beide
/// lesen, ist derselbe Weg wie beim Gerätenamen (siehe
/// <see cref="DeviceNameFile"/>).
/// </para>
///
/// <para>
/// **Warum es überhaupt gespeichert wird.** Solange die Übernahme läuft, geht
/// jeder Anschlag zum anderen Rechner. Dieses Kürzel ist das Einzige, was hier
/// bleibt — der einzige Weg zurück. Es zu vergessen hieße, den Rechner nur noch
/// über den Ausschalter zurückzubekommen.
/// </para>
///
/// <para>
/// Der Inhalt ist eine Zeile in der Form <c>ctrl+alt+KeyK</c>. Gelesen und
/// geschrieben wird sie auf der anderen Seite in <c>app/src/lib/hotkey.ts</c>;
/// diese Datei prüft nur, dass überhaupt etwas Plausibles darin steht — was ein
/// gültiges Kürzel ist, entscheidet die Seite, die die Tasten sieht.
/// </para>
/// </summary>
public static class HotkeyFile
{
    public const string FileName = "hotkey.txt";

    /// <summary>
    /// Länger wird kein Kürzel. Vier Modifier und ein Tastenname passen
    /// bequem hinein; alles darüber ist keine Eingabe mehr, sondern ein
    /// Versehen.
    /// </summary>
    public const int MaxLength = 64;

    public static string In(string dataDirectory) => Path.Combine(dataDirectory, FileName);

    /// <summary>
    /// Das vergebene Kürzel.
    /// </summary>
    /// <returns>
    /// <c>null</c>, solange keins vergeben wurde. Genau daran erkennt die App,
    /// dass sie beim ersten Verbinden danach fragen muss — eine Vorgabe hier
    /// wäre ein Kürzel, das niemand gesehen hat.
    /// </returns>
    public static string? Read(string dataDirectory)
    {
        var path = In(dataDirectory);

        try
        {
            return File.Exists(path) ? Sanitize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne lesbares Kürzel gibt es keine Übernahme — das ist unbequem,
            // aber harmlos. Ein Fehler wäre hier eine Meldung über etwas, das
            // gerade niemand tun wollte.
            return null;
        }
    }

    public static void Write(string dataDirectory, string hotkey)
    {
        var clean = Sanitize(hotkey)
                    ?? throw new ArgumentException(
                        "Ein leeres Kürzel wäre kein Weg zurück.", nameof(hotkey));

        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(In(dataDirectory), clean);
    }

    /// <summary>
    /// Was in die Datei darf: eine Zeile ohne Leerraum, nicht länger als
    /// <see cref="MaxLength"/>.
    /// </summary>
    /// <returns><c>null</c>, wenn nichts Brauchbares übrig bleibt.</returns>
    public static string? Sanitize(string? hotkey)
    {
        var trimmed = hotkey?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return null;
        }

        return trimmed.Any(char.IsWhiteSpace) ? null : trimmed;
    }
}
