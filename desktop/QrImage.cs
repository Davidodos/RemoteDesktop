using QRCoder;

namespace RemoteDesktopClient;

/// <summary>
/// Macht aus dem Kopplungs-Link ein Bild, das eine Handykamera lesen kann.
///
/// Gezeichnet wird die Modulmatrix von Hand statt über den mitgelieferten
/// Renderer von QRCoder: der bringt eigene Annahmen über Ränder und Skalierung
/// mit, und hier soll die Kantenlänge ein glattes Vielfaches der Modulzahl sein.
/// Andernfalls entstehen beim Skalieren halbe Module, und genau daran scheitern
/// Kameras bei schlechtem Licht.
/// </summary>
public static class QrImage
{
    /// <summary>
    /// Erzeugt ein quadratisches Bild, das höchstens <paramref name="maxSide"/>
    /// Pixel breit ist. Die tatsächliche Kantenlänge liegt darunter, sobald sie
    /// sich nicht glatt teilen lässt.
    /// </summary>
    public static Bitmap Render(string content, int maxSide)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSide, 1);

        using var generator = new QRCodeGenerator();

        // Stufe Q verträgt rund 25 % Verlust. Der Link ist kurz, die Stufe
        // kostet hier also kaum Module — dafür liest die Kamera den Code auch
        // schräg und auf einem spiegelnden Monitor.
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);

        var matrix = data.ModuleMatrix;
        var modules = matrix.Count;
        var scale = Math.Max(1, maxSide / modules);
        var side = modules * scale;

        var bitmap = new Bitmap(side, side);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);

        using var ink = new SolidBrush(Color.Black);

        // Die Ruhezone steckt bereits in der Matrix, die QRCoder liefert — sie
        // wird hier nur nicht noch einmal aufgeschlagen.
        for (var y = 0; y < modules; y++)
        {
            var row = matrix[y];

            for (var x = 0; x < modules; x++)
            {
                if (row[x])
                {
                    graphics.FillRectangle(ink, x * scale, y * scale, scale, scale);
                }
            }
        }

        return bitmap;
    }
}
