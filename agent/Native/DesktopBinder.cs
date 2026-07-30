using System.Runtime.InteropServices;

namespace RemoteDesktopAgent.Native;

/// <summary>
/// Hängt den aufrufenden Thread an den Desktop, der gerade die Eingaben
/// bekommt.
///
/// Windows kennt pro Sitzung mehrere Desktops: den normalen (<c>Default</c>),
/// den des Anmeldebildschirms (<c>Winlogon</c>) und den, auf dem UAC-Dialoge
/// erscheinen. Ein Thread sieht immer nur seinen eigenen — deshalb bleibt der
/// Bildschirm bei einem UAC-Dialog stehen und Tastendrücke landen im Leeren.
///
/// <para><b>Grenze:</b> Als gewöhnlicher Benutzerprozess darf man den sicheren
/// Desktop nicht öffnen; <see cref="BindToInputDesktop"/> schlägt dort mit
/// „Zugriff verweigert“ fehl. Vollständig lösen lässt sich das nur mit einem
/// Dienst in Sitzung 0, der pro Anmeldesitzung einen Prozess auf dem jeweils
/// aktiven Desktop startet. Siehe <c>docs/ARCHITEKTUR.md</c>.</para>
/// </summary>
public sealed class DesktopBinder(ILogger<DesktopBinder> logger)
{
    private const uint DesktopAccess = 0x0001    // DESKTOP_READOBJECTS
                                       | 0x0002  // DESKTOP_CREATEWINDOW
                                       | 0x0100  // DESKTOP_SWITCHDESKTOP
                                       | 0x0040; // DESKTOP_ENUMERATE

    private IntPtr _bound = IntPtr.Zero;

    /// <summary>
    /// Bindet den Thread an den aktuellen Eingabe-Desktop, falls der sich seit
    /// dem letzten Aufruf geändert hat. Gibt zurück, ob der Thread jetzt am
    /// richtigen Desktop hängt.
    /// </summary>
    public bool BindToInputDesktop()
    {
        var desktop = OpenInputDesktop(0, false, DesktopAccess);

        if (desktop == IntPtr.Zero)
        {
            // Passiert regelmäßig: Sperrbildschirm und UAC-Dialog laufen auf
            // einem Desktop, den ein Benutzerprozess nicht öffnen darf.
            logger.LogDebug(
                "Eingabe-Desktop nicht zu öffnen (Fehler {Error}).", Marshal.GetLastWin32Error());

            return false;
        }

        if (desktop == _bound)
        {
            CloseDesktop(desktop);
            return true;
        }

        if (!SetThreadDesktop(desktop))
        {
            logger.LogDebug(
                "Thread lässt sich nicht umhängen (Fehler {Error}).", Marshal.GetLastWin32Error());

            CloseDesktop(desktop);
            return false;
        }

        if (_bound != IntPtr.Zero)
        {
            CloseDesktop(_bound);
        }

        _bound = desktop;
        logger.LogInformation("Thread hängt jetzt am aktuellen Eingabe-Desktop.");

        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
