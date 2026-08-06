namespace RemoteDesktopClient.Ui;

/// <summary>
/// Ein Absatz, der umbricht und dabei seine Höhe kennt.
///
/// <para>
/// <c>Label</c> kann entweder mitwachsen (<c>AutoSize</c>) oder umbrechen, aber
/// beides zusammen nur mit einer festen Breite, die man vorher wissen müsste.
/// Die Texte hier sind ganze deutsche Sätze und stehen in einem Fenster, dessen
/// Breite der Nutzer zieht — also muss die Höhe aus der Breite folgen und nicht
/// umgekehrt.
/// </para>
/// </summary>
public sealed class TextBlock : Control, IMeasurable
{
    private readonly TextFormatFlags _flags;

    public TextBlock(string text, Font? font = null, Color? color = null, bool centered = false)
    {
        _flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                 | (centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        Text = text;
        Font = font ?? Theme.Body;
        ForeColor = color ?? Theme.TextDim;
        TabStop = false;
    }

    public void Retext(string text)
    {
        if (Text == text)
        {
            return;
        }

        Text = text;

        // Ein längerer Text ist höher, und die Höhe kennt nur der Stapel weiter
        // oben. Ohne das stünde der neue Satz halb abgeschnitten da.
        Stack.Reflow(this);
        Invalidate();
    }

    public void Recolor(Color color)
    {
        ForeColor = color;
        Invalidate();
    }

    public int MeasureHeight(int width) => Text.Length == 0
        ? 0
        : TextRenderer.MeasureText(
            Text, Font, new Size(Math.Max(1, width), int.MaxValue), _flags).Height;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Parent is not null)
        {
            using var background = new SolidBrush(Parent.BackColor);
            e.Graphics.FillRectangle(background, ClientRectangle);
        }

        Theme.Draw(e.Graphics, Text, Font, ForeColor, ClientRectangle, _flags);
    }
}
