namespace RemoteDesktopAgent.Capture;

/// <summary>Ein rechteckiger Bildausschnitt in Pixeln des Quellmonitors.</summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public long Area => (long)Width * Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Kleinstes Rechteck, das beide umschließt.</summary>
    public CaptureRegion Union(CaptureRegion other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);

        return new CaptureRegion(left, top, Math.Max(Right, other.Right) - left,
            Math.Max(Bottom, other.Bottom) - top);
    }

    /// <summary>Auf die Bildgrenzen beschneiden. Liegt nichts mehr drin, kommt ein leeres zurück.</summary>
    public CaptureRegion Clamp(int width, int height)
    {
        var left = Math.Clamp(X, 0, width);
        var top = Math.Clamp(Y, 0, height);

        return new CaptureRegion(
            left,
            top,
            Math.Clamp(Right, 0, width) - left,
            Math.Clamp(Bottom, 0, height) - top);
    }

    /// <summary>
    /// Auf ein Raster ausrichten — nach außen, damit nie ein Streifen des
    /// geänderten Bereichs abgeschnitten wird.
    ///
    /// JPEG komprimiert in 16×16-Blöcken (8×8 Luma, halbe Auflösung bei Chroma).
    /// Ein Ausschnitt, der mitten in einem Block anfängt, erzeugt an der Kante
    /// sichtbare Farbsäume gegenüber dem stehen gebliebenen Rest des Bildes.
    /// </summary>
    public CaptureRegion AlignTo(int grid, int width, int height)
    {
        var left = X / grid * grid;
        var top = Y / grid * grid;
        var right = Math.Min((Right + grid - 1) / grid * grid, width);
        var bottom = Math.Min((Bottom + grid - 1) / grid * grid, height);

        return new CaptureRegion(left, top, right - left, bottom - top);
    }
}
