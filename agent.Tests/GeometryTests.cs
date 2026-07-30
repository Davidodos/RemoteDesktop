using RemoteDesktopAgent.Native;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Koordinaten-Mapping. Hier falsch zu liegen heißt: jeder Klick landet
/// daneben — deshalb der ausführlichste Testblock im Projekt.
/// </summary>
public class GeometryTests
{
    private static readonly MonitorInfo Primary =
        new(0, 0, 0, 1920, 1080, true, @"\\.\DISPLAY1");

    /// <summary>Zweiter Monitor rechts daneben.</summary>
    private static readonly MonitorInfo Right =
        new(1, 1920, 0, 2560, 1440, false, @"\\.\DISPLAY2");

    /// <summary>Dritter Monitor links davon — negative Koordinaten.</summary>
    private static readonly MonitorInfo Left =
        new(2, -1920, 0, 1920, 1080, false, @"\\.\DISPLAY3");

    [Fact]
    public void BoundingBox_umschliesst_alle_Monitore()
    {
        // Arrange
        var monitors = new[] { Primary, Right, Left };

        // Act
        var desktop = Geometry.BoundingBox(monitors);

        // Assert
        Assert.Equal(-1920, desktop.X);
        Assert.Equal(0, desktop.Y);
        Assert.Equal(1920 + 1920 + 2560, desktop.Width);
        Assert.Equal(1440, desktop.Height);
    }

    [Fact]
    public void BoundingBox_wirft_bei_leerer_Liste()
    {
        Assert.Throws<ArgumentException>(() => Geometry.BoundingBox([]));
    }

    [Fact]
    public void ToAbsolute_bildet_linke_obere_Ecke_auf_Null_ab()
    {
        // Arrange
        var desktop = Geometry.BoundingBox([Primary]);

        // Act
        var (dx, dy) = Geometry.ToAbsolute(0.0, 0.0, Primary, desktop);

        // Assert
        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void ToAbsolute_bildet_rechte_untere_Ecke_auf_Maximum_ab()
    {
        // Arrange
        var desktop = Geometry.BoundingBox([Primary]);

        // Act
        var (dx, dy) = Geometry.ToAbsolute(1.0, 1.0, Primary, desktop);

        // Assert — die letzte Pixelspalte muss erreichbar sein.
        Assert.Equal(65535, dx);
        Assert.Equal(65535, dy);
    }

    [Fact]
    public void ToAbsolute_trifft_bei_Einzelmonitor_die_Mitte()
    {
        // Arrange
        var desktop = Geometry.BoundingBox([Primary]);

        // Act
        var (dx, dy) = Geometry.ToAbsolute(0.5, 0.5, Primary, desktop);

        // Assert
        Assert.InRange(dx, 32700, 32835);
        Assert.InRange(dy, 32700, 32835);
    }

    [Fact]
    public void ToAbsolute_trifft_den_zweiten_Monitor_statt_den_ersten()
    {
        // Arrange — zwei Monitore nebeneinander, gesamt 4480 breit.
        var monitors = new[] { Primary, Right };
        var desktop = Geometry.BoundingBox(monitors);

        // Act — Mitte des rechten Monitors liegt bei x = 1920 + 1280 = 3200.
        var (dx, _) = Geometry.ToAbsolute(0.5, 0.5, Right, desktop);

        // Assert
        var expected = (int)Math.Round(3200.0 * 65535 / (desktop.Width - 1));
        Assert.InRange(dx, expected - 2, expected + 2);
    }

    [Fact]
    public void ToAbsolute_kommt_mit_negativen_Monitorkoordinaten_klar()
    {
        // Arrange — der linke Monitor beginnt bei x = -1920.
        var monitors = new[] { Primary, Left };
        var desktop = Geometry.BoundingBox(monitors);

        // Act — linke Kante des linken Monitors ist der Ursprung des Desktops.
        var (dx, _) = Geometry.ToAbsolute(0.0, 0.0, Left, desktop);

        // Assert
        Assert.Equal(0, dx);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(42.0)]
    public void ToAbsolute_klemmt_Werte_ausserhalb_von_null_bis_eins(double outOfRange)
    {
        // Arrange
        var desktop = Geometry.BoundingBox([Primary]);

        // Act
        var (dx, dy) = Geometry.ToAbsolute(outOfRange, outOfRange, Primary, desktop);

        // Assert — nie außerhalb des gültigen Bereichs, egal was ankommt.
        Assert.InRange(dx, 0, 65535);
        Assert.InRange(dy, 0, 65535);
    }

    [Fact]
    public void ToAbsolute_ueberschreitet_nie_das_Maximum()
    {
        // Arrange — drei Monitore, ungleiche Größen.
        var monitors = new[] { Primary, Right, Left };
        var desktop = Geometry.BoundingBox(monitors);

        // Act & Assert — Raster über jeden Monitor, nichts darf ausbrechen.
        foreach (var monitor in monitors)
        {
            for (var i = 0; i <= 10; i++)
            {
                var (dx, dy) = Geometry.ToAbsolute(i / 10.0, i / 10.0, monitor, desktop);

                Assert.InRange(dx, 0, 65535);
                Assert.InRange(dy, 0, 65535);
            }
        }
    }
}
