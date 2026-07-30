using System.Buffers.Binary;

namespace RemoteDesktopAgent.Capture;

/// <summary>
/// Der Kopf jeder Binärnachricht auf <c>/ws/screen</c>: wohin das folgende JPEG
/// im Bild gehört.
///
/// Acht Bytes statt JSON, weil bei 30 Bildern pro Sekunde und bis zu acht
/// Ausschnitten je Bild jedes gesparte Byte zählt — und weil die App den
/// Rest der Nachricht so ohne Umkopieren als Blob an <c>createImageBitmap</c>
/// weiterreichen kann.
///
/// Die Ausschnittmaße sind die des <em>Zielbereichs</em> auf dem Monitor. Ist
/// der Stream herunterskaliert, ist das enthaltene JPEG kleiner — die App
/// zeichnet es per <c>drawImage</c> wieder auf diese Größe. Damit bleiben
/// Rundungsfehler der Skalierung vollständig auf der Agent-Seite.
/// </summary>
public static class FrameHeader
{
    public const int Size = 8;

    /// <summary>Größter darstellbarer Wert je Feld — 16 Bit reichen für jeden realen Monitor.</summary>
    public const int MaxCoordinate = ushort.MaxValue;

    public static void Write(Span<byte> destination, CaptureRegion region)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"Header braucht {Size} Bytes.", nameof(destination));
        }

        if (region.X < 0 || region.Y < 0 || region.Width < 0 || region.Height < 0 ||
            region.Right > MaxCoordinate || region.Bottom > MaxCoordinate)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region,
                "Ausschnitt liegt außerhalb des darstellbaren Bereichs.");
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)region.X);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)region.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)region.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)region.Height);
    }

    public static CaptureRegion Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
        {
            throw new ArgumentException($"Header braucht {Size} Bytes.", nameof(source));
        }

        return new CaptureRegion(
            BinaryPrimitives.ReadUInt16LittleEndian(source),
            BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[6..]));
    }
}
