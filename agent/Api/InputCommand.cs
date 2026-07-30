using System.Text.Json;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Api;

/// <summary>
/// Ein Eingabe-Befehl vom Handy. Bewusst als geschlossene Hierarchie, damit
/// der Executor per Pattern-Matching alle Fälle abdeckt.
/// </summary>
public abstract record InputCommand
{
    /// <summary>Cursor auf eine Position des gewählten Monitors setzen (0..1).</summary>
    public sealed record MoveAbsolute(int Monitor, double X, double Y) : InputCommand;

    /// <summary>Relative Bewegung vom Trackpad.</summary>
    public sealed record MoveRelative(int Dx, int Dy) : InputCommand;

    public sealed record ButtonDown(MouseButton Button) : InputCommand;

    public sealed record ButtonUp(MouseButton Button) : InputCommand;

    public sealed record Click(MouseButton Button) : InputCommand;

    /// <summary>Mausrad in Rasterschritten. Positiv = hoch bzw. rechts.</summary>
    public sealed record Scroll(int Vertical, int Horizontal) : InputCommand;

    public sealed record KeyDown(ushort VirtualKey) : InputCommand;

    public sealed record KeyUp(ushort VirtualKey) : InputCommand;

    /// <summary>Tastenkombination, z.B. Strg+Shift+Esc.</summary>
    public sealed record KeyCombo(IReadOnlyList<ushort> Modifiers, ushort VirtualKey) : InputCommand;

    /// <summary>Freitext als Unicode tippen.</summary>
    public sealed record TypeText(string Text) : InputCommand;
}

/// <summary>Ergebnis des Parsens — Fehler sind ein normaler Fall, keine Exception.</summary>
public readonly record struct ParseResult(InputCommand? Command, string? Error)
{
    public bool IsSuccess => Command is not null;

    public static ParseResult Ok(InputCommand command) => new(command, null);

    public static ParseResult Fail(string error) => new(null, error);
}

/// <summary>
/// Übersetzt die JSON-Nachrichten des Input-Sockets in <see cref="InputCommand"/>.
///
/// Rein funktional und ohne Win32-Bezug, damit das Protokoll unter Test steht —
/// hier falsch abzubiegen bedeutet Klicks an der falschen Stelle.
/// </summary>
public static class InputCommandParser
{
    /// <summary>Obergrenze für einen Tipp-Vorgang, schützt vor Endlos-Payloads.</summary>
    private const int MaxTextLength = 4096;

    /// <summary>Mehr als das ist kein Scrollen mehr, sondern ein Fehler.</summary>
    private const int MaxScrollNotches = 100;

    public static ParseResult Parse(string json)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ParseResult.Fail($"Kein gültiges JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParseResult.Fail("Erwartet wird ein JSON-Objekt.");
            }

