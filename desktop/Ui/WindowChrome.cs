using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteDesktopClient.Ui;

/// <summary>
/// Die Titelzeile dunkel einfärben.
///
/// <para>
/// **Warum kein selbst gezeichneter Rahmen:** ein Fenster ohne Rahmen muss alles
/// selbst können, was Windows sonst mitbringt — Ziehen, Andocken an den
/// Bildschirmrand, die Aufteilungsvorschläge beim Zeigen auf „Maximieren", das
/// Verhalten beim Wechsel auf einen Bildschirm mit anderer Skalierung. Das
/// nachzubauen misslingt fast immer an einer dieser Stellen, und das Ergebnis
/// wirkt gebastelt, nicht professionell. Windows kann eine dunkle Titelzeile
/// selbst; sie muss nur bestellt werden.
/// </para>
///
/// <para>
/// Die Aufrufe schlagen auf älteren Fassungen von Windows 10 fehl und werden
/// dort schlicht ignoriert. Dann bleibt die Titelzeile hell und der Rest des
/// Fensters dunkel — nicht schön, aber auch nicht kaputt.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowChrome
{
    /// <summary>Dunkle Titelzeile. 20 seit Windows 10 2004, davor 19.</summary>
    private const int UseImmersiveDarkMode = 20;

    private const int UseImmersiveDarkModeBefore20H1 = 19;

    /// <summary>Eigene Farbe für Titelzeile und Rahmen — erst ab Windows 11.</summary>
    private const int CaptionColor = 35;

    private const int BorderColor = 34;

    private const int TextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// Auf ein Fenster anwenden. Muss nach dem Erzeugen des Fensterhandles
    /// laufen — vorher gibt es nichts einzufärben.
    /// </summary>
    public static void Darken(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Set(handle, UseImmersiveDarkMode, 1);
        Set(handle, UseImmersiveDarkModeBefore20H1, 1);

        // Windows 11 lässt die Titelzeile exakt in der Farbe der Seitenleiste
        // stehen. Damit hört das Fenster oben nicht auf, sondern geht durch.
        Set(handle, CaptionColor, Bgr(Theme.Rail));
        Set(handle, BorderColor, Bgr(Theme.Border));
        Set(handle, TextColor, Bgr(Theme.Text));
    }

    private static void Set(IntPtr handle, int attribute, int value)
    {
        try
        {
            DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Ohne Desktopfenster-Manager gibt es keine Titelzeile zum Färben.
            // Ein Fenster ist trotzdem besser als eine Fehlermeldung.
        }
    }

    /// <summary>DWM erwartet die Farbe als 0x00BBGGRR, nicht als RGB.</summary>
    private static int Bgr(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
