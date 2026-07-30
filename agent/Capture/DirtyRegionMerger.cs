namespace RemoteDesktopAgent.Capture;

/// <summary>
/// Fasst die Änderungsrechtecke der Desktop Duplication API zu wenigen,
/// sinnvoll großen Ausschnitten zusammen.
///
/// Windows liefert beim Tippen gern 40 winzige Rechtecke pro Frame. Jedes
/// einzeln zu kodieren und zu senden kostet mehr Overhead (JPEG-Header,
/// WebSocket-Rahmen, ein <c>drawImage</c> pro Stück) als die paar gesparten
/// Pixel wert sind. Umgekehrt jedes Mal das Vollbild zu schicken verschwendet
/// Bandbreite, wenn sich nur der Mauszeiger bewegt hat.
/// </summary>
public static class DirtyRegionMerger
{
    /// <summary>JPEG-Blockraster. Siehe <see cref="CaptureRegion.AlignTo"/>.</summary>
    public const int Grid = 16;

    /// <summary>Mehr Ausschnitte lohnen sich pro Frame nicht.</summary>
    public const int MaxRegions = 8;

    /// <summary>
    /// Ab diesem Anteil geänderter Fläche ist ein Vollbild billiger als die
    /// Einzelstücke — ihre Ränder überlappen sich dann ohnehin größtenteils.
    /// </summary>
    private const double FullFrameThreshold = 0.6;

    /// <summary>
    /// Zwei Ausschnitte werden verschmolzen, solange die Hülle nicht mehr als
    /// diesen Faktor über ihrer Einzelfläche liegt.
    /// </summary>
    private const double MergeSlack = 1.5;

    /// <summary>
    /// Änderungsrechtecke → zu sendende Ausschnitte. Leere Eingabe ergibt eine
    /// leere Ausgabe: dann gibt es nichts zu senden.
    /// </summary>
    public static IReadOnlyList<CaptureRegion> Merge(
        IEnumerable<CaptureRegion> dirty, int width, int height)
    {
        var full = new CaptureRegion(0, 0, width, height);

        var regions = dirty
            .Select(r => r.Clamp(width, height))
            .Where(r => !r.IsEmpty)
            .Select(r => r.AlignTo(Grid, width, height))
            .ToList();

        if (regions.Count == 0)
        {
            return [];
        }

        MergeOverlapping(regions);

        // Nach dem Verschmelzen erneut prüfen: viele kleine Rechtecke über den
        // ganzen Schirm verteilt ergeben zusammen mehr Fläche als das Vollbild.
        if (regions.Count == 1 && regions[0].Area >= full.Area)
        {
            return [full];
        }

        if (regions.Sum(r => r.Area) >= full.Area * FullFrameThreshold)
        {
            return [full];
        }

        return regions;
    }

    /// <summary>
    /// Verschmilzt paarweise, solange es sich lohnt — und danach notfalls auch
    /// gegen den eigenen Willen, bis <see cref="MaxRegions"/> eingehalten ist.
    /// </summary>
    private static void MergeOverlapping(List<CaptureRegion> regions)
    {
        while (true)
        {
            var (a, b, cost) = FindCheapestPair(regions);

            if (a < 0)
            {
                return;
            }

            var worthIt = cost <= (regions[a].Area + regions[b].Area) * MergeSlack;

            if (!worthIt && regions.Count <= MaxRegions)
            {
                return;
            }

            regions[a] = regions[a].Union(regions[b]);
            regions.RemoveAt(b);
        }
    }

    /// <summary>Das Paar mit der kleinsten gemeinsamen Hülle; <c>(-1, -1, 0)</c> wenn keins übrig ist.</summary>
    private static (int A, int B, long Cost) FindCheapestPair(List<CaptureRegion> regions)
    {
        var best = (A: -1, B: -1, Cost: long.MaxValue);

        for (var i = 0; i < regions.Count; i++)
        {
            for (var j = i + 1; j < regions.Count; j++)
            {
                var cost = regions[i].Union(regions[j]).Area;

                if (cost < best.Cost)
                {
                    best = (i, j, cost);
                }
            }
        }

        return best.A < 0 ? (-1, -1, 0) : best;
    }
}
