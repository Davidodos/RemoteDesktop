using System.Reflection;

namespace RemoteDesktopClient.Ui;

/// <summary>
/// Das Symbol des Programms — dasselbe, das auch auf dem Handy steht.
///
/// <para>
/// Es liegt als Ressource *in* der .exe und nicht als Datei daneben: eine Datei
/// daneben kann fehlen, und ein Programm ohne Symbol im Infobereich ist ein
/// Programm, das man nicht wiederfindet. Erzeugt wird die <c>.ico</c> aus
/// <c>assets/icon.svg</c> mit <c>node scripts/icons.mjs</c>.
/// </para>
/// </summary>
public static class Brand
{
    private const string Resource = "RemoteDesktopClient.RemoteDesktop.ico";

    /// <summary>
    /// Das Symbol in der Größe, die Windows an dieser Stelle zeigt.
    ///
    /// Die Größe wird ausdrücklich mitgegeben, weil die <c>.ico</c> mehrere
    /// Fassungen enthält: bei 16 Pixeln eine gröbere Zeichnung, ab 48 die
    /// feine. Ohne Angabe nimmt Windows die erste und rechnet sie herunter —
    /// das ist genau die Fassung, die dabei zu Brei wird.
    /// </summary>
    public static Icon? Load(int size)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);

            return stream is null ? null : new Icon(stream, size, size);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Ein fehlendes Symbol ist kein Grund, das Fenster nicht zu zeigen.
            return null;
        }
    }
}
