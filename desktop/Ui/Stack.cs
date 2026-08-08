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
///
/// <para>
/// **Gemessen wird nur, wenn sich etwas geändert hat.** Bis v1.2.0 rechnete
/// jeder einzelne Schritt am Mausrad die ganze Seite neu durch — jeder Absatz
/// noch einmal durch <c>TextRenderer.MeasureText</c>, jede Karte samt Inhalt,
/// und das dreifach verschachtelt. Das war das Ruckeln. Jetzt merkt sich der
/// Stapel die Höhen zu einer Breite; ein Rollschritt verschiebt die Kinder nur
/// noch.
/// </para>
/// </summary>
public sealed class Stack : Control, IMeasurable
{
    /// <summary>
    /// Zeichnet das Fenster samt Kindern in einem Rutsch, statt jedes Kind für
    /// sich blinken zu lassen. Ohne das flackert ein Rollvorgang, weil jede
    /// verschobene Karte ihr eigenes Fenster neu zeichnet.
    /// </summary>
    private const int WsExComposited = 0x02000000;

    private readonly List<Item> _items = [];

    private int _offset;
    private int _content;
    private bool _arranging;
    private bool _dragging;
    private int _grabbedAt;
    private int _grabbedOffset;
    private bool _overBar;

    /// <summary>Zu welcher Breite die gemerkten Höhen gehören — <c>-1</c>: zu keiner.</summary>
    private int _laidOutWidth = -1;
    private int _measuredWidth = -1;
    private int _measuredHeight;

    /// <summary>Zu welcher Fenstergröße die letzte Anordnung passte.</summary>
    private Size _arrangedFor = Size.Empty;

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

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            // Nur der rollbare Stapel: in einer Karte wird nichts verschoben,
            // und jede zusätzliche Zeichenschicht kostet auch etwas.
            if (Scrollable)
            {
                parameters.ExStyle |= WsExComposited;
            }

