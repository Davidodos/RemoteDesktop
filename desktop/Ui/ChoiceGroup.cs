using System.Drawing.Drawing2D;

namespace RemoteDesktopClient.Ui;

/// <summary>
/// Eine Auswahl aus wenigen Möglichkeiten, jede mit einem Satz Erklärung.
///
/// <para>
/// Ein <c>RadioButton</c> kann nur eine Zeile Text. Genau die Erklärung
/// darunter ist hier aber die eigentliche Auskunft — „Heimnetz" sagt einem
/// niemandem etwas, „Handy und Rechner am selben Router" schon. Deshalb ist
/// eine Möglichkeit hier eine anklickbare Fläche mit zwei Zeilen und nicht ein
/// Kringel mit Beschriftung.
/// </para>
/// </summary>
/// <typeparam name="T">Womit die Auswahl im Programm bezeichnet wird.</typeparam>
public sealed class ChoiceGroup<T> : Control, IMeasurable
    where T : notnull
{
    private readonly List<Option> _options = [];

    private T? _chosen;
    private int _hovered = -1;

    public ChoiceGroup()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        Cursor = Cursors.Hand;
    }

    /// <summary>
    /// Wird nur beim Klicken gemeldet, nicht beim Setzen von außen — sonst
    /// löste jedes Anzeigen des gespeicherten Zustands ein Speichern aus.
    /// </summary>
    public event Action<T>? Chosen;

    public void Add(T value, string title, string explanation) =>
        _options.Add(new Option(value, title, explanation));

    public void Select(T value)
    {
        _chosen = value;
        Invalidate();
    }

    private int Gap => LogicalToDeviceUnits(8);

    private int Inset => LogicalToDeviceUnits(14);

    private int RowHeight(int width)
    {
        var text = width - Inset - LogicalToDeviceUnits(44);

        var explanation = TextRenderer.MeasureText(
            "Xg", Theme.Small, new Size(Math.Max(1, text), int.MaxValue),
            TextFormatFlags.WordBreak).Height;

        return LogicalToDeviceUnits(24) + explanation + (Inset * 2) - LogicalToDeviceUnits(6);
    }

    public int MeasureHeight(int width) =>
        _options.Count == 0
            ? 0
            : (_options.Count * RowHeight(width)) + ((_options.Count - 1) * Gap);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Parent is not null)
        {
            using var behind = new SolidBrush(Parent.BackColor);
            e.Graphics.FillRectangle(behind, ClientRectangle);
        }

        var height = RowHeight(Width);

        for (var index = 0; index < _options.Count; index++)
        {
            PaintOption(e.Graphics, _options[index], index, new Rectangle(
                0, index * (height + Gap), Width - 1, height));
        }
    }

    private void PaintOption(Graphics graphics, Option option, int index, Rectangle bounds)
    {
        var picked = _chosen is not null && _chosen.Equals(option.Value);

        Theme.FillRounded(
            graphics,
            bounds,
            Theme.ControlRadius,
            picked ? Theme.SurfaceRaised : _hovered == index ? Theme.SurfaceHover : Theme.Field,
            picked ? Theme.Accent : Theme.Border);

        var dot = LogicalToDeviceUnits(16);
        var left = bounds.X + Inset;

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var ring = new Pen(picked ? Theme.Accent : Theme.Border, 1.6f))
        {
            graphics.DrawEllipse(ring, left, bounds.Y + Inset + LogicalToDeviceUnits(3), dot, dot);
        }

        if (picked)
        {
            using var core = new SolidBrush(Theme.Accent);
            var shrink = LogicalToDeviceUnits(4);

            graphics.FillEllipse(
                core,
                left + shrink,
                bounds.Y + Inset + LogicalToDeviceUnits(3) + shrink,
                dot - (shrink * 2),
                dot - (shrink * 2));
        }

        graphics.SmoothingMode = previous;

        var textLeft = left + dot + LogicalToDeviceUnits(12);
        var textWidth = bounds.Right - textLeft - Inset;

        Theme.Draw(
            graphics, option.Title, Theme.BodyStrong, Theme.Text,
            new Rectangle(textLeft, bounds.Y + Inset, textWidth, LogicalToDeviceUnits(20)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);

        Theme.Draw(
            graphics, option.Explanation, Theme.Small, Theme.TextDim,
            new Rectangle(
                textLeft,
                bounds.Y + Inset + LogicalToDeviceUnits(20),
                textWidth,
                bounds.Bottom - bounds.Y - Inset - LogicalToDeviceUnits(20)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            | TextFormatFlags.NoPrefix);
    }

    private int At(int y)
    {
        var height = RowHeight(Width);
        var index = y / Math.Max(1, height + Gap);

        return index >= 0 && index < _options.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = At(e.Y);

        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var index = At(e.Y);

        if (index < 0)
        {
            return;
        }

        var value = _options[index].Value;

        if (_chosen is not null && _chosen.Equals(value))
        {
            return;
        }

        _chosen = value;
        Invalidate();
        Chosen?.Invoke(value);
    }

    private sealed record Option(T Value, string Title, string Explanation);
}
