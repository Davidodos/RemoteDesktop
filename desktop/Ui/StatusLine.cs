namespace RemoteDesktopClient.Ui;

/// <summary>Wie eine Meldung gemeint ist.</summary>
public enum Tone
{
    Neutral,
    Working,
    Good,
    Bad
}

/// <summary>
/// Die Zeile am unteren Rand: was gerade passiert ist.
///
/// <para>
/// Sie ersetzt die Meldungsfenster. Ein Hinweis, der ein Fenster aufmacht,
/// unterbricht — und muss anschließend weggeklickt werden, auch wenn er nur
/// „Gespeichert." lautete. Hier steht er da, bis das Nächste passiert, und
/// hält niemanden auf.
/// </para>
/// </summary>
public sealed class StatusLine : Control
{
    private Tone _tone = Tone.Neutral;

    public StatusLine()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Rail;
        Height = LogicalToDeviceUnits(34);
        Text = string.Empty;
    }

    /// <summary>Siehe <see cref="NavigationRail.OnHandleCreated"/> — dieselbe Rechnung.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Height = LogicalToDeviceUnits(34);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = LogicalToDeviceUnits(34);
    }

    public void Say(string message, Tone tone = Tone.Neutral)
    {
        Text = message;
        _tone = tone;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var background = new SolidBrush(Theme.Rail))
        {
            e.Graphics.FillRectangle(background, ClientRectangle);
        }

        using (var seam = new Pen(Theme.Border))
        {
            e.Graphics.DrawLine(seam, 0, 0, Width, 0);
        }

        if (Text.Length == 0)
        {
            return;
        }

        var color = _tone switch
        {
            Tone.Good => Theme.Online,
            Tone.Bad => Theme.Danger,
            Tone.Working => Theme.Accent,
            _ => Theme.TextDim
        };

        Theme.Draw(
            e.Graphics, Text, Theme.Body, color,
            new Rectangle(
                LogicalToDeviceUnits(20), 0,
                Width - LogicalToDeviceUnits(40), Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }
}