            return parameters;
        }
    }

    public void Add(Control child, int? gap = null)
    {
        _items.Add(new Item(child, gap));
        Controls.Add(child);

        // Ein Kind, das nach dem Einhängen seine Höhe selbst ändert — ein
        // Eingabefeld tut das, sobald es sein Fenster bekommt —, macht die
        // gemerkten Höhen ungültig. Während des eigenen Anordnens nicht:
        // dort ist die Änderung ja gerade das Ergebnis der Rechnung.
        child.SizeChanged += (_, _) =>
        {
            if (!_arranging)
            {
                Reflow(child);
            }
        };

        Reflow(this);
    }

    /// <summary>
    /// Ein Kind ein- oder ausblenden — und zwar **ohne Lücke**.
    ///
    /// <para>
    /// <see cref="Control.Visible"/> allein genügt nicht: der Stapel rechnet
    /// weiter mit der Höhe des Kindes, und an seiner Stelle bliebe ein leerer
    /// Streifen stehen. Und abfragen lässt sich die Eigenschaft hier nicht —
    /// sie ist <c>false</c>, sobald irgendein übergeordnetes Element unsichtbar
    /// ist, und das trifft auf jede Seite zu, die gerade nicht vorne liegt.
    /// </para>
    /// </summary>
    public void Toggle(Control child, bool shown)
    {
        var item = _items.FirstOrDefault(entry => entry.Child == child);

        if (item is null || item.Shown == shown)
        {
            return;
        }

        item.Shown = shown;
        child.Visible = shown;

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
    ///
    /// <para>
    /// Unterwegs verwirft jeder Stapel seine gemerkten Höhen: was sich geändert
    /// hat, weiß nur der, bei dem es passiert ist — die darüber müssen es
    /// erfahren.
    /// </para>
    /// </summary>
    public static void Reflow(Control? from)
    {
        var outermost = from as Stack;

        (from as Stack)?.Forget();

        for (var parent = from?.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is Stack stack)
            {
                stack.Forget();
                outermost = stack;
            }
        }

        outermost?.PerformLayout();
        outermost?.Invalidate(invalidateChildren: true);
    }

    /// <summary>Die gemerkten Höhen wegwerfen — beim nächsten Bedarf neu gerechnet.</summary>
    private void Forget()
    {
        _laidOutWidth = -1;
        _measuredWidth = -1;
        _arrangedFor = Size.Empty;
    }

    /// <summary>
    /// Die Höhe, die dieser Stapel bei der gegebenen Breite bräuchte. Das ist
    /// dieselbe Rechnung wie beim Anordnen, nur ohne etwas zu verschieben.
    /// </summary>
    public int MeasureHeight(int width)
    {
        if (width == _measuredWidth)
        {
            return _measuredHeight;
        }

        var inner = width - Padding.Horizontal;
        var height = Padding.Vertical;
        var first = true;

        foreach (var item in _items.Where(entry => entry.Shown))
        {
            if (!first)
            {
                height += item.Gap ?? Gap;
            }

            first = false;
            height += HeightOf(item.Child, inner);
        }

        _measuredWidth = width;
        _measuredHeight = height;

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
            // Unveränderte Größe und unveränderter Inhalt heißt: die Höhen von
            // eben stimmen noch. Ohne diese Abkürzung misst jedes Anzeigen einer
            // Seite alles zweimal — einmal ohne und einmal mit Rollbalken.
            if (ClientSize != _arrangedFor)
            {
                Measure(ClientSize.Width - Padding.Horizontal);

                if (NeedsBar)
                {
                    Measure(ClientSize.Width - Padding.Horizontal - BarWidth);
                }

                _arrangedFor = ClientSize;
            }

            Place();
        }
        finally
        {
            _arranging = false;
        }

        Invalidate();
    }

    /// <summary>
    /// Wo jedes Kind hingehört und wie hoch es ist — zur gegebenen Breite
    /// einmal gerechnet und dann gemerkt. Verschoben wird hier nichts.
    /// </summary>
    private void Measure(int width)
    {
        if (width == _laidOutWidth)
        {
            return;
        }

        var y = Padding.Top;
        var first = true;

        foreach (var item in _items)
        {
            if (!item.Shown)
            {
                item.Y = y;
                item.Height = 0;

                continue;
            }

            if (!first)
            {
                y += item.Gap ?? Gap;
            }

            first = false;

            item.Y = y;
            item.Height = HeightOf(item.Child, width);

            y += item.Height;
        }

        _content = y + Padding.Bottom;
        _laidOutWidth = width;
    }

    /// <summary>Die gemessene Anordnung auf die Kinder anwenden.</summary>
    private void Place()
    {
        var width = Math.Max(1, _laidOutWidth);

        Clamp();

        foreach (var item in _items.Where(entry => entry.Shown))
        {
            item.Child.SetBounds(Padding.Left, item.Y - _offset, width, item.Height);
        }
    }

    /// <summary>
    /// Nur die Verschiebung anwenden — der einzige Handgriff beim Rollen.
    ///
    /// <para>
    /// <see cref="Control.SuspendLayout"/> ist hier kein Feinschliff: ohne ihn
    /// löst jedes verschobene Kind ein Neuanordnen dieses Stapels aus, und aus
    /// einem Rollschritt würden so viele, wie es Kinder gibt.
    /// </para>
    /// </summary>
    private void Shift()
    {
        _arranging = true;
        SuspendLayout();

        try
        {
            foreach (var item in _items.Where(entry => entry.Shown))
            {
                item.Child.Top = item.Y - _offset;
            }
        }
        finally
        {
            ResumeLayout(false);
            _arranging = false;
        }

        Invalidate();
    }

    private void Clamp()
    {
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
        Shift();

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
        Shift();
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

    /// <summary>Ein Kind samt seinem Platz im Stapel. Veränderlich, weil gemerkt.</summary>
    private sealed class Item(Control child, int? gap)
    {
        public Control Child { get; } = child;

        public int? Gap { get; } = gap;

        public int Y { get; set; }

        public int Height { get; set; }

        /// <summary>Ob es mitgerechnet wird. Siehe <see cref="Stack.Toggle"/>.</summary>
        public bool Shown { get; set; } = true;
    }
}
