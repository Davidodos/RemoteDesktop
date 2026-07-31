namespace RemoteDesktopClient;

/// <summary>
/// Der Windows-Client: ein Tray-Programm mit einem WebView2-Fenster, in dem
/// dieselbe React-App läuft wie auf dem Handy.
///
/// Der Agent bleibt davon unberührt — er ist ein eigener Dienst und wird von
/// hier aus nur über die Loopback-Adresse angesprochen, für Kopplungscode und
/// Widerruf.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Ohne das rendert die WebView auf skalierten Displays unscharf.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Ein zweites Tray-Icon wäre nicht nur hässlich, sondern verwirrend:
        // beide Fenster teilten sich denselben localStorage und überschrieben
        // sich gegenseitig die Sitzung.
        using var single = new Mutex(initiallyOwned: true, @"Local\RemoteDesktopClient", out var first);

        if (!first)
        {
            return;
        }

        if (WebView2Runtime.InstalledVersion() is null)
        {
            Fail(WebView2Runtime.MissingMessage);
            return;
        }

        var appDirectory = WebAppLocator.Locate(AppContext.BaseDirectory);

        if (appDirectory is null)
        {
            Fail(WebAppLocator.MissingMessage);
            return;
        }

        Application.Run(new ClientTrayContext(appDirectory));
    }

    /// <summary>
    /// Ein Fenster, das gar nicht erst aufgeht, wirkt wie ein Absturz. Deshalb
    /// endet jeder Abbruch beim Start mit einem Satz, der sagt, was fehlt.
    /// </summary>
    private static void Fail(string message) =>
        MessageBox.Show(message, "RemoteDesktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
