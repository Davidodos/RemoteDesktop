namespace RemoteDesktopClient.Ui;

/// <summary>
/// Ein Eingabefeld mit dunklem Grund und rundem Rahmen.
///
/// <para>
/// Ein <c>TextBox</c> lässt sich einfärben, aber nicht abrunden, und sein
/// Rahmen bleibt in jedem Fall der von Windows. Deshalb sitzt das echte Feld
/// randlos in diesem Wirt, und der Wirt zeichnet Fläche, Rahmen und die
/// Hervorhebung bei Fokus.
/// </para>
/// </summary>
public sealed class ThemedTextBox : Control, IMeasurable
{
    private readonly TextBox _input = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        Font = Theme.Body
    };

    private bool _focused;

    public ThemedTextBox(string? placeholder = null)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Field;

        if (placeholder is not null)
        {
            _input.PlaceholderText = placeholder;
        }

        _input.GotFocus += (_, _) => Mark(true);
        _input.LostFocus += (_, _) => Mark(false);
        _input.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);

        Controls.Add(_input);
        Height = LogicalToDeviceUnits(34);
    }

    /// <summary>Siehe <see cref="NavigationRail.OnHandleCreated"/> — dieselbe Rechnung.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Height = LogicalToDeviceUnits(34);
    }

    /// <summary>
    /// Bei jedem Zeichen. Die Einrichtung hängt daran, ob „Weiter" überhaupt
    /// anklickbar ist — ein Knopf, der erst nach dem Verlassen des Feldes
    /// aufwacht, sieht aus wie einer, der klemmt.
    /// </summary>
    public event EventHandler? ValueChanged;

    public string Value
    {
        get => _input.Text;
        set => _input.Text = value;
    }

    /// <summary>
    /// Der graue Beispieltext im leeren Feld. Nachträglich änderbar, weil
    /// dasselbe Feld je nach Netzmodus etwas anderes aufnimmt — im Heimnetz eine
    /// IP, bei Tailscale den Namen im Tailnet.
    /// </summary>
    public string Placeholder
    {
        get => _input.PlaceholderText;
        set => _input.PlaceholderText = value;
    }

    public bool ReadOnly
    {
        get => _input.ReadOnly;
        set => _input.ReadOnly = value;
    }

    public new bool Enabled
    {
        get => _input.Enabled;
        set
        {
            _input.Enabled = value;
            _input.ForeColor = value ? Theme.Text : Theme.TextDim;
            Invalidate();
        }
    }

    public void UseMonospace() => _input.Font = Theme.Mono;

    public int MeasureHeight(int width) => LogicalToDeviceUnits(34);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var inset = LogicalToDeviceUnits(11);

        _input.SetBounds(
            inset,
            (Height - _input.PreferredHeight) / 2,
            Math.Max(1, Width - (inset * 2)),
            _input.PreferredHeight);
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
            Theme.ControlRadius,
            Theme.Field,
            _focused ? Theme.Accent : Theme.Border);
    }

    private void Mark(bool focused)
    {
        _focused = focused;
        Invalidate();
    }
}
