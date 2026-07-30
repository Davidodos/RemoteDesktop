namespace RemoteDesktopAgent.Services;

/// <summary>Wiedergabefortschritt eines Stücks, alles in Sekunden.</summary>
/// <param name="Position">Verstrichene Zeit zum Zeitpunkt der letzten Meldung.</param>
/// <param name="Duration">Gesamtlänge, 0 bei Livestreams und unbekannter Länge.</param>
/// <param name="Age">Wie alt die Positionsangabe ist.</param>
public readonly record struct MediaProgress(double Position, double Duration, double Age);

/// <summary>
/// Rechnet die Zeitangaben von Windows in etwas um, mit dem die App eine
/// Fortschrittsleiste zeichnen kann.
///
/// Der Haken an der Windows-Schnittstelle: die Position wird <em>nicht</em>
/// laufend fortgeschrieben, sondern nur, wenn die App sie meldet — beim Start,
/// beim Pausieren, beim Springen. Dazwischen bleibt sie stehen. Deshalb kommt
/// das Alter der Angabe mit: die App zählt selbst weiter und muss nicht im
/// Sekundentakt nachfragen.
///
/// Reine Rechnung ohne Windows-Abhängigkeit, damit sie prüfbar bleibt.
/// </summary>
public static class MediaTimeline
{
    /// <summary>
    /// Älter als das ist keine Positionsangabe glaubwürdig. Manche Apps setzen
    /// den Zeitstempel gar nicht, dann stünde hier sonst ein Wert aus dem Jahr 1.
    /// </summary>
    private const double MaxAgeSeconds = 60;

    public static MediaProgress Describe(
        TimeSpan start, TimeSpan end, TimeSpan position, DateTimeOffset lastUpdated, DateTimeOffset now)
    {
        var duration = (end - start).TotalSeconds;
        var elapsed = (position - start).TotalSeconds;

        // Ein Livestream meldet Anfang und Ende gleich — dann gibt es keinen
        // Fortschritt zu zeigen.
        if (duration <= 0 || double.IsNaN(duration))
        {
            return new MediaProgress(Math.Max(elapsed, 0), 0, 0);
        }

        elapsed = Math.Clamp(elapsed, 0, duration);

        var age = (now - lastUpdated).TotalSeconds;

        if (age is < 0 or > MaxAgeSeconds || lastUpdated == default)
        {
            age = 0;
        }

        return new MediaProgress(elapsed, duration, age);
    }
}
