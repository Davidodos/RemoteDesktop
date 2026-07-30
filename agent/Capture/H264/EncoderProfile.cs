namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Ein H.264-Encoder samt der Filterkette, die ffmpeg braucht, um die Bilder
/// der Desktop Duplication dorthin zu bekommen.
/// </summary>
/// <param name="Name">Encoder-Name in ffmpeg.</param>
/// <param name="Filter">Was zwischen Aufnahme und Encoder passieren muss.</param>
/// <param name="Options">Encoder-Einstellungen, auf niedrige Latenz getrimmt.</param>
/// <param name="IsHardware">Software-Encoder taugen nur als Notnagel.</param>
public sealed record EncoderProfile(
    string Name, string Filter, IReadOnlyList<string> Options, bool IsHardware);

/// <summary>
/// Die Encoder, die wir der Reihe nach durchprobieren.
///
/// Die Reihenfolge ist Absicht: NVIDIA und Intel liefern die Bilder ohne Umweg
/// über den Hauptspeicher, AMD braucht den Umweg, und libx264 belastet die CPU
/// spürbar — der taugt nur, damit überhaupt ein Bild ankommt.
/// </summary>
public static class EncoderProfiles
{
    /// <summary>Zielbitrate. 8 MBit/s reichen für 1440p bei Schreibtischinhalten.</summary>
    public const string Bitrate = "8M";

    /// <summary>
    /// Schlüsselbild alle zwei Sekunden. Häufiger kostet Bandbreite, seltener
    /// bedeutet nach einem Paketverlust ein länger stehendes kaputtes Bild.
    /// </summary>
    public const int KeyframeInterval = 60;

    /// <summary>
    /// Der Umweg über den Hauptspeicher. Kostet Bandbreite auf dem PCIe-Bus,
    /// funktioniert dafür mit jeder Kombination aus Grafikkarte und
    /// ffmpeg-Build — insbesondere dann, wenn die direkte Übergabe von D3D11
    /// an den Encoder nicht einkompiliert ist.
    /// </summary>
    private const string ViaSystemMemory = "hwdownload,format=bgra,format=nv12";

    public static IReadOnlyList<EncoderProfile> All { get; } =
    [
        // Erst der direkte Weg auf der Grafikkarte, dann derselbe Encoder über
        // den Hauptspeicher. Ob die direkte Übergabe geht, hängt vom
        // ffmpeg-Build ab und lässt sich nur durch Ausprobieren feststellen:
        // ein vorhandener Encoder heißt noch lange nicht, dass die Filterkette
        // dahinter zusammenpasst.
        new("h264_nvenc", "hwmap=derive_device=cuda",
            ["-preset", "p1", "-tune", "ll", "-rc", "cbr", "-zerolatency", "1"], true),

        new("h264_nvenc", ViaSystemMemory,
            ["-preset", "p1", "-tune", "ll", "-rc", "cbr", "-zerolatency", "1"], true),

        new("h264_qsv", "hwmap=derive_device=qsv,format=qsv",
            ["-preset", "veryfast", "-low_power", "1"], true),

        new("h264_qsv", ViaSystemMemory, ["-preset", "veryfast"], true),

        new("h264_amf", ViaSystemMemory,
            ["-usage", "ultralowlatency", "-quality", "speed"], true),

        new("libx264", "hwdownload,format=bgra,format=yuv420p",
            ["-preset", "ultrafast", "-tune", "zerolatency"], false)
    ];
}