            if (!root.TryGetProperty("t", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return ParseResult.Fail("Feld 't' (Befehlstyp) fehlt.");
            }

            return typeElement.GetString() switch
            {
                "move" => ParseMoveAbsolute(root),
                "moverel" => ParseMoveRelative(root),
                "down" => ParseButton(root, static b => new InputCommand.ButtonDown(b)),
                "up" => ParseButton(root, static b => new InputCommand.ButtonUp(b)),
                "click" => ParseButton(root, static b => new InputCommand.Click(b)),
                "scroll" => ParseScroll(root),
                "keydown" => ParseKey(root, static k => new InputCommand.KeyDown(k)),
                "keyup" => ParseKey(root, static k => new InputCommand.KeyUp(k)),
                "key" => ParseKeyCombo(root),
                "text" => ParseText(root),
                var other => ParseResult.Fail($"Unbekannter Befehlstyp '{other}'.")
            };
        }
    }

    private static ParseResult ParseMoveAbsolute(JsonElement root)
    {
        if (!TryGetDouble(root, "x", out var x) || !TryGetDouble(root, "y", out var y))
        {
            return ParseResult.Fail("'move' braucht die Zahlen 'x' und 'y'.");
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return ParseResult.Fail("'x' und 'y' dürfen nicht NaN sein.");
        }

        var monitor = TryGetInt(root, "monitor", out var m) ? m : 0;

        if (monitor < 0)
        {
            return ParseResult.Fail("'monitor' darf nicht negativ sein.");
        }

        return ParseResult.Ok(new InputCommand.MoveAbsolute(monitor, x, y));
    }

    private static ParseResult ParseMoveRelative(JsonElement root)
    {
        if (!TryGetInt(root, "dx", out var dx) || !TryGetInt(root, "dy", out var dy))
        {
            return ParseResult.Fail("'moverel' braucht die Ganzzahlen 'dx' und 'dy'.");
        }

        return ParseResult.Ok(new InputCommand.MoveRelative(dx, dy));
    }

    private static ParseResult ParseButton(JsonElement root, Func<MouseButton, InputCommand> build)
    {
        // Ohne Angabe ist die linke Maustaste gemeint — der mit Abstand
        // häufigste Fall, und die App muss ihn nicht jedes Mal ausschreiben.
        var name = root.TryGetProperty("button", out var element)
                   && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : "left";

        return name?.ToLowerInvariant() switch
        {
            "left" => ParseResult.Ok(build(MouseButton.Left)),
            "right" => ParseResult.Ok(build(MouseButton.Right)),
            "middle" => ParseResult.Ok(build(MouseButton.Middle)),
            var other => ParseResult.Fail($"Unbekannte Maustaste '{other}'.")
        };
    }

    private static ParseResult ParseScroll(JsonElement root)
    {
        var vertical = TryGetInt(root, "dy", out var dy) ? dy : 0;
        var horizontal = TryGetInt(root, "dx", out var dx) ? dx : 0;

        if (vertical == 0 && horizontal == 0)
        {
            return ParseResult.Fail("'scroll' braucht 'dy' oder 'dx' ungleich null.");
        }

        if (Math.Abs(vertical) > MaxScrollNotches || Math.Abs(horizontal) > MaxScrollNotches)
        {
            return ParseResult.Fail($"Scroll-Betrag über dem Limit von {MaxScrollNotches}.");
        }

        return ParseResult.Ok(new InputCommand.Scroll(vertical, horizontal));
    }

    private static ParseResult ParseKey(JsonElement root, Func<ushort, InputCommand> build)
    {
        if (!TryGetKey(root, "key", out var virtualKey, out var error))
        {
            return ParseResult.Fail(error);
        }

        return ParseResult.Ok(build(virtualKey));
    }

    private static ParseResult ParseKeyCombo(JsonElement root)
    {
        if (!TryGetKey(root, "key", out var virtualKey, out var error))
        {
            return ParseResult.Fail(error);
        }

        var modifiers = new List<ushort>();

        if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in mods.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    return ParseResult.Fail("'mods' darf nur Strings enthalten.");
                }

                if (!VirtualKeys.TryResolve(entry.GetString()!, out var modifier))
                {
                    return ParseResult.Fail($"Unbekannter Modifier '{entry.GetString()}'.");
                }

                modifiers.Add(modifier);
            }
        }

        return ParseResult.Ok(new InputCommand.KeyCombo(modifiers, virtualKey));
    }

    private static ParseResult ParseText(JsonElement root)
    {
        if (!root.TryGetProperty("text", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return ParseResult.Fail("'text' braucht das Feld 'text'.");
        }

        var text = element.GetString()!;

        if (text.Length > MaxTextLength)
        {
            return ParseResult.Fail($"Text länger als {MaxTextLength} Zeichen.");
        }

        return ParseResult.Ok(new InputCommand.TypeText(text));
    }

    private static bool TryGetKey(JsonElement root, string property, out ushort virtualKey, out string error)
    {
        virtualKey = 0;

        if (!root.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            error = $"Feld '{property}' (Tastenname) fehlt.";
            return false;
        }

        var name = element.GetString()!;

        if (!VirtualKeys.TryResolve(name, out virtualKey))
        {
            error = $"Unbekannte Taste '{name}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryGetDouble(JsonElement root, string property, out double value)
    {
        value = 0;

        return root.TryGetProperty(property, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetDouble(out value);
    }

    private static bool TryGetInt(JsonElement root, string property, out int value)
    {
        value = 0;

        if (!root.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (element.TryGetInt32(out value))
        {
            return true;
        }

        // Die App rechnet mit Fließkomma; ganzzahlige Werte kommen deshalb
        // gelegentlich als 12.0 an.
        if (element.TryGetDouble(out var asDouble) && Math.Abs(asDouble) < int.MaxValue)
        {
            value = (int)Math.Round(asDouble);
            return true;
        }

        return false;
    }
}
