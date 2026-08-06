namespace RemoteDesktopClient.Ui;

/// <summary>
/// Ein abgesetzter Kasten mit Überschrift, Zustandspunkt und Inhalt.
///
/// <para>
/// Der Zustandspunkt ist kein Schmuck: er ist die einzige Stelle im Fenster, an
/// der man ohne Lesen erkennt, ob etwas läuft. Deshalb hat er drei Farben und
/// nicht fünf — grün, rot, grau.
/// </para>
///
/// <para>
/// Ein Hinweis zur Fläche: <see cref="Control.BackColor"/> ist der Ton der
/// Karte, damit Kinder darauf ihren Untergrund finden. Gezeichnet wird trotzdem
/// erst die Fensterfarbe und darüber das abgerundete Rechteck — sonst stünden
/// an den vier Ecken Zipfel in Kartenfarbe.
/// </para>
/// </summary>
public sealed class Card : Control, IMeasurable
{
    private readonly string _title;
    private readonly Stack _body = new() { Gap = 10 };

    private string? _state;
    private Color _stateColor = Theme.TextDim;

    public Card(string title)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        _title = title;
        BackColor = Theme.Surface;

        Controls.Add(_body);
    }

    /// <summary>Der Inhalt. Was hineinkommt, bestimmt die Seite.</summary>
    public Stack Body => _body;

    private int Inset => LogicalToDeviceUnits(18);

    private int HeaderHeight => _title.Length == 0 ? 0 : LogicalToDeviceUnits(30);

    public void ShowState(string? state, Color color)
    {
        _state = state;
        _stateColor = color;
        Invalidate();
    }

    public int MeasureHeight(int width) =>
        (Inset * 2) + HeaderHeight + _body.MeasureHeight(width - (Inset * 2));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var top = Inset + HeaderHeight;

        _body.SetBounds(
            Inset, top, Math.Max(1, Width - (Inset * 2)), Math.Max(1, Height - top - Inset));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Parent is not null)
        {
            using var behind = new SolidBrush(Parent.BackColor);
            e.Graphics.FillRectangle(behind, ClientRectangle);
        }

        Theme.FillRounded(
            e.Graphics,
            new Rectangle(0, 0, Width - 1, Height - 1),
            Theme.CardRadius,
            Theme.Surface,
            Theme.Border);

        if (HeaderHeight == 0)
        {
            return;
        }

        var header = new Rectangle(Inset, Inset, Width - (Inset * 2), HeaderHeight);

        Theme.Draw(e.Graphics, _title, Theme.CardTitle, Theme.Text, header,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);

        if (_state is null)
        {
            return;
        }

        var width = TextRenderer.MeasureText(_state, Theme.Small).Width;
        var dot = LogicalToDeviceUnits(8);
        var gap = LogicalToDeviceUnits(7);
        var right = header.Right;

        var label = new Rectangle(
            right - width, header.Y, width, LogicalToDeviceUnits(20));

        Theme.Draw(e.Graphics, _state, Theme.Small, _stateColor, label,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(_stateColor);

        e.Graphics.FillEllipse(
            brush, right - width - gap - dot, label.Y + ((label.Height - dot) / 2), dot, dot);

        e.Graphics.SmoothingMode = previous;
    }
}
