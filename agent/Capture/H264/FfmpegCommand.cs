namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Baut die ffmpeg-Kommandozeile für einen Monitor.
///
/// Getrennt vom Prozessstart, damit sich die Argumente ohne laufendes Windows
/// prüfen lassen — ein Tippfehler hier äußert sich sonst als kommentarlos
/// beendeter Prozess.
/// </summary>
public static class FfmpegCommand
{
    /// <summary>
    /// <c>ddagrab</c> ist ffmpegs Zugang zur Desktop Duplication API,
    /// <c>output_idx</c> wählt den Monitor in der Reihenfolge der Grafikkarte.
    /// </summary>
    public static IReadOnlyList<string> Build(
        EncoderProfile encoder, int adapterIndex, int outputIndex, int framerate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(adapterIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(outputIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(framerate, 1);

        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            // Ohne ausdrücklich benanntes D3D11-Gerät sucht sich ffmpeg selbst
            // eines aus und erwischt auf Rechnern mit zwei Grafikkarten die
            // falsche — dann bleibt das Bild schwarz.
            "-init_hw_device", $"d3d11va=dx:{adapterIndex}",
            "-filter_hw_device", "dx",
            "-filter_complex",
            $"ddagrab=output_idx={outputIndex}:framerate={framerate}:draw_mouse=0,{encoder.Filter}",
            "-c:v", encoder.Name,
            .. encoder.Options,
            "-b:v", EncoderProfiles.Bitrate,
            "-g", EncoderProfiles.KeyframeInterval.ToString(),
            // Keine B-Bilder: sie bringen Kompression auf Kosten von Latenz,
            // und Latenz ist hier das Einzige, was zählt.
            "-bf", "0",
            "-f", "h264",
            "pipe:1"
        ];
    }

    /// <summary>Argumente als eine Zeile — nur für Logausgaben.</summary>
    public static string Describe(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
