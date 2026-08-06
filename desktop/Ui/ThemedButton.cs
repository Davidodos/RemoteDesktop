namespace RemoteDesktopClient.Ui;

/// <summary>Wie wichtig ein Knopf ist — mehr Abstufungen braucht es nicht.</summary>
public enum ButtonTone
{
    /// <summary>Der eine Handgriff, der auf dieser Karte gemeint ist.</summary>
    Primary,

    /// <summary>Alles Weitere, das danebensteht.</summary>
    Secondary,

    /// <summary>Etwas, das man nicht versehentlich anklicken soll.</summary>
    Danger
}

/// <summary>
/// Ein Knopf, der zur restlichen Oberfläche passt.
///
/// <para>
/// Selbst gezeichnet und nicht der Windows-Knopf mit anderer Farbe: ein
/// <c>Button</c> mit <c>FlatStyle.Flat</c> behält seinen hellen Rand beim
/// Fokussieren und blitzt beim Klicken in Systemfarben auf. Auf dunklem Grund
/// sieht man beides sofort.
/// </para>
/// </summary>
public sealed class ThemedButton : Control
{
    private readonly ButtonTone _tone;

    private bool _hover;
    private bool _pressed;

    public ThemedButton(string text, ButtonTone tone = ButtonTone.Secondary)
    {
        _tone = tone;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);

        Text = text;
        Font = Theme.BodyStrong;
        Cursor = Cursors.Hand;
        TabStop = true;
        AutoSize = false;
        Height = LogicalToDeviceUnits(34);
        Width = Measure();
    }

    /// <summary>
    /// Breite aus dem Text statt aus einer festen Zahl: die Beschriftungen sind
    /// deutsch und ganze Sätze („Tailscale herunterladen"), und bei größerer
    /// Systemschrift wachsen sie noch einmal.
    /// </summary>
    public int Measure() =>
        TextRenderer.MeasureText(Text, Font).Width + LogicalToDeviceUnits(32);

    public void Relabel(string text)
    {
        Text = text;
        Remeasure();
        Invalidate();
    }

    /// <summary>
    /// Maße neu ausrechnen.
    ///
    /// <para>
    /// Nötig, weil <see cref="Control.LogicalToDeviceUnits(int)"/> vor dem
    /// Erzeugen des Fensterhandles noch von 96 dpi ausgeht. Auf einem Bildschirm
    /// mit 150 % wüchse sonst die Schrift, der Knopf aber nicht — und der Text
    /// stünde über den Rand hinaus.
    /// </para>
    /// </summary>
    private void Remeasure()
    {
        Height = LogicalToDeviceUnits(34);
        Width = Measure();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Remeasure();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Remeasure();
        Stack.Reflow(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var (fill, border, text) = Palette();

        if (Parent is not null)
        {
            using var background = new SolidBrush(Parent.BackColor);
            e.Graphics.FillRectangle(background, ClientRectangle);
        }

        Theme.FillRounded(e.Graphics, bounds, Theme.ControlRadius, fill, border);

        Theme.Draw(
            e.Graphics, Text, Font, Enabled ? text : Theme.TextDim, bounds,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis);

        if (Focused)
        {
            // Ein Fokusrahmen *innerhalb* des Knopfs, damit man mit der Tastatur
            // sieht, wo man ist, ohne dass der Knopf dabei wächst.
            var inner = Rectangle.Inflate(bounds, -3, -3);
            Theme.FillRounded(e.Graphics, inner, Theme.ControlRadius - 3, fill, Theme.Accent);

            Theme.Draw(
                e.Graphics, Text, Font, Enabled ? text : Theme.TextDim, bounds,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis);
        }
    }

    private (Color Fill, Color Border, Color Text) Palette()
    {
        if (!Enabled)
        {
            return (Theme.Surface, Theme.Border, Theme.TextDim);
        }

        return _tone switch
        {
            ButtonTone.Primary => (
                _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent,
                _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent,
                Theme.OnAccent),

            ButtonTone.Danger => (
                _pressed || _hover ? Theme.SurfaceRaised : Theme.Surface,
                Theme.Danger,
                Theme.Danger),

            _ => (
                _pressed ? Theme.Surface : _hover ? Theme.SurfaceHover : Theme.SurfaceRaised,
                Theme.Border,
                Theme.Text)
        };
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Focus();
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    /// <summary>Leertaste und Eingabetaste sollen denselben Klick auslösen.</summary>
    protected override bool IsInputKey(Keys key) =>
        key is Keys.Space or Keys.Enter || base.IsInputKey(key);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
        }

        base.OnKeyDown(e);
    }
}
