using RemoteDesktopClient.Ui;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Eine Seite im gemeinsamen Fenster: Überschrift, ein Satz darunter, Inhalt.
///
/// <para>
/// Der Satz unter der Überschrift ist Pflicht und nicht Zierde. Jede Seite muss
/// in einer Zeile sagen können, wofür sie da ist — geht das nicht, gehört ihr
/// Inhalt auf zwei Seiten.
/// </para>
///
/// <para>
/// **Eine Ausnahme, und die ist begründet:** die Übersicht. Ihr Inhalt *ist*
/// die Antwort auf „wofür ist diese Seite da" — ganz oben steht der Name dieses
/// Rechners, darunter, was auf ihm läuft. „Was auf diesem Rechner steht." dazu
/// zu schreiben wäre eine Erklärung, die länger ist als das, was sie erklärt.
/// Ein leerer Satz lässt die Zeile weg; der Inhalt rückt dann nach oben.
/// </para>
/// </summary>
public abstract class PageView : Control
{
    private readonly string _title;
    private readonly string _subtitle;

    protected PageView(string title, string subtitle)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        _title = title;
        _subtitle = subtitle;

        BackColor = Theme.Window;
        Body = new Stack { Scrollable = true, Padding = new Padding(0, 0, 0, 24) };

        Controls.Add(Body);
    }

    /// <summary>Wohin die Karten kommen.</summary>
    protected Stack Body { get; }

    /// <summary>Wohin Meldungen gehen. Setzt das Fenster beim Einhängen.</summary>
    public Action<string, Tone> Reporter { get; set; } = (_, _) => { };

    /// <summary>
    /// Eine Meldung in die Statuszeile. Der Normalfall ist eine reine Auskunft,
    /// deshalb ist <see cref="Tone.Neutral"/> die Vorgabe.
    /// </summary>
    protected void Report(string message, Tone tone = Tone.Neutral) =>
        Reporter(message, tone);

    /// <summary>
    /// Zustand neu erfragen und anzeigen. Wird bei jedem Seitenwechsel gerufen —
    /// eine Seite, die noch den Stand von vorhin zeigt, ist schlimmer als eine,
    /// die kurz leer ist.
    /// </summary>
    public virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>
    /// Ob das Fenster diese Seite von allein nachsehen lassen darf, solange sie
    /// offen ist.
    ///
    /// <para>
    /// Nur für Seiten, die einen fremden Zustand anzeigen — ob der Dienst läuft,
    /// welche Geräte gekoppelt sind. Auf Seiten mit Eingabefeldern wäre es
    /// schädlich: die Seite liest beim Nachsehen den gespeicherten Stand ein und
    /// überschriebe damit, was jemand gerade tippt.
    /// </para>
    /// </summary>
    public virtual bool LiveRefresh => false;

    private int Side => LogicalToDeviceUnits(28);

    /// <summary>Ohne Satz unter der Überschrift beginnt der Inhalt weiter oben.</summary>
    private int HeaderHeight => LogicalToDeviceUnits(_subtitle.Length == 0 ? 62 : 84);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        Body.SetBounds(
            Side,
            HeaderHeight,
            Math.Max(1, Width - (Side * 2)),
            Math.Max(1, Height - HeaderHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Theme.Window);
        e.Graphics.FillRectangle(background, ClientRectangle);

        Theme.Draw(
            e.Graphics, _title, Theme.PageTitle, Theme.Text,
            new Rectangle(Side, LogicalToDeviceUnits(26), Width - (Side * 2), LogicalToDeviceUnits(30)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);

        if (_subtitle.Length == 0)
        {
            return;
        }

        Theme.Draw(
            e.Graphics, _subtitle, Theme.Body, Theme.TextDim,
            new Rectangle(Side, LogicalToDeviceUnits(56), Width - (Side * 2), LogicalToDeviceUnits(22)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }
}
