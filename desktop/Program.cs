namespace RemoteDesktopClient;

/// <summary>
/// Die eine <c>.exe</c>, die ein Mensch startet.
///
/// <para>
/// Sie ist Tray-Programm, Einrichtung und Fernsteuerfenster in einem. Der Agent
/// bleibt ein eigener Dienst mit eigener Programmdatei — er läuft unter SYSTEM,
/// und der soll weder WinForms noch eine Anzeigekomponente mit sich tragen. Von
/// hier aus wird er über die Dienstverwaltung angesprochen, nie direkt gestartet.
/// </para>
///
/// <para>
/// Ein zweiter Aufrufweg führt an der Oberfläche vorbei: mit
/// <c>--admin-task</c> ruft sich dieses Programm selbst erhöht auf, erledigt
/// genau einen Handgriff und beendet sich. Siehe <see cref="Elevation"/>.
/// </para>
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Der erhöhte Aufruf: kein Fenster, kein Tray, keine Einmaligkeitssperre.
        // Er tut eine Sache und geht wieder.
        if (args.Contains(Elevation.TaskSwitch))
        {
            return Elevation.Execute(args);
        }

        // Das letzte Netz: was sonst nirgends aufgefangen wird, landet in einer
        // Datei und nicht in einem Absturzfenster mit Aufrufliste. Ein Fenster,
        // das man wegklicken muss und das das Programm mitnimmt, ist die
        // schlechteste Art, von einem Fehler zu erfahren.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Ohne das rendert die WebView auf skalierten Displays unscharf.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Das Mausrad gehört dem Element unter dem Zeiger, nicht dem mit dem
        // Fokus. Warum das eine eigene Umleitung braucht, steht in WheelRouter.
        Application.AddMessageFilter(new Ui.WheelRouter());

        // Ein zweites Tray-Icon wäre nicht nur hässlich, sondern verwirrend:
        // beide Fenster teilten sich denselben localStorage und überschrieben
        // sich gegenseitig die Sitzung.
        using var single = new Mutex(initiallyOwned: true, @"Local\RemoteDesktopClient", out var first);

        if (!first)
        {
            return 0;
        }

        // Fehlende Teile sind seit V3 kein Grund mehr, gar nicht erst zu starten:
        // genau dann braucht man die Oberfläche am dringendsten, weil sie sagt,
        // was fehlt, und den Knopf dazu hat. Früher endete das Programm hier mit
        // einer Meldung, und wer nur den Agent installiert hatte, sah nie ein
        // Fenster.
        Application.Run(new ClientTrayContext(WebAppLocator.Locate(AppContext.BaseDirectory)));

        return 0;
    }

    /// <summary>
    /// Wohin ein unerwarteter Fehler geht: in eine Datei im Profil des
    /// Benutzers, angehängt, mit Zeitstempel. Sie ist das Erste, wonach man bei
    /// „es tut plötzlich nichts mehr" fragen kann.
    /// </summary>
    private static void Log(Exception? failure)
    {
        if (failure is null)
        {
            return;
        }

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RemoteDesktop");

            Directory.CreateDirectory(folder);

            File.AppendAllText(
                Path.Combine(folder, "fehler.log"),
                $"{DateTime.Now:u}  {failure}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Wenn nicht einmal das geht, ist Schweigen die letzte Möglichkeit.
        }
    }
}
