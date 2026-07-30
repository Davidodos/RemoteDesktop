namespace RemoteDesktopAgent.Capture.H264;

/// <summary>
/// Zerlegt den H.264-Rohstrom von ffmpeg in einzelne Bilder.
///
/// ffmpeg schreibt auf die Pipe einen ununterbrochenen Strom aus NAL-Einheiten,
/// jeweils eingeleitet von einem Startcode (<c>00 00 01</c> oder
/// <c>00 00 00 01</c>). WebRTC will dagegen ein vollständiges Bild pro Sendung:
/// wird ein Bild in mehreren Sendungen abgesetzt, markiert der Paketierer
/// jedes Stück als eigenes Bild und der Decoder im Browser zeigt Artefakte.
///
/// Nicht threadsicher — eine Instanz pro ffmpeg-Prozess.
/// </summary>
public sealed class AnnexBSplitter
{
    /// <summary>Zugriffseinheit-Trenner. Steht, wenn vorhanden, immer am Anfang eines Bildes.</summary>
    private const int NalAccessUnitDelimiter = 9;

    /// <summary>Sequenz- und Bildparameter. Kommen unmittelbar vor einem Schlüsselbild.</summary>
    private const int NalSequenceParameterSet = 7;
    private const int NalPictureParameterSet = 8;

    /// <summary>Die beiden Typen, die tatsächlich Bildinhalt tragen.</summary>
    private const int NalNonKeyframeSlice = 1;
    private const int NalKeyframeSlice = 5;

    /// <summary>Puffer für das Bild, das gerade zusammengesetzt wird.</summary>
    private readonly MemoryStream _current = new(256 * 1024);

    /// <summary>Reste vom letzten Lesevorgang, in denen noch ein Startcode stecken kann.</summary>
    private byte[] _carry = [];

    private bool _hasSlice;

    /// <summary>
    /// Nimmt gelesene Bytes entgegen und gibt jedes fertige Bild zurück.
    /// Die zurückgegebenen Puffer gehören dem Aufrufer.
    /// </summary>
    public IEnumerable<byte[]> Push(ReadOnlySpan<byte> data)
    {
        var buffer = new byte[_carry.Length + data.Length];
        _carry.CopyTo(buffer, 0);
        data.CopyTo(buffer.AsSpan(_carry.Length));

        var complete = new List<byte[]>();
        var position = 0;

        while (true)
        {
            var start = FindStartCode(buffer, position, out var codeLength);

            if (start < 0)
            {
                break;
            }

            var next = FindStartCode(buffer, start + codeLength, out _);

            if (next < 0)
            {
                // Die letzte NAL ist womöglich noch nicht vollständig gelesen —
                // sie wartet bis zum nächsten Aufruf.
                position = start;
                break;
            }

            AppendNal(buffer.AsSpan(start, next - start), codeLength, complete);
            position = next;
        }

        _carry = buffer[position..];

        return complete;
    }

    /// <summary>Gibt das letzte angefangene Bild heraus — für das Ende des Streams.</summary>
    public byte[]? Flush()
    {
        if (_current.Length == 0)
        {
            return null;
        }

        var frame = _current.ToArray();
        _current.SetLength(0);
        _hasSlice = false;

        return frame;
    }

    private void AppendNal(ReadOnlySpan<byte> nal, int codeLength, List<byte[]> complete)
    {
        var type = nal[codeLength] & 0x1F;

        if (StartsNewFrame(type, nal, codeLength) && _current.Length > 0)
        {
            complete.Add(_current.ToArray());
            _current.SetLength(0);
            _hasSlice = false;
        }

        if (type is NalNonKeyframeSlice or NalKeyframeSlice)
        {
            _hasSlice = true;
        }

        _current.Write(nal);
    }

    /// <summary>
    /// Entscheidet, ob mit dieser NAL ein neues Bild beginnt.
    ///
    /// Trenner und Sequenzparameter leiten immer eines ein. Die Bildparameter
    /// nicht: sie folgen unmittelbar auf die Sequenzparameter und gehören zum
    /// selben Schlüsselbild — würden sie trennen, käme beim Browser ein Bild
    /// ohne seine Parameter an und der Strom ließe sich nie anfangen zu
    /// dekodieren. Bei Bilddaten hilft das oberste Bit hinter dem NAL-Kopf: es
    /// steht für <c>first_mb_in_slice == 0</c>, also den Anfang eines Bildes
    /// und nicht die Fortsetzung eines bereits begonnenen.
    /// </summary>
    private bool StartsNewFrame(int type, ReadOnlySpan<byte> nal, int codeLength)
    {
        if (type is NalAccessUnitDelimiter or NalSequenceParameterSet)
        {
            return true;
        }

        if (type is not (NalNonKeyframeSlice or NalKeyframeSlice) || !_hasSlice)
        {
            return false;
        }

        return nal.Length > codeLength + 1 && (nal[codeLength + 1] & 0x80) != 0;
    }

    /// <summary>Position des nächsten Startcodes ab <paramref name="from"/>, sonst -1.</summary>
    private static int FindStartCode(ReadOnlySpan<byte> buffer, int from, out int codeLength)
    {
        for (var i = Math.Max(from, 0); i + 2 < buffer.Length; i++)
        {
            if (buffer[i] != 0x00 || buffer[i + 1] != 0x00)
            {
                continue;
            }

            if (buffer[i + 2] == 0x01)
            {
                codeLength = 3;
                return i;
            }

            if (buffer[i + 2] == 0x00 && i + 3 < buffer.Length && buffer[i + 3] == 0x01)
            {
                codeLength = 4;
                return i;
            }
        }

        codeLength = 0;
        return -1;
    }
}
