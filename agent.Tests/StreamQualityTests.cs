using RemoteDesktopAgent.Capture;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die Qualitätsregelung. Sie darf nicht pendeln — ein Bild, das im Sekundentakt
/// zwischen scharf und matschig springt, ist unangenehmer als ein dauerhaft
/// mittelmäßiges.
/// </summary>
public class StreamQualityTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(33);

    [Fact]
    public void Startet_im_Auto_Modus()
    {
        // Act
        var quality = new StreamQuality();

        // Assert
        Assert.Equal(QualityMode.Auto, quality.Mode);
    }

    [Fact]
    public void Ueberschrittenes_Budget_senkt_die_Qualitaet()
    {
        // Arrange
        var quality = new StreamQuality();
        var before = quality.Current;

        // Act
        quality.Report(Budget * 3, Budget);

        // Assert
        Assert.True(quality.Current.Quality < before.Quality);
    }

    [Fact]
    public void Anhaltende_Ueberlastung_senkt_auch_die_Aufloesung()
    {
        // Arrange
        var quality = new StreamQuality();

        // Act
        for (var i = 0; i < 10; i++)
        {
            quality.Report(Budget * 3, Budget);
        }

        // Assert
        Assert.True(quality.Current.Scale < 1.0);
    }

    [Fact]
    public void Die_schlechteste_Stufe_ist_die_Untergrenze()
    {
        // Arrange
        var quality = new StreamQuality();

        for (var i = 0; i < 50; i++)
        {
            quality.Report(Budget * 10, Budget);
        }

        var floor = quality.Current;

        // Act
        quality.Report(Budget * 10, Budget);

        // Assert
        Assert.Equal(floor, quality.Current);
        Assert.True(floor.Quality > 0);
        Assert.True(floor.Scale > 0);
    }

    [Fact]
    public void Ein_einzelnes_schnelles_Bild_hebt_die_Qualitaet_noch_nicht()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.Report(Budget * 3, Budget);
        var lowered = quality.Current;

        // Act
        quality.Report(TimeSpan.Zero, Budget);

        // Assert — sonst pendelt die Regelung im Sekundentakt.
        Assert.Equal(lowered, quality.Current);
    }

    [Fact]
    public void Dauerhaft_schnelle_Bilder_heben_die_Qualitaet_wieder()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.Report(Budget * 3, Budget);
        var lowered = quality.Current;

        // Act
        for (var i = 0; i < 100; i++)
        {
            quality.Report(TimeSpan.Zero, Budget);
        }

        // Assert
        Assert.True(quality.Current.Quality > lowered.Quality);
    }

    [Fact]
    public void Ein_Ausreisser_setzt_den_Aufstieg_zurueck()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.Report(Budget * 3, Budget);
        var lowered = quality.Current;

        // Act — fast genug für ein Hochstufen, dann ein langsames Bild.
        for (var i = 0; i < 40; i++)
        {
            quality.Report(TimeSpan.Zero, Budget);
        }

        quality.Report(Budget, Budget);

        for (var i = 0; i < 20; i++)
        {
            quality.Report(TimeSpan.Zero, Budget);
        }

        // Assert
        Assert.Equal(lowered, quality.Current);
    }

    [Fact]
    public void Fester_Modus_ignoriert_die_Messung()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.SetMode(QualityMode.High);
        var high = quality.Current;

        // Act
        for (var i = 0; i < 50; i++)
        {
            quality.Report(Budget * 10, Budget);
        }

        // Assert
        Assert.Equal(high, quality.Current);
    }

    [Fact]
    public void Niedriger_Modus_liefert_die_sparsamste_Stufe()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.SetMode(QualityMode.High);
        var high = quality.Current;

        // Act
        quality.SetMode(QualityMode.Low);

        // Assert
        Assert.True(quality.Current.Quality < high.Quality);
        Assert.True(quality.Current.Scale < high.Scale);
    }

    [Fact]
    public void Zurueck_auf_Auto_startet_bei_der_zuletzt_festen_Stufe()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.SetMode(QualityMode.Low);
        var low = quality.Current;

        // Act
        quality.SetMode(QualityMode.Auto);

        // Assert — sonst springt das Bild beim Umschalten sichtbar.
        Assert.Equal(low, quality.Current);
    }

    [Fact]
    public void Gleicher_Modus_setzt_die_Regelung_nicht_zurueck()
    {
        // Arrange
        var quality = new StreamQuality();
        quality.Report(Budget * 3, Budget);
        var lowered = quality.Current;

        // Act — die Bildschleife meldet den Modus vor jedem Bild erneut.
        for (var i = 0; i < 100; i++)
        {
            quality.SetMode(QualityMode.Auto);
            quality.Report(TimeSpan.Zero, Budget);
        }

        // Assert
        Assert.True(quality.Current.Quality > lowered.Quality);
    }

    [Fact]
    public void Ohne_Budget_passiert_nichts()
    {
        // Arrange
        var quality = new StreamQuality();
        var before = quality.Current;

        // Act
        quality.Report(TimeSpan.FromSeconds(1), TimeSpan.Zero);

        // Assert
        Assert.Equal(before, quality.Current);
    }
}

public class FrameHeaderTests
{
    [Fact]
    public void Geschrieben_und_gelesen_ergibt_dasselbe()
    {
        // Arrange
        var region = new CaptureRegion(1920, 1000, 640, 480);
        var buffer = new byte[FrameHeader.Size];

        // Act
        FrameHeader.Write(buffer, region);

        // Assert
        Assert.Equal(region, FrameHeader.Read(buffer));
    }

    [Fact]
    public void Header_ist_acht_Bytes_gross()
    {
        // Arrange
        var buffer = new byte[FrameHeader.Size];

        // Act
        FrameHeader.Write(buffer, new CaptureRegion(0, 0, 1, 1));

        // Assert
        Assert.Equal(8, buffer.Length);
    }

    [Fact]
    public void Little_Endian_wie_im_Browser()
    {
        // Arrange
        var buffer = new byte[FrameHeader.Size];

        // Act — 0x0102 = 258
        FrameHeader.Write(buffer, new CaptureRegion(258, 0, 0, 0));

        // Assert
        Assert.Equal(0x02, buffer[0]);
        Assert.Equal(0x01, buffer[1]);
    }

    [Fact]
    public void Zu_kleiner_Puffer_wird_abgelehnt()
    {
        // Arrange
        var buffer = new byte[FrameHeader.Size - 1];

        // Act + Assert
        Assert.Throws<ArgumentException>(() =>
            FrameHeader.Write(buffer, new CaptureRegion(0, 0, 10, 10)));
    }

    [Fact]
    public void Unmoegliche_Koordinaten_werden_abgelehnt()
    {
        // Arrange
        var buffer = new byte[FrameHeader.Size];
        var region = new CaptureRegion(0, 0, FrameHeader.MaxCoordinate + 1, 10);

        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameHeader.Write(buffer, region));
    }
}
