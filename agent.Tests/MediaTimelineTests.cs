using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Umrechnung der Windows-Zeitangaben. Fehler hier zeigen sich als
/// Fortschrittsleiste, die springt oder am falschen Ende steht.
/// </summary>
public class MediaTimelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Position_und_Laenge_werden_uebernommen()
    {
        // Act
        var progress = MediaTimeline.Describe(
            start: TimeSpan.Zero,
            end: TimeSpan.FromMinutes(4),
            position: TimeSpan.FromMinutes(1),
            lastUpdated: Now,
            now: Now);

        // Assert
        Assert.Equal(60, progress.Position);
        Assert.Equal(240, progress.Duration);
    }

    [Fact]
    public void Ein_Startversatz_wird_herausgerechnet()
    {
        // Arrange — manche Apps zählen nicht bei null los.
        var progress = MediaTimeline.Describe(
            start: TimeSpan.FromSeconds(30),
            end: TimeSpan.FromSeconds(90),
            position: TimeSpan.FromSeconds(45),
            lastUpdated: Now,
            now: Now);

        // Assert
        Assert.Equal(15, progress.Position);
        Assert.Equal(60, progress.Duration);
    }

    [Fact]
    public void Das_Alter_der_Angabe_kommt_mit()
    {
        // Arrange — Windows schreibt die Position nicht laufend fort, die App
        // muss selbst weiterzählen.
        var progress = MediaTimeline.Describe(
            TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1),
            lastUpdated: Now.AddSeconds(-3),
            now: Now);

        // Assert
        Assert.Equal(3, progress.Age, precision: 3);
    }

    [Fact]
    public void Ein_Livestream_hat_keine_Laenge()
    {
        // Arrange — Anfang und Ende sind gleich.
        var progress = MediaTimeline.Describe(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMinutes(20), Now, Now);

        // Assert — die App zeichnet dann keine Leiste.
        Assert.Equal(0, progress.Duration);
    }

    [Fact]
    public void Eine_Position_hinter_dem_Ende_wird_geklemmt()
    {
        // Act
        var progress = MediaTimeline.Describe(
            TimeSpan.Zero, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(75), Now, Now);

        // Assert
        Assert.Equal(60, progress.Position);
    }

    [Fact]
    public void Eine_negative_Position_wird_zu_null()
    {
        // Act
        var progress = MediaTimeline.Describe(
            start: TimeSpan.FromSeconds(30), end: TimeSpan.FromSeconds(90),
            position: TimeSpan.FromSeconds(10), lastUpdated: Now, now: Now);

        // Assert
        Assert.Equal(0, progress.Position);
    }

    [Fact]
    public void Ein_fehlender_Zeitstempel_ergibt_kein_Alter()
    {
        // Arrange — Apps, die ihn nicht setzen, lieferten sonst Jahrhunderte.
        var progress = MediaTimeline.Describe(
            TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1),
            lastUpdated: default,
            now: Now);

        // Assert
        Assert.Equal(0, progress.Age);
    }

    [Fact]
    public void Ein_uralter_Zeitstempel_wird_verworfen()
    {
        // Act
        var progress = MediaTimeline.Describe(
            TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1),
            lastUpdated: Now.AddHours(-2),
            now: Now);

        // Assert — sonst spränge die Leiste beim ersten Zeichnen ans Ende.
        Assert.Equal(0, progress.Age);
    }

    [Fact]
    public void Ein_Zeitstempel_aus_der_Zukunft_wird_verworfen()
    {
        // Arrange — kommt bei ungenauen Uhren vor.
        var progress = MediaTimeline.Describe(
            TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1),
            lastUpdated: Now.AddSeconds(5),
            now: Now);

        // Assert
        Assert.Equal(0, progress.Age);
    }
}
