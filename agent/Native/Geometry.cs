namespace RemoteDesktopAgent.Native;

/// <summary>Ein physischer Monitor, Koordinaten im virtuellen Desktop.</summary>
public sealed record MonitorInfo(
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary,
    string DeviceName);

/// <summary>Bounding-Box über alle Monitore. Bezugsrahmen für SendInput-Absolutkoordinaten.</summary>
public sealed record VirtualDesktop(int X, int Y, int Width, int Height);

/// <summary>
/// Reine Rechenlogik ohne Win32 — deshalb vollständig unit-testbar.
///
/// Die App schickt Positionen normalisiert (0..1) relativ zum <em>gewählten
/// Monitor</em>. Der Agent rechnet daraus die Absolutkoordinate um, die
/// SendInput erwartet: 0..65535 über den gesamten virtuellen Desktop.
/// </summary>
public static class Geometry
{
    private const int AbsoluteMax = 65535;

    /// <summary>
    /// Normalisierte Monitor-Position → SendInput-Absolutkoordinate.
    /// </summary>
    /// <param name="nx">0..1, links nach rechts auf dem Monitor</param>
    /// <param name="ny">0..1, oben nach unten auf dem Monitor</param>
    public static (int dx, int dy) ToAbsolute(
        double nx, double ny, MonitorInfo monitor, VirtualDesktop desktop)
    {
        // Außerhalb liegende Werte klemmen, statt den Cursor irgendwo hin zu
        // schleudern — Touch-Events am Displayrand liefern gern 1.0001.
        var clampedX = Math.Clamp(nx, 0.0, 1.0);
        var clampedY = Math.Clamp(ny, 0.0, 1.0);

        var screenX = monitor.X + clampedX * monitor.Width;
        var screenY = monitor.Y + clampedY * monitor.Height;

        return (
            NormalizeAxis(screenX, desktop.X, desktop.Width),
            NormalizeAxis(screenY, desktop.Y, desktop.Height));
    }

    /// <summary>
    /// Eine Achse auf 0..65535 abbilden. Der Teiler ist die Ausdehnung minus
    /// eins, weil sonst die letzte Pixelspalte nie erreicht wird.
    /// </summary>
    private static int NormalizeAxis(double value, int origin, int extent)
    {
        if (extent <= 1)
        {
            return 0;
        }

        var relative = (value - origin) * AbsoluteMax / (extent - 1);
        return Math.Clamp((int)Math.Round(relative), 0, AbsoluteMax);
    }

    /// <summary>Bounding-Box über alle Monitore.</summary>
    public static VirtualDesktop BoundingBox(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Monitor erforderlich.", nameof(monitors));
        }

        var left = monitors.Min(m => m.X);
        var top = monitors.Min(m => m.Y);
        var right = monitors.Max(m => m.X + m.Width);
        var bottom = monitors.Max(m => m.Y + m.Height);

        return new VirtualDesktop(left, top, right - left, bottom - top);
    }
}
