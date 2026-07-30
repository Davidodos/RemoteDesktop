using System.Runtime.InteropServices;

namespace RemoteDesktopAgent.Native;

public enum MouseButton
{
    Left,
    Right,
    Middle
}

/// <summary>
/// Schickt Maus- und Tastatur-Events über SendInput an Windows.
///
/// Down und Up sind bewusst getrennte Operationen — nur so lassen sich
/// "Taste gedrückt halten", Drag-Gesten und der Autoklicker sauber abbilden.
/// </summary>
public sealed class InputSender
{
    private static readonly int InputSize = Marshal.SizeOf<Win32.INPUT>();

    /// <summary>Cursor an eine Absolutposition setzen (0..65535, virtueller Desktop).</summary>
    public void MoveAbsolute(int dx, int dy)
    {
        Send(MouseInput(dx, dy,
            Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK));
    }

    /// <summary>Cursor relativ verschieben — für die Trackpad-Fläche in der App.</summary>
    public void MoveRelative(int dx, int dy)
    {
        Send(MouseInput(dx, dy, Win32.MOUSEEVENTF_MOVE));
    }

    public void MouseDown(MouseButton button) => Send(ButtonInput(button, isDown: true));

    public void MouseUp(MouseButton button) => Send(ButtonInput(button, isDown: false));

    public void Click(MouseButton button)
    {
        Send(ButtonInput(button, isDown: true), ButtonInput(button, isDown: false));
    }

    /// <summary>Mausrad. Positive Werte scrollen nach oben bzw. rechts.</summary>
    public void Scroll(int verticalNotches, int horizontalNotches)
    {
        var inputs = new List<Win32.INPUT>(2);

        if (verticalNotches != 0)
        {
            inputs.Add(MouseInput(0, 0, Win32.MOUSEEVENTF_WHEEL,
                unchecked((uint)(verticalNotches * Win32.WHEEL_DELTA))));
        }

        if (horizontalNotches != 0)
        {
            inputs.Add(MouseInput(0, 0, Win32.MOUSEEVENTF_HWHEEL,
                unchecked((uint)(horizontalNotches * Win32.WHEEL_DELTA))));
        }

        if (inputs.Count > 0)
        {
            Send(inputs.ToArray());
        }
    }

    public void KeyDown(ushort virtualKey) => Send(KeyInput(virtualKey, isDown: true));

    public void KeyUp(ushort virtualKey) => Send(KeyInput(virtualKey, isDown: false));

    /// <summary>
    /// Tastenkombination: Modifier runter, Taste antippen, Modifier in
    /// umgekehrter Reihenfolge wieder hoch. So kommt Strg+Shift+Esc an.
    /// </summary>
    public void KeyCombo(IReadOnlyList<ushort> modifiers, ushort virtualKey)
    {
        var inputs = new List<Win32.INPUT>(modifiers.Count * 2 + 2);

        foreach (var modifier in modifiers)
        {
            inputs.Add(KeyInput(modifier, isDown: true));
        }

        inputs.Add(KeyInput(virtualKey, isDown: true));
        inputs.Add(KeyInput(virtualKey, isDown: false));

        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            inputs.Add(KeyInput(modifiers[i], isDown: false));
        }

        Send(inputs.ToArray());
    }

    /// <summary>
    /// Text als Unicode tippen. Umgeht das Tastaturlayout komplett — nötig,
    /// weil die Handy-Tastatur Zeichen liefert, keine Scancodes.
    /// </summary>
    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new List<Win32.INPUT>(text.Length * 2);

        foreach (var unit in text)
        {
            inputs.Add(UnicodeInput(unit, isDown: true));
            inputs.Add(UnicodeInput(unit, isDown: false));
        }

        Send(inputs.ToArray());
    }

    // ---- Bau der INPUT-Strukturen -------------------------------------

    private static Win32.INPUT MouseInput(int dx, int dy, uint flags, uint mouseData = 0) => new()
    {
        type = Win32.INPUT_MOUSE,
        u = new Win32.INPUTUNION
        {
            mi = new Win32.MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = mouseData,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    private static Win32.INPUT ButtonInput(MouseButton button, bool isDown)
    {
        var flag = button switch
        {
            MouseButton.Left => isDown ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP,
            MouseButton.Right => isDown ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => isDown ? Win32.MOUSEEVENTF_MIDDLEDOWN : Win32.MOUSEEVENTF_MIDDLEUP,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unbekannte Maustaste.")
        };

        return MouseInput(0, 0, flag);
    }

    private static Win32.INPUT KeyInput(ushort virtualKey, bool isDown)
    {
        var flags = isDown ? 0u : Win32.KEYEVENTF_KEYUP;

        if (VirtualKeys.IsExtended(virtualKey))
        {
            flags |= Win32.KEYEVENTF_EXTENDEDKEY;
        }

        return new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static Win32.INPUT UnicodeInput(char unit, bool isDown) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new Win32.INPUTUNION
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = unit,
                dwFlags = Win32.KEYEVENTF_UNICODE | (isDown ? 0u : Win32.KEYEVENTF_KEYUP),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    private static void Send(params Win32.INPUT[] inputs)
    {
        var sent = Win32.SendInput((uint)inputs.Length, inputs, InputSize);

        if (sent != inputs.Length)
        {
            // Passiert typischerweise, wenn UIPI blockt: ein Prozess mit
            // niedrigerer Integritätsstufe darf keine Events an ein elevated
            // Fenster schicken. Laut werden statt still schlucken.
            throw new InvalidOperationException(
                $"SendInput hat nur {sent} von {inputs.Length} Events akzeptiert " +
                $"(Win32-Fehler {Marshal.GetLastWin32Error()}). " +
                "Meist UIPI — der Agent braucht dieselbe Rechtestufe wie das Zielfenster.");
        }
    }
}
