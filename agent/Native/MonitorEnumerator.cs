namespace RemoteDesktopAgent.Native;

/// <summary>
/// Fragt die aktuell angeschlossenen Monitore bei Windows ab.
///
/// Wird bei jedem Aufruf neu enumeriert statt gecacht — Monitore kommen und
/// gehen (Laptop andockt, Fernseher an, PC wacht mit anderer Konfiguration
/// auf), und eine veraltete Liste bedeutet Klicks an der falschen Stelle.
/// </summary>
public sealed class MonitorEnumerator
{
    public IReadOnlyList<MonitorInfo> Enumerate()
    {
        var found = new List<MonitorInfo>();
        var index = 0;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref Win32.RECT rect, IntPtr data)
        {
            var info = new Win32.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>()
            };

            if (!Win32.GetMonitorInfo(hMonitor, ref info))
            {
                return true;   // Diesen überspringen, Rest weiter enumerieren.
            }

            found.Add(new MonitorInfo(
                Index: index++,
                X: info.rcMonitor.Left,
                Y: info.rcMonitor.Top,
                Width: info.rcMonitor.Right - info.rcMonitor.Left,
                Height: info.rcMonitor.Bottom - info.rcMonitor.Top,
                IsPrimary: (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0,
                DeviceName: info.szDevice));

            return true;
        }

        if (!Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
        {
            throw new InvalidOperationException("EnumDisplayMonitors ist fehlgeschlagen.");
        }

        if (found.Count == 0)
        {
            throw new InvalidOperationException(
                "Windows meldet keinen einzigen Monitor. Läuft der Agent in Session 0?");
        }

        // Stabile Reihenfolge: links nach rechts, dann oben nach unten. Sonst
        // wechseln die Monitor-Tabs in der App bei jedem Reconnect die Plätze.
        var ordered = found
            .OrderBy(m => m.X)
            .ThenBy(m => m.Y)
            .Select((m, i) => m with { Index = i })
            .ToList();

        return ordered;
    }

    /// <summary>Bounding-Box laut Windows — Gegenprobe zu <see cref="Geometry.BoundingBox"/>.</summary>
    public VirtualDesktop GetVirtualDesktop() => new(
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
}
