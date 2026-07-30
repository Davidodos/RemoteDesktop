using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Führt geparste Eingabe-Befehle aus.
///
/// Hält die Monitor-Liste kurzzeitig gecacht: bei 60 Bewegungs-Events pro
/// Sekunde wäre eine Enumeration je Event Verschwendung, aber ein dauerhafter
/// Cache würde einen Monitor-Wechsel verschlafen.
/// </summary>
public sealed class InputExecutor(
    InputSender sender,
    MonitorEnumerator monitors,
    ILogger<InputExecutor> logger)
{
    private static readonly TimeSpan MonitorCacheLifetime = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private IReadOnlyList<MonitorInfo> _cachedMonitors = [];
    private VirtualDesktop _cachedDesktop = new(0, 0, 0, 0);
    private DateTime _cachedAt = DateTime.MinValue;

    public void Execute(InputCommand command)
    {
        switch (command)
        {
            case InputCommand.MoveAbsolute move:
                ExecuteMove(move);
                break;

            case InputCommand.MoveRelative move:
                sender.MoveRelative(move.Dx, move.Dy);
                break;

            case InputCommand.ButtonDown down:
                sender.MouseDown(down.Button);
                break;

            case InputCommand.ButtonUp up:
                sender.MouseUp(up.Button);
                break;

            case InputCommand.Click click:
                sender.Click(click.Button);
                break;

            case InputCommand.Scroll scroll:
                sender.Scroll(scroll.Vertical, scroll.Horizontal);
                break;

            case InputCommand.KeyDown keyDown:
                sender.KeyDown(keyDown.VirtualKey);
                break;

            case InputCommand.KeyUp keyUp:
                sender.KeyUp(keyUp.VirtualKey);
                break;

            case InputCommand.KeyCombo combo:
                sender.KeyCombo(combo.Modifiers, combo.VirtualKey);
                break;

            case InputCommand.TypeText text:
                sender.TypeText(text.Text);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(command), command, "Unbehandelter Eingabe-Befehl.");
        }
    }

    private void ExecuteMove(InputCommand.MoveAbsolute move)
    {
        var (available, desktop) = GetLayout();

        if (move.Monitor >= available.Count)
        {
            // Kann legitim passieren: die App hat noch die alte Monitor-Liste,
            // während gerade ein Bildschirm abgezogen wurde. Auf den primären
            // ausweichen statt den Befehl zu verwerfen.
            logger.LogDebug(
                "Monitor {Requested} existiert nicht ({Count} vorhanden), nutze primären.",
                move.Monitor, available.Count);

            var primary = available.FirstOrDefault(m => m.IsPrimary) ?? available[0];
            var (px, py) = Geometry.ToAbsolute(move.X, move.Y, primary, desktop);
            sender.MoveAbsolute(px, py);
            return;
        }

        var target = available[move.Monitor];
        var (dx, dy) = Geometry.ToAbsolute(move.X, move.Y, target, desktop);
        sender.MoveAbsolute(dx, dy);
    }

    /// <summary>Aktuelles Monitor-Layout, höchstens <see cref="MonitorCacheLifetime"/> alt.</summary>
    public (IReadOnlyList<MonitorInfo> Monitors, VirtualDesktop Desktop) GetLayout()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _cachedAt < MonitorCacheLifetime && _cachedMonitors.Count > 0)
            {
                return (_cachedMonitors, _cachedDesktop);
            }

            _cachedMonitors = monitors.Enumerate();
            _cachedDesktop = Geometry.BoundingBox(_cachedMonitors);
            _cachedAt = DateTime.UtcNow;

            return (_cachedMonitors, _cachedDesktop);
        }
    }
}
