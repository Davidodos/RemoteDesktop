using System.Drawing.Drawing2D;

namespace RemoteDesktopClient.Ui;

/// <summary>Die Seiten des Fensters. Mehr gibt es nicht — und keine davon fehlt.</summary>
public enum Page
{
    Overview,
    Remote,
    Devices,
    Network,
    Settings
}

/// <summary>
/// Die Seitenleiste: Zeichen, Seiten, Zustand des Agents.
///
/// <para>
/// **Sie ersetzt die Reiter.** Reiter sagen „diese drei Dinge gehören
/// zusammen"; eine Leiste sagt „das hier ist das Programm, und es hat fünf
/// Ansichten". Der Unterschied ist nicht Geschmack: die Fernsteuerung ist keine
/// Einstellung, und in einem Reiterband stünde sie so da.
/// </para>
///
/// <para>
/// Unten steht immer, ob der Agent läuft — auf jeder Seite, auch beim
/// Einstellen. Das ist die eine Angabe, wegen der man sonst zurückwechseln
/// müsste.
/// </para>
/// </summary>
public sealed class NavigationRail : Control
{
    private static readonly (Page Page, string Label)[] Items =
    [
        (Page.Overview, "Übersicht"),
        (Page.Remote, "Fernsteuerung"),
        (Page.Devices, "Geräte"),
        (Page.Network, "Netz"),
        (Page.Settings, "Einstellungen")
    ];

    private readonly Icon? _mark = Brand.Load(32);

    private Page _current = Page.Overview;
    private int _hovered = -1;
    private string _state = "Zustand wird geprüft…";
    private Color _stateColor = Theme.TextDim;

    public NavigationRail()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Rail;
        Cursor = Cursors.Hand;
        Width = LogicalToDeviceUnits(216);
    }

    /// <summary>
    /// Die Breite noch einmal, sobald die tatsächliche Auflösung feststeht: vor
    /// dem Fensterhandle rechnet <see cref="Control.LogicalToDeviceUnits(int)"/>
    /// mit 96 dpi, und bei 150 % wäre die Leiste zu schmal für ihre eigene
    /// Beschriftung.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Width = LogicalToDeviceUnits(216);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Width = LogicalToDeviceUnits(216);
    }

    public event Action<Page>? Picked;

    public void Highlight(Page page)
    {
        _current = page;
        Invalidate();
    }

    public void ShowAgent(string state, Color color)
    {
        _state = state;
        _stateColor = color;
        Invalidate();
    }

    private int RowHeight => LogicalToDeviceUnits(40);

    private int FirstRow => LogicalToDeviceUnits(84);

    private int Inset => LogicalToDeviceUnits(14);

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var background = new SolidBrush(Theme.Rail))
        {
            e.Graphics.FillRectangle(background, ClientRectangle);
        }

        using (var seam = new Pen(Theme.Border))
        {
            e.Graphics.DrawLine(seam, Width - 1, 0, Width - 1, Height);
        }

        PaintMark(e.Graphics);

        for (var index = 0; index < Items.Length; index++)
        {
            PaintItem(e.Graphics, index);
        }

        PaintAgent(e.Graphics);
    }

    private void PaintMark(Graphics graphics)
    {
        var size = LogicalToDeviceUnits(28);
        var top = LogicalToDeviceUnits(26);

        if (_mark is not null)
        {
            graphics.DrawIcon(_mark, new Rectangle(Inset, top, size, size));
        }

        Theme.Draw(
            graphics, "RemoteDesktop", Theme.CardTitle, Theme.Text,
            new Rectangle(
                Inset + size + LogicalToDeviceUnits(10), top, Width, size),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void PaintItem(Graphics graphics, int index)
    {
        var (page, label) = Items[index];
        var bounds = new Rectangle(
            LogicalToDeviceUnits(8),
            FirstRow + (index * RowHeight),
            Width - LogicalToDeviceUnits(17),
            RowHeight - LogicalToDeviceUnits(4));

        var chosen = page == _current;

        if (chosen || _hovered == index)
        {
            Theme.FillRounded(
                graphics, bounds, Theme.ControlRadius,
                chosen ? Theme.SurfaceRaised : Theme.SurfaceHover);
        }

        if (chosen)
        {
            // Der Streifen links ist die eigentliche Markierung. Eine bloße
            // Einfärbung verschwindet, sobald jemand den Bildschirm schräg
            // ansieht oder die Helligkeit herunterdreht.
            var previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var stripe = new SolidBrush(Theme.Accent);
            using var path = Theme.Rounded(
                new Rectangle(
                    bounds.X, bounds.Y + LogicalToDeviceUnits(7),
                    LogicalToDeviceUnits(3), bounds.Height - LogicalToDeviceUnits(14)),
                2);

            graphics.FillPath(stripe, path);
            graphics.SmoothingMode = previous;
        }

        Theme.Draw(
            graphics, label, chosen ? Theme.BodyStrong : Theme.Body,
            chosen ? Theme.Text : Theme.TextDim,
            new Rectangle(
                bounds.X + LogicalToDeviceUnits(16), bounds.Y,
                bounds.Width - LogicalToDeviceUnits(20), bounds.Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void PaintAgent(Graphics graphics)
    {
        var top = Height - LogicalToDeviceUnits(62);

        using (var seam = new Pen(Theme.Border))
        {
            graphics.DrawLine(seam, Inset, top, Width - Inset, top);
        }

        var dot = LogicalToDeviceUnits(8);
        var line = new Rectangle(
            Inset, top + LogicalToDeviceUnits(14), Width - (Inset * 2), LogicalToDeviceUnits(18));

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var brush = new SolidBrush(_stateColor))
        {
            graphics.FillEllipse(
                brush, line.X, line.Y + ((line.Height - dot) / 2), dot, dot);
        }

        graphics.SmoothingMode = previous;

        Theme.Draw(
            graphics, _state, Theme.Small, Theme.TextDim,
            new Rectangle(
                line.X + dot + LogicalToDeviceUnits(9), line.Y,
                line.Width - dot, line.Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    private int At(int y)
    {
        var index = (y - FirstRow) / RowHeight;

        return y >= FirstRow && index >= 0 && index < Items.Length ? index : -1;
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
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
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

        if (index >= 0)
        {
            Picked?.Invoke(Items[index].Page);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mark?.Dispose();
        }

        base.Dispose(disposing);
    }
}
