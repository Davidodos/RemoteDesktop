using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RemoteDesktopAgent.Capture;

/// <summary>
/// Kodiert Bildausschnitte als JPEG.
///
/// GDI+ statt WIC: der Encoder steckt bereits in jedem Windows, die
/// Skalierung kommt gratis mit, und bei den hier üblichen Ausschnitten liegt
/// er im einstelligen Millisekundenbereich. Der Aufwand einer eigenen
/// WIC-Anbindung lohnt erst, wenn Stufe 2 (H.264) nicht reicht.
///
/// Nicht threadsicher — pro Stream eine Instanz.
/// </summary>
public sealed class JpegEncoder : IDisposable
{
    private static readonly ImageCodecInfo Codec =
        ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");

    private readonly EncoderParameters _parameters = new(1);
    private readonly MemoryStream _output = new(256 * 1024);

    private Bitmap? _scaled;

    /// <summary>
    /// Schneidet den Ausschnitt aus dem Rohbild, skaliert ihn und gibt die
    /// JPEG-Bytes zurück. Der Rückgabepuffer gehört dem Aufrufer.
    /// </summary>
    public byte[] Encode(FrameBuffer frame, CaptureRegion region, QualityLevel level)
    {
        if (region.IsEmpty)
        {
            throw new ArgumentException("Leerer Ausschnitt.", nameof(region));
        }

        // Zeiger auf die erste Zeile des Ausschnitts. Der Stride bleibt der des
        // Vollbilds — dadurch ist kein Umkopieren nötig, GDI+ liest direkt aus
        // dem gemappten Speicher der Grafikkarte.
        var origin = frame.Pixels + region.Y * frame.RowPitch + region.X * 4;

        using var source = new Bitmap(
            region.Width, region.Height, frame.RowPitch, PixelFormat.Format32bppRgb, origin);

        var targetWidth = Scale(region.Width, level.Scale);
        var targetHeight = Scale(region.Height, level.Scale);

        _output.SetLength(0);
        _parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)level.Quality);

        if (targetWidth == region.Width && targetHeight == region.Height)
        {
            source.Save(_output, Codec, _parameters);
        }
        else
        {
            Resize(source, targetWidth, targetHeight).Save(_output, Codec, _parameters);
        }

        return _output.ToArray();
    }

    /// <summary>
    /// Skaliert in einen wiederverwendeten Puffer. Bei 30 Bildern pro Sekunde
    /// jedes Mal eine neue Bitmap anzulegen, beschäftigt vor allem den
    /// Garbage Collector.
    /// </summary>
    private Bitmap Resize(Bitmap source, int width, int height)
    {
        if (_scaled is null || _scaled.Width != width || _scaled.Height != height)
        {
            _scaled?.Dispose();
            _scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        }

        using var graphics = Graphics.FromImage(_scaled);

        // Bilinear statt HighQualityBicubic: der Unterschied ist auf einem
        // Handydisplay nicht zu sehen, kostet aber ein Vielfaches an Rechenzeit.
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(source, 0, 0, width, height);

        return _scaled;
    }

    private static int Scale(int value, double factor) =>
        Math.Max(1, (int)Math.Round(value * factor));

    public void Dispose()
    {
        _scaled?.Dispose();
        _parameters.Dispose();
        _output.Dispose();
    }
}
