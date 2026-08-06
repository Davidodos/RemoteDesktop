namespace RemoteDesktopClient.Ui;

/// <summary>
/// Nebeneinander in einer Zeile — die beiden Fälle, die in diesem Fenster
/// vorkommen.
///
/// <para>
/// <see cref="Buttons"/> reiht Knöpfe von links auf und bricht um, wenn die
/// Breite nicht reicht. Das ist kein Luxus: die Beschriftungen sind deutsche
/// Sätze, und bei größerer Systemschrift passen sonst zwei davon nicht mehr
/// nebeneinander.
/// </para>
///
/// <para>
/// <see cref="Fill"/> ist die Zeile aus Eingabefeld und Knopf: das Feld nimmt,
/// was übrig bleibt.
/// </para>
/// </summary>
public sealed class Row : Control, IMeasurable
{
    private readonly Control[] _children;
    private readonly bool _wraps;

    private Row(Control[] children, bool wraps)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);

        _children = children;
        _wraps = wraps;

        Controls.AddRange(children);
    }

    public static Row Buttons(params Control[] children) => new(children, wraps: true);

    public static Row Fill(params Control[] children) => new(children, wraps: false);

    private int Gap => LogicalToDeviceUnits(8);

    public int MeasureHeight(int width) => Arrange(width, move: false);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Arrange(Width, move: true);
    }

    /// <summary>
    /// Messen und Anordnen in einer Rechnung. Zwei getrennte Verfahren wären
    /// zwei Stellen, an denen dieselbe Zeile unterschiedlich breit ausfällt.
    /// </summary>
    private int Arrange(int width, bool move)
    {
        if (_children.Length == 0)
        {
            return 0;
        }

        if (!_wraps)
        {
            var fixedWidth = _children.Skip(1).Sum(child => child.Width + Gap);
            var first = Math.Max(LogicalToDeviceUnits(60), width - fixedWidth);

            var widths = _children
                .Select((child, index) => index == 0 ? first : child.Width)
                .ToArray();

            // Erst die Breiten, dann die Höhen: ein umbrechender Absatz links
            // ist genau so hoch, wie der Platz neben dem Knopf es zulässt.
            var heights = _children
                .Select((child, index) => HeightOf(child, widths[index]))
                .ToArray();

            var tallest = heights.Max();

            if (move)
            {
                var x = 0;

                for (var index = 0; index < _children.Length; index++)
                {
                    _children[index].SetBounds(
                        x, (tallest - heights[index]) / 2, widths[index], heights[index]);

                    x += widths[index] + Gap;
                }
            }

            return tallest;
        }

        var line = _children.Max(child => child.Height);
        var left = 0;
        var top = 0;

        foreach (var child in _children)
        {
            if (left > 0 && left + child.Width > width)
            {
                left = 0;
                top += line + Gap;
            }

            if (move)
            {
                child.SetBounds(left, top, child.Width, child.Height);
            }

            left += child.Width + Gap;
        }

        return top + line;
    }

    private static int HeightOf(Control child, int width) =>
        child is IMeasurable measurable ? measurable.MeasureHeight(width) : child.Height;
}
