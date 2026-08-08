using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RemoteDesktopClient.Ui;

/// <summary>
/// Farben, Schriften und Maße der Oberfläche — an genau einer Stelle.
///
/// <para>
/// **Die Farben sind nicht erfunden.** Es sind dieselben, die die React-App in
/// <c>app/src/styles.css</c> benutzt. Das ist der Grund, warum das eingebettete
/// Fernsteuerbild nicht wie ein Fremdkörper im Fenster sitzt: es *ist* dieselbe
/// Oberfläche, nur in einem anderen Rahmen. Ändert sich dort etwas, gehört es
/// hier nachgezogen — und umgekehrt.
/// </para>
///
/// <para>
/// Was hier bewusst fehlt: eine Warnfarbe. Die App kennt drei Zustandsfarben
/// (gut, schlecht, egal) und kommt damit aus. Eine vierte einzuführen hieße,
/// überall neu zu entscheiden, was „mittelschlimm" ist.
/// </para>
/// </summary>
public static class Theme
{
    public static readonly Color Window = Hex("101418");
    public static readonly Color Rail = Hex("161c24");
    public static readonly Color Surface = Hex("1a2027");
    public static readonly Color SurfaceRaised = Hex("232b34");
    public static readonly Color SurfaceHover = Hex("202832");
    public static readonly Color Field = Hex("141a21");
    public static readonly Color Border = Hex("2f3945");
    public static readonly Color Text = Hex("e8edf2");
    public static readonly Color TextDim = Hex("93a1b0");
    public static readonly Color Accent = Hex("4a9eff");
    public static readonly Color AccentHover = Hex("6bb0ff");
    public static readonly Color AccentPressed = Hex("3a86e0");

    /// <summary>Schrift *auf* der Akzentfarbe. Weiß darauf wäre kaum lesbar.</summary>
    public static readonly Color OnAccent = Hex("0d1117");

    public static readonly Color Online = Hex("3ddc84");
    public static readonly Color Danger = Hex("ff5c5c");

    /// <summary>
    /// Weder gut noch schlimm: etwas läuft, tut aber nicht, was es soll. Diese
    /// Farbe steht auch in <c>app/src/styles.css</c> als <c>--warn</c>.
    /// </summary>
    public static readonly Color Warn = Hex("ffb454");

    public const int CardRadius = 12;
    public const int ControlRadius = 8;

    public static Font Body { get; } = Face(9.75f);
    public static Font BodyStrong { get; } = Face(9.75f, semibold: true);
    public static Font Small { get; } = Face(8.75f);
    public static Font CardTitle { get; } = Face(12f, semibold: true);
    public static Font PageTitle { get; } = Face(17f, semibold: true);

    /// <summary>
    /// Für Fingerabdrücke und Kopplungscodes. Eine Schrift mit festen
    /// Zeichenbreiten ist hier kein Geschmack, sondern Voraussetzung: beides
    /// wird Zeichen für Zeichen mit einem zweiten Bildschirm verglichen.
    /// </summary>
    public static Font Mono { get; } = new(
        Installed("Cascadia Mono", "Consolas", "Courier New"), 9.5f);

    public static Font Code { get; } = new(
        Installed("Cascadia Mono", "Consolas", "Courier New"), 26f, FontStyle.Bold);

    /// <summary>
    /// Ein abgerundetes Rechteck. Der Rundungsradius wird begrenzt, weil ein
    /// Radius größer als die halbe Kante sonst eine Figur ergibt, die sich
    /// selbst überschlägt.
    /// </summary>
    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var limit = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        var size = limit * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, size, size, 180, 90);
        path.AddArc(bounds.Right - size - 1, bounds.Y, size, size, 270, 90);
        path.AddArc(bounds.Right - size - 1, bounds.Bottom - size - 1, size, size, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - size - 1, size, size, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>Fläche und Rand in einem — der Normalfall in diesem Programm.</summary>
    public static void FillRounded(
        Graphics graphics, Rectangle bounds, int radius, Color fill, Color? border = null)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(fill);

        graphics.FillPath(brush, path);

        if (border is { } line)
        {
            using var pen = new Pen(line);
            graphics.DrawPath(pen, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Text so zeichnen, wie WinForms es sonst auch tut — über TextRenderer und
    /// nicht über <c>Graphics.DrawString</c>. Der Unterschied ist die
    /// Rasterung: GDI+ zeichnet Text weich und auf dunklem Grund sichtbar
    /// matschig, GDI mit ClearType so wie jedes andere Fenster.
    /// </summary>
    public static void Draw(
        Graphics graphics,
        string text,
        Font font,
        Color color,
        Rectangle bounds,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter) =>
        TextRenderer.DrawText(graphics, text, font, bounds, color, flags);

    private static Color Hex(string value) => Color.FromArgb(
        255,
        Convert.ToInt32(value[..2], 16),
        Convert.ToInt32(value.Substring(2, 2), 16),
        Convert.ToInt32(value.Substring(4, 2), 16));

    /// <summary>
    /// „Segoe UI Semibold" gibt es seit Windows 7, „Segoe UI Variable" erst seit
    /// Windows 11. Statt eine davon vorauszusetzen wird genommen, was da ist —
    /// eine fehlende Schriftfamilie ersetzt Windows sonst wortlos durch
    /// Microsoft Sans Serif, und das Fenster sähe zwanzig Jahre alt aus.
    /// </summary>
    private static Font Face(float size, bool semibold = false) => semibold
        ? new Font(Installed("Segoe UI Semibold", "Segoe UI", "Tahoma"), size)
        : new Font(Installed("Segoe UI Variable Text", "Segoe UI", "Tahoma"), size);

    private static string Installed(params string[] candidates)
    {
        using var families = new InstalledFontCollection();

        var names = families.Families.Select(family => family.Name).ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        return candidates.FirstOrDefault(names.Contains) ?? FontFamily.GenericSansSerif.Name;
    }
}
