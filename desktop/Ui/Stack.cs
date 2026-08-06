namespace RemoteDesktopClient.Ui;

/// <summary>
/// Etwas, das seine Höhe aus der verfügbaren Breite ausrechnen kann.
/// </summary>
public interface IMeasurable
{
    int MeasureHeight(int width);
}

/// <summary>
/// Untereinander, mit gleichmäßigem Abstand — und bei Bedarf mit einem eigenen
/// Rollbalken.
///
/// <para>
/// **Warum nicht <c>FlowLayoutPanel</c> mit <c>AutoScroll</c>:** dessen
/// Rollbalken kommt vom System und ist hellgrau. In einem dunklen Fenster ist er
/// der eine Streifen, an dem man sofort sieht, dass hier jemand nur die Farben
/// überschrieben hat. Windows lässt ihn nicht einfärben — also zeichnet dieses
/// Programm ihn selbst.
/// </para>
///
/// <para>
/// Dasselbe Gerüst trägt beides: die Seiten (rollbar) und den Inhalt einer
/// Karte (nicht rollbar, gibt seine Höhe nach oben weiter). Zwei Bauteile für
/// zweimal dieselbe Aufgabe wären eine Stelle mehr, an der die Abstände
/// auseinanderlaufen.
/// </para>
/// </summary>
public sealed class Stack : Control, IMeasurable
{
    private readonly List<Item> _items = [];

    private int _offset;
    private int _content;
    private bool _arranging;
    private bool _dragging;
    private int _grabbedAt;
    private int _grabbedOffset;
    private bool _overBar;

    public Stack()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    // Kein eigenes BackColor — und das ist Absicht.
    //
    // WinForms erbt die Hintergrundfarbe vom übergeordneten Element, solange
    // keine eigene gesetzt ist. Genau das wird hier gebraucht: derselbe Stapel
    // trägt einmal eine ganze Seite (Fensterfarbe) und einmal den Inhalt einer
    // Karte (Kartenfarbe). Eine fest eingetragene Farbe malte im zweiten Fall
    // ein dunkles Rechteck über die Karte.

    /// <summary>Ob überstehender Inhalt gerollt werden darf oder die Höhe wächst.</summary>
    public bool Scrollable { get; init; }

    public int Gap { get; init; } = 14;

    private int BarWidth => LogicalToDeviceUnits(10);

    private bool NeedsBar => Scrollable && _content > ClientSize.Height;

    public void Add(Control child, int? gap = null)
    {
        _items.Add(new Item(child, gap));
        Controls.Add(child);
        Reflow(this);
    }

    /// <summary>
    /// Leert den Stapel **und entsorgt seinen Inhalt**. Das ist richtig für
    /// alles, was beim Anzeigen neu entsteht, und falsch für Felder, in die
    /// jemand gerade getippt hat — solche Steuerelemente gehören in einen
    /// eigenen Stapel, der stehen bleibt.
    /// </summary>
    public void Clear()
    {
        foreach (var item in _items)
        {
            item.Child.Dispose();
        }

        _items.Clear();
        Controls.Clear();
        _offset = 0;
        Reflow(this);
    }

    /// <summary>
    /// Neu anordnen — und zwar **von oben**.
    ///
    /// <para>
    /// Wächst der Inhalt einer Karte, ändert sich damit die Höhe der Karte, und
    /// die kennt nur der Stapel, in dem die Karte steckt. Ohne diesen Weg nach
    /// oben bliebe die Karte in ihrer alten Höhe stehen und schnitte ihren
    /// eigenen Inhalt ab.
    /// </para>
    /// </summary>
    public static void Reflow(Control? from)
    {
        var outermost = from as Stack;

        for (var parent = from?.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is Stack stack)
            {
                outermost = stack;
            }
        }

