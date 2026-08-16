namespace RemoteDesktopClient;

/// <summary>
/// Eine Tastenkombination, wie sie beide Seiten schreiben.
///
/// <para>
/// Das Kürzel für den Vollzugriff wird in der Seite gedrückt und im Fenster
/// angezeigt und geändert. Beide müssen dasselbe darunter verstehen — deshalb
/// steht hier die Windows-Hälfte von <c>app/src/lib/hotkey.ts</c>: dasselbe
/// Format (<c>ctrl+alt+KeyK</c>), dieselben Tastennamen, dieselbe Schreibweise
/// für Menschen (<c>Strg+Alt+K</c>).
/// </para>
///
/// <para>
/// Gemerkt wird der Name aus <c>KeyboardEvent.code</c> und nicht das Zeichen:
/// <c>code</c> sagt, welche Taste unter dem Finger liegt, unabhängig von Layout
/// und Modifiern. Bei <c>key</c> hinge das Kürzel davon ab, in welcher Sprache
/// jemand gerade schreibt.
/// </para>
/// </summary>
public readonly record struct HotkeyCombination(
    bool Ctrl, bool Alt, bool Shift, bool Meta, string Code);

public static class HotkeyKeys
{
    /// <summary>
    /// Der Anschlag aus WinForms als Kombination.
    /// </summary>
    /// <returns>
    /// <c>null</c>, solange nur Modifier liegen oder die Taste keinen Namen hat,
    /// den die Seite kennt. Beim Greifen nach Strg+Alt+K ist das der Normalfall
    /// und kein Fehler.
    /// </returns>
    public static HotkeyCombination? From(KeyEventArgs pressed)
    {
        if (Name(pressed.KeyCode) is not { } code)
        {
            return null;
        }

        var combination = new HotkeyCombination(
            pressed.Control, pressed.Alt, pressed.Shift, Meta: false, code);

        // Ohne Modifier fiele das Kürzel mitten im Tippen — dieselbe Regel wie
        // in `isUsableHotkey` auf der anderen Seite.
        return combination.Ctrl || combination.Alt ? combination : null;
    }

    /// <summary>Für die Ablage: <c>ctrl+alt+KeyK</c>.</summary>
    public static string Serialize(HotkeyCombination combination)
    {
        var parts = new List<string>();

        if (combination.Ctrl)
        {
            parts.Add("ctrl");
        }

        if (combination.Alt)
        {
            parts.Add("alt");
        }

        if (combination.Shift)
        {
            parts.Add("shift");
        }

        if (combination.Meta)
        {
            parts.Add("meta");
        }

        parts.Add(combination.Code);

        return string.Join('+', parts);
    }

    /// <summary>
    /// Zurück aus der Datei. <c>null</c>, wenn dort nichts Brauchbares stand —
    /// eine Datei, die auch von Hand beschreibbar ist, enthält irgendwann
    /// Unsinn.
    /// </summary>
    public static HotkeyCombination? Parse(string? raw)
    {
        var parts = (raw ?? string.Empty)
            .Trim()
            .Split('+', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return null;
        }

        var code = parts[^1];
        var named = parts[..^1].Select(part => part.ToLowerInvariant()).ToHashSet();

        var combination = new HotkeyCombination(
            named.Contains("ctrl"),
            named.Contains("alt"),
            named.Contains("shift"),
            named.Contains("meta"),
            code);

        return combination.Ctrl || combination.Alt || combination.Meta ? combination : null;
    }

    /// <summary>Wie es dasteht: <c>Strg+Alt+K</c>.</summary>
    public static string Describe(HotkeyCombination combination)
    {
        var parts = new List<string>();

        if (combination.Ctrl)
        {
            parts.Add("Strg");
        }

        if (combination.Alt)
        {
            parts.Add("Alt");
        }

        if (combination.Shift)
        {
            parts.Add("Umschalt");
        }

        if (combination.Meta)
        {
            parts.Add("Windows");
        }

        parts.Add(Readable(combination.Code));

        return string.Join('+', parts);
    }

    /// <summary>
    /// Der Name, den die Seite kennt — <c>null</c> für alles, was für sich
    /// genommen kein Kürzel ergibt.
    /// </summary>
    private static string? Name(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => $"Key{key}",
        >= Keys.D0 and <= Keys.D9 => $"Digit{(int)key - (int)Keys.D0}",
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Numpad{(int)key - (int)Keys.NumPad0}",
        >= Keys.F1 and <= Keys.F24 => $"F{(int)key - (int)Keys.F1 + 1}",
        Keys.Escape => "Escape",
        Keys.Return => "Enter",
        Keys.Space => "Space",
        Keys.Tab => "Tab",
        Keys.Back => "Backspace",
        Keys.Delete => "Delete",
        Keys.Insert => "Insert",
        Keys.Home => "Home",
        Keys.End => "End",
        Keys.PageUp => "PageUp",
        Keys.PageDown => "PageDown",
        Keys.Scroll => "ScrollLock",
        Keys.Pause => "Pause",
        Keys.Up => "ArrowUp",
        Keys.Down => "ArrowDown",
        Keys.Left => "ArrowLeft",
        Keys.Right => "ArrowRight",
        _ => null
    };

    /// <summary>Gegenstück zu <c>describeCode</c> in <c>app/src/lib/hotkey.ts</c>.</summary>
    private static string Readable(string code) => code switch
    {
        _ when code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) => code[3..],
        _ when code.StartsWith("Digit", StringComparison.Ordinal) => code[5..],
        _ when code.StartsWith("Numpad", StringComparison.Ordinal) => $"Num {code[6..]}",
        "Escape" => "Esc",
        "Enter" => "Eingabe",
        "Space" => "Leertaste",
        "Backspace" => "Rücktaste",
        "Delete" => "Entf",
        "Insert" => "Einfg",
        "Home" => "Pos1",
        "End" => "Ende",
        "PageUp" => "Bild ↑",
        "PageDown" => "Bild ↓",
        "ScrollLock" => "Rollen",
        _ => code
    };
}
