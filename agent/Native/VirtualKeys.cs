namespace RemoteDesktopAgent.Native;

/// <summary>
/// Übersetzt die Tastennamen, die die App schickt, in Windows-Virtual-Key-Codes.
///
/// Die App sendet sprechende Namen ("ctrl", "f5", "arrowup") statt Zahlen —
/// das hält das Protokoll lesbar und macht Debugging erträglich.
/// </summary>
public static class VirtualKeys
{
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;    // Alt
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_LWIN = 0x5B;

    // Mediensteuerung
    public const ushort VK_VOLUME_MUTE = 0xAD;
    public const ushort VK_VOLUME_DOWN = 0xAE;
    public const ushort VK_VOLUME_UP = 0xAF;
    public const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    public const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    public const ushort VK_MEDIA_STOP = 0xB2;
    public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

    /// <summary>
    /// Tasten, die das Extended-Flag brauchen. Ohne das Flag landen z.B. die
    /// Pfeiltasten als Nummernblock-Eingabe, und die Media-Keys kommen gar
    /// nicht an.
    /// </summary>
    private static readonly HashSet<ushort> Extended =
    [
        0x21, 0x22, 0x23, 0x24,             // PageUp, PageDown, End, Home
        0x25, 0x26, 0x27, 0x28,             // Pfeile
        0x2D, 0x2E,                         // Insert, Delete
        0x5B, 0x5C, 0x5D,                   // Win links/rechts, Menü
        0x6F,                               // Numpad-Division
        0xA3,                               // Strg rechts
        0xA5,                               // Alt rechts
        VK_VOLUME_MUTE, VK_VOLUME_DOWN, VK_VOLUME_UP,
        VK_MEDIA_NEXT_TRACK, VK_MEDIA_PREV_TRACK,
        VK_MEDIA_STOP, VK_MEDIA_PLAY_PAUSE
    ];

    private static readonly IReadOnlyDictionary<string, ushort> ByName =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // Modifier
            ["ctrl"] = VK_CONTROL,
            ["control"] = VK_CONTROL,
            ["alt"] = VK_MENU,
            ["shift"] = VK_SHIFT,
            ["win"] = VK_LWIN,
            ["meta"] = VK_LWIN,

            // Steuerung
            ["escape"] = 0x1B,
            ["esc"] = 0x1B,
            ["tab"] = 0x09,
            ["enter"] = 0x0D,
            ["return"] = 0x0D,
            ["space"] = 0x20,
            ["backspace"] = 0x08,
            ["delete"] = 0x2E,
            ["insert"] = 0x2D,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["pageup"] = 0x21,
            ["pagedown"] = 0x22,
            ["capslock"] = 0x14,
            ["printscreen"] = 0x2C,

            // Pfeile
            ["arrowup"] = 0x26,
            ["arrowdown"] = 0x28,
            ["arrowleft"] = 0x25,
            ["arrowright"] = 0x27,

            // Medien
            ["playpause"] = VK_MEDIA_PLAY_PAUSE,
            ["nexttrack"] = VK_MEDIA_NEXT_TRACK,
            ["prevtrack"] = VK_MEDIA_PREV_TRACK,
            ["stop"] = VK_MEDIA_STOP,
            ["volumeup"] = VK_VOLUME_UP,
            ["volumedown"] = VK_VOLUME_DOWN,
            ["mute"] = VK_VOLUME_MUTE
        };

    public static bool IsExtended(ushort virtualKey) => Extended.Contains(virtualKey);

    /// <summary>
    /// Tastenname → VK-Code. Deckt zusätzlich zu der Tabelle oben die
    /// generischen Fälle ab: "a".."z", "0".."9", "f1".."f24".
    /// </summary>
    public static bool TryResolve(string name, out ushort virtualKey)
    {
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim();

        if (ByName.TryGetValue(key, out virtualKey))
        {
            return true;
        }

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);

            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        if (key.Length is 2 or 3
            && (key[0] == 'f' || key[0] == 'F')
            && int.TryParse(key.AsSpan(1), out var number)
            && number is >= 1 and <= 24)
        {
            virtualKey = (ushort)(0x70 + number - 1);   // VK_F1 = 0x70
            return true;
        }

        return false;
    }
}