        outermost?.PerformLayout();
        outermost?.Invalidate(invalidateChildren: true);
    }

    /// <summary>
    /// Die Höhe, die dieser Stapel bei der gegebenen Breite bräuchte. Das ist
    /// dieselbe Rechnung wie beim Anordnen, nur ohne etwas zu verschieben.
    /// </summary>
    public int MeasureHeight(int width)
    {
        var inner = width - Padding.Horizontal;
        var height = Padding.Vertical;

        for (var index = 0; index < _items.Count; index++)
        {
            if (index > 0)
            {
                height += _items[index].Gap ?? Gap;
            }

            height += HeightOf(_items[index].Child, inner);
        }

        return height;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Arrange();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        Arrange();
    }

    /// <summary>
    /// Zwei Durchgänge, weil der Rollbalken Breite kostet: erst ohne ihn messen,
    /// und nur wenn der Inhalt dann immer noch übersteht, mit ihm neu anordnen.
    /// Andersherum bekäme jede knapp passende Seite einen Balken, den sie nicht
    /// braucht.
    /// </summary>
    private void Arrange()
    {
        if (_items.Count == 0)
        {
            _content = 0;
            return;
        }

        // Ein Kind zu verschieben lässt WinForms das übergeordnete Element neu
        // anordnen — also dieses hier. Ohne die Sperre riefe sich das Anordnen
        // aus sich selbst heraus auf.
        if (_arranging)
        {
            return;
        }

        _arranging = true;

        try
        {
            Place(ClientSize.Width - Padding.Horizontal);

            if (NeedsBar)
            {
                Place(ClientSize.Width - Padding.Horizontal - BarWidth);
            }
        }
        finally
        {
            _arranging = false;
        }

        Invalidate();
    }

    private void Place(int width)
    {
        var y = Padding.Top;

        for (var index = 0; index < _items.Count; index++)
        {
            if (index > 0)
            {
                y += _items[index].Gap ?? Gap;
            }

            var child = _items[index].Child;
            var height = HeightOf(child, width);

            child.SetBounds(Padding.Left, y - _offset, Math.Max(1, width), height);

            y += height;
        }

        _content = y + Padding.Bottom;

        var limit = Math.Max(0, _content - ClientSize.Height);

        if (_offset > limit)
        {
            _offset = limit;
        }
    }

    private static int HeightOf(Control child, int width) =>
        child is IMeasurable measurable ? measurable.MeasureHeight(width) : child.Height;

    // ---- Rollen ---------------------------------------------------------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Wheel(e.Delta);
    }

    /// <summary>
    /// Eine Radbewegung verarbeiten. Öffentlich, weil sie auch von
    /// <see cref="WheelRouter"/> kommt — Windows schickt das Rad an das Element
    /// mit dem Fokus, gemeint ist aber immer das unter dem Zeiger.
    /// </summary>
    /// <returns>Ob tatsächlich gerollt wurde.</returns>
    public bool Wheel(int delta) => ScrollBy(-delta / 2);

    private bool ScrollBy(int amount)
    {
        if (!NeedsBar)
        {
            return false;
        }

        var limit = Math.Max(0, _content - ClientSize.Height);
        var next = Math.Clamp(_offset + amount, 0, limit);

        if (next == _offset)
        {
            return false;
        }

        _offset = next;
        Arrange();

        return true;
    }

    private Rectangle Thumb()
    {
        var track = ClientSize.Height;
        var height = Math.Max(
            LogicalToDeviceUnits(40), (int)(track * (track / (float)_content)));

        var limit = Math.Max(1, _content - track);
        var top = (int)((track - height) * (_offset / (float)limit));

        return new Rectangle(
            ClientSize.Width - BarWidth + LogicalToDeviceUnits(3),
            top,
            BarWidth - LogicalToDeviceUnits(6),
            height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!NeedsBar)
        {
            return;
        }

        var thumb = Thumb();

        Theme.FillRounded(
            e.Graphics, thumb, thumb.Width / 2,
            _dragging || _overBar ? Theme.TextDim : Theme.Border);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!NeedsBar || e.X < ClientSize.Width - BarWidth)
        {
            return;
        }

        if (Thumb().Contains(e.Location))
        {
            _dragging = true;
            _grabbedAt = e.Y;
            _grabbedOffset = _offset;

            return;
        }

        // Neben den Balken geklickt: eine Seite weiter in diese Richtung.
        ScrollBy(e.Y < Thumb().Top ? -ClientSize.Height : ClientSize.Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var over = NeedsBar && e.X >= ClientSize.Width - BarWidth;

        if (over != _overBar)
        {
            _overBar = over;
            Invalidate();
        }

        if (!_dragging)
        {
            return;
        }

        var track = ClientSize.Height - Thumb().Height;

        if (track <= 0)
        {
            return;
        }

        var limit = Math.Max(0, _content - ClientSize.Height);
        var moved = (e.Y - _grabbedAt) * (limit / (float)track);

        var next = Math.Clamp(_grabbedOffset + (int)moved, 0, limit);

        if (next == _offset)
        {
            return;
        }

        _offset = next;
        Arrange();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _overBar = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private sealed record Item(Control Child, int? Gap);
}
