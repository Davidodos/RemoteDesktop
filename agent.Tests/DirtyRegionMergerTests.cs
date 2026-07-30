using RemoteDesktopAgent.Capture;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Zusammenfassen der Änderungsrechtecke. Zu grob heißt Bandbreite verschenken,
/// zu fein heißt Overhead pro Ausschnitt — beides sieht man auf dem Handy.
/// </summary>
public class DirtyRegionMergerTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    [Fact]
    public void Ohne_Aenderung_gibt_es_nichts_zu_senden()
    {
        // Act
        var result = DirtyRegionMerger.Merge([], Width, Height);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Leere_Rechtecke_werden_verworfen()
    {
        // Arrange — Windows meldet gelegentlich entartete Rechtecke.
        var dirty = new[] { new CaptureRegion(100, 100, 0, 50) };

        // Act
        var result = DirtyRegionMerger.Merge(dirty, Width, Height);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Ein_kleiner_Bereich_bleibt_klein()
    {
        // Arrange
        var dirty = new[] { new CaptureRegion(200, 300, 64, 32) };

        // Act
        var region = Assert.Single(DirtyRegionMerger.Merge(dirty, Width, Height));

        // Assert
        Assert.Equal(new CaptureRegion(192, 288, 80, 48), region);
    }

    [Fact]
    public void Ausschnitte_liegen_auf_dem_JPEG_Raster()
    {
        // Arrange
        var dirty = new[] { new CaptureRegion(101, 203, 7, 9) };

        // Act
        var region = Assert.Single(DirtyRegionMerger.Merge(dirty, Width, Height));

        // Assert
        Assert.Equal(0, region.X % DirtyRegionMerger.Grid);
        Assert.Equal(0, region.Y % DirtyRegionMerger.Grid);
    }

    [Fact]
    public void Ausschnitte_reichen_nie_ueber_den_Bildrand()
    {
        // Arrange — ein Rechteck, das rechts unten übersteht.
        var dirty = new[] { new CaptureRegion(Width - 10, Height - 10, 200, 200) };

        // Act
        var region = Assert.Single(DirtyRegionMerger.Merge(dirty, Width, Height));

        // Assert
        Assert.Equal(Width, region.Right);
        Assert.Equal(Height, region.Bottom);
    }

    [Fact]
    public void Komplett_ausserhalb_liegende_Rechtecke_verschwinden()
    {
        // Arrange
        var dirty = new[] { new CaptureRegion(Width + 100, 0, 50, 50) };

        // Act
        var result = DirtyRegionMerger.Merge(dirty, Width, Height);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Benachbarte_Bereiche_werden_zusammengefasst()
    {
        // Arrange — zwei Zeilen Text direkt untereinander.
        var dirty = new[]
        {
            new CaptureRegion(100, 100, 200, 20),
            new CaptureRegion(100, 124, 200, 20)
        };

        // Act
        var region = Assert.Single(DirtyRegionMerger.Merge(dirty, Width, Height));

        // Assert
        Assert.True(region.Width >= 200);
        Assert.True(region.Bottom >= 144);
    }

    [Fact]
    public void Weit_auseinander_liegende_Bereiche_bleiben_getrennt()
    {
        // Arrange — links oben und rechts unten, dazwischen passiert nichts.
        var dirty = new[]
        {
            new CaptureRegion(0, 0, 100, 100),
            new CaptureRegion(1800, 980, 100, 100)
        };

        // Act
        var result = DirtyRegionMerger.Merge(dirty, Width, Height);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Nie_mehr_als_die_Obergrenze_an_Ausschnitten()
    {
        // Arrange — 20 verstreute kleine Änderungen, wie beim Tippen.
        var dirty = Enumerable.Range(0, 20)
            .Select(i => new CaptureRegion(i * 90, i * 50, 16, 16))
            .ToArray();

        // Act
        var result = DirtyRegionMerger.Merge(dirty, Width, Height);

        // Assert
        Assert.InRange(result.Count, 1, DirtyRegionMerger.MaxRegions);
    }

    [Fact]
    public void Grossflaechige_Aenderung_wird_zum_Vollbild()
    {
        // Arrange — ein Video im Vollbild ändert praktisch alles.
        var dirty = new[] { new CaptureRegion(0, 0, Width, Height) };

        // Act
        var region = Assert.Single(DirtyRegionMerger.Merge(dirty, Width, Height));

        // Assert
        Assert.Equal(new CaptureRegion(0, 0, Width, Height), region);
    }

    [Fact]
    public void Viele_grosse_Bereiche_ergeben_zusammen_das_Vollbild()
    {
        // Arrange — vier Viertel, einzeln je 25 % der Fläche.
        var dirty = new[]
        {
            new CaptureRegion(0, 0, Width / 2, Height / 2),
            new CaptureRegion(Width / 2, 0, Width / 2, Height / 2),
            new CaptureRegion(0, Height / 2, Width / 2, Height / 2),
            new CaptureRegion(Width / 2, Height / 2, Width / 2, Height / 2)
        };

        // Act
        var region = Assert.Single(DirtyRegionMerger.Merge(dirty, Width, Height));

        // Assert
        Assert.Equal(new CaptureRegion(0, 0, Width, Height), region);
    }

    [Fact]
    public void Ueberlappende_Bereiche_werden_nicht_doppelt_gesendet()
    {
        // Arrange
        var dirty = new[]
        {
            new CaptureRegion(100, 100, 200, 200),
            new CaptureRegion(150, 150, 200, 200)
        };

        // Act
        var result = DirtyRegionMerger.Merge(dirty, Width, Height);

        // Assert
        Assert.Single(result);
    }
}

public class CaptureRegionTests
{
    [Fact]
    public void Union_umschliesst_beide()
    {
        // Arrange
        var a = new CaptureRegion(10, 20, 30, 40);
        var b = new CaptureRegion(100, 0, 10, 10);

        // Act
        var union = a.Union(b);

        // Assert
        Assert.Equal(new CaptureRegion(10, 0, 100, 60), union);
    }

    [Fact]
    public void Clamp_schneidet_auf_die_Bildgroesse()
    {
        // Arrange
        var region = new CaptureRegion(-50, -50, 200, 200);

        // Act
        var clamped = region.Clamp(100, 100);

        // Assert
        Assert.Equal(new CaptureRegion(0, 0, 100, 100), clamped);
    }

    [Fact]
    public void Clamp_ergibt_leer_wenn_nichts_uebrig_bleibt()
    {
        // Arrange
        var region = new CaptureRegion(500, 500, 100, 100);

        // Act
        var clamped = region.Clamp(100, 100);

        // Assert
        Assert.True(clamped.IsEmpty);
    }

    [Fact]
    public void AlignTo_waechst_nach_aussen()
    {
        // Arrange
        var region = new CaptureRegion(17, 33, 3, 3);

        // Act
        var aligned = region.AlignTo(16, 1920, 1080);

        // Assert
        Assert.Equal(16, aligned.X);
        Assert.Equal(32, aligned.Y);
        Assert.True(aligned.Right >= region.Right);
        Assert.True(aligned.Bottom >= region.Bottom);
    }

    [Fact]
    public void AlignTo_sprengt_die_Bildgrenzen_nicht()
    {
        // Arrange — Höhe 1080 ist kein Vielfaches von 16.
        var region = new CaptureRegion(0, 1070, 100, 10);

        // Act
        var aligned = region.AlignTo(16, 1920, 1080);

        // Assert
        Assert.Equal(1080, aligned.Bottom);
    }
}
