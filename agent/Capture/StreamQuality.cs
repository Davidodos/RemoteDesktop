namespace RemoteDesktopAgent.Capture;

/// <summary>Eine Kombination aus JPEG-Qualität und Auflösungsfaktor.</summary>
public readonly record struct QualityLevel(int Quality, double Scale);

/// <summary>Was die App an Qualität anfordern kann.</summary>
public enum QualityMode
{
    /// <summary>Regelt sich selbst nach der gemessenen Frame-Dauer.</summary>
    Auto,
    High,
    Medium,
    Low
}

/// <summary>
/// Regelt Qualität und Auflösung nach der Zeit, die ein Frame tatsächlich
/// gekostet hat (Kodieren + Senden).
///
/// Die Sendedauer ist der ehrlichste Indikator, den wir hier haben: der
/// WebSocket blockt, sobald der Sendepuffer voll ist, und das passiert genau
/// dann, wenn die Mobilfunkverbindung nicht mehr hinterherkommt. Eine echte
/// Bandbreitenmessung bräuchte Rückmeldungen der App und wäre für den Zweck
/// überdimensioniert.
/// </summary>
public sealed class StreamQuality
{
    /// <summary>Von gut nach sparsam. Erst Qualität senken, dann Auflösung.</summary>
    private static readonly QualityLevel[] Levels =
    [
        new(85, 1.0),
        new(75, 1.0),
        new(65, 1.0),
        new(60, 0.75),
        new(50, 0.6),
        new(40, 0.5)
    ];

    /// <summary>Feste Stufen für die manuellen Modi.</summary>
    private const int HighIndex = 0;
    private const int MediumIndex = 2;
    private const int LowIndex = 5;

    /// <summary>
    /// So viele Frames am Stück müssen deutlich unter dem Budget bleiben, bevor
    /// wieder hochgeschaltet wird — sonst pendelt die Qualität sichtbar.
    /// </summary>
    private const int FramesBeforeUpgrade = 45;

    /// <summary>Verbraucht ein Frame mehr als das, ist die Leitung überlastet.</summary>
    private const double OverBudgetFactor = 1.2;

    /// <summary>Und darunter ist offensichtlich Luft nach oben.</summary>
    private const double UnderBudgetFactor = 0.5;

    private int _index = 1;
    private int _goodFrames;

    public QualityMode Mode { get; private set; } = QualityMode.Auto;

    public QualityLevel Current => Levels[Mode switch
    {
        QualityMode.High => HighIndex,
        QualityMode.Medium => MediumIndex,
        QualityMode.Low => LowIndex,
        _ => _index
    }];

    public void SetMode(QualityMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        _goodFrames = 0;

        // Beim Zurückschalten auf Auto dort weitermachen, wo der manuelle Modus
        // stand — sonst springt das Bild einmal sichtbar.
        if (mode != QualityMode.Auto)
        {
            _index = mode switch
            {
                QualityMode.High => HighIndex,
                QualityMode.Medium => MediumIndex,
                _ => LowIndex
            };
        }
    }

    /// <summary>Meldet die Dauer eines gesendeten Frames gegen das Zeitbudget pro Frame.</summary>
    public void Report(TimeSpan frameCost, TimeSpan budget)
    {
        if (Mode != QualityMode.Auto || budget <= TimeSpan.Zero)
        {
            return;
        }

        if (frameCost > budget * OverBudgetFactor)
        {
            _index = Math.Min(_index + 1, Levels.Length - 1);
            _goodFrames = 0;
            return;
        }

        if (frameCost > budget * UnderBudgetFactor)
        {
            // Im Zielkorridor: nichts ändern, aber auch nicht auf ein Hochstufen
            // hinarbeiten.
            _goodFrames = 0;
            return;
        }

        if (++_goodFrames >= FramesBeforeUpgrade)
        {
            _index = Math.Max(_index - 1, 0);
            _goodFrames = 0;
        }
    }
}
