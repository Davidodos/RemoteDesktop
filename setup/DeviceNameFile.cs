namespace RemoteDesktopSetup;

/// <summary>
/// Wie dieses Gerät heißt — einmal gewählt, danach überall benutzt.
///
/// <para>
/// **Der Befund dahinter:** der Name wurde bis dahin bei *jeder* Kopplung neu
/// eingetippt („Name dieses Geräts — so steht es drüben in der Liste"). Wer
/// drei Geräte koppelte, vergab denselben Namen dreimal und tippte ihn beim
/// dritten Mal anders. Und wer nicht selbst koppelte, sondern nur seinen Code
/// vorzeigte, hieß drüben <c>DESKTOP-4F2K9L1</c>, weil dann
/// <c>Environment.MachineName</c> einsprang.
/// </para>
///
/// <para>
/// Jetzt steht er in einer Datei neben den übrigen Daten, und alle lesen sie:
/// der Agent für <c>/api/info</c> und die beiden Kopplungsantworten, das
/// Fenster für den Steckbrief bei gestopptem Agent. Gesetzt wird er im ersten
/// Schritt der Einrichtung und danach unter „Einstellungen".
/// </para>
///
/// <para>
/// Schwesterfassung am Handy: <c>host/HostPreference.kt</c>.
/// </para>
/// </summary>
public static class DeviceNameFile
{
    public const string FileName = "devicename.txt";

    /// <summary>
    /// Länger nennt sich kein Gerät. Derselbe Wert wie in
    /// <c>DeviceProfile.MAX_NAME</c> auf beiden Gegenseiten — ein Name, den die
    /// eine Seite annimmt und die andere verwirft, wäre schlimmer als ein zu
    /// langer.
    /// </summary>
    public const int MaxLength = 64;

    public static string In(string dataDirectory) => Path.Combine(dataDirectory, FileName);

    /// <summary>
    /// Der gewählte Name — oder der Windows-Name, solange keiner gewählt wurde.
    ///
    /// Ein Rückfall und keine leere Zeichenkette: der Name steht in fremden
    /// Geräteliste, und „" wäre dort ein Eintrag, den niemand zuordnen kann.
    /// </summary>
    public static string Read(string dataDirectory) =>
        Stored(dataDirectory) ?? Environment.MachineName;

    /// <summary>
    /// Ob schon jemand einen Namen vergeben hat. Die Einrichtung fragt danach:
    /// beim ersten Mal steht der Windows-Name als Vorschlag im Feld, danach der
    /// gewählte.
    /// </summary>
    public static bool IsSet(string dataDirectory) => Stored(dataDirectory) is not null;

    public static void Write(string dataDirectory, string name)
    {
        var clean = Sanitize(name)
                    ?? throw new ArgumentException("Ein Gerätename darf nicht leer sein.", nameof(name));

        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(In(dataDirectory), clean);
    }

    /// <summary>
    /// Was von einem eingetippten Namen übrig bleibt: getrimmt, gekürzt, und
    /// <c>null</c>, wenn nichts übrig ist. Zeilenumbrüche fliegen raus — die
    /// Datei hat genau eine Zeile.
    /// </summary>
    public static string? Sanitize(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (clean.Length > MaxLength)
        {
            clean = clean[..MaxLength].Trim();
        }

        return clean.Length == 0 ? null : clean;
    }

    private static string? Stored(string dataDirectory)
    {
        try
        {
            var path = In(dataDirectory);

            return File.Exists(path) ? Sanitize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein unlesbarer Name ist kein Grund, gar keinen zu haben.
            return null;
        }
    }
}
