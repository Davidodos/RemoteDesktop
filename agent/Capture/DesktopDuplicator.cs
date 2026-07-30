using System.Runtime.CompilerServices;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteDesktopAgent.Capture;

/// <summary>Rohbild eines Frames im Hauptspeicher, gültig nur innerhalb des Callbacks.</summary>
public readonly record struct FrameBuffer(IntPtr Pixels, int RowPitch, int Width, int Height);

/// <summary>Was ein Aufruf von <see cref="DesktopDuplicator.TryCapture"/> ergeben hat.</summary>
public enum CaptureStatus
{
    /// <summary>Neues Bild, der Consumer wurde aufgerufen.</summary>
    Frame,

    /// <summary>Innerhalb des Timeouts hat sich nichts geändert.</summary>
    Timeout,

    /// <summary>Windows hat die Duplikation entzogen; sie wurde neu aufgebaut, dieses Bild fehlt.</summary>
    Lost
}

/// <summary>
/// Bildschirmaufnahme über die Desktop Duplication API.
///
/// Das ist der Weg, den Windows selbst für Remote-Desktop-Software vorsieht:
/// die Bilder kommen fertig zusammengesetzt von der GPU, ohne die Fenster
/// einzeln abzumalen, und Windows sagt dazu, welche Bereiche sich geändert
/// haben. Ein Screenshot pro Frame (<c>BitBlt</c>) würde bei 30 fps eine CPU
/// dauerhaft auslasten.
///
/// Nicht threadsicher — pro Stream eine Instanz.
/// </summary>
public sealed class DesktopDuplicator : IDisposable
{
    /// <summary>Signatur des Verbrauchers eines Frames. Läuft, solange das Bild gemappt ist.</summary>
    public delegate void FrameConsumer(FrameBuffer buffer, IReadOnlyList<CaptureRegion> dirty);

    /// <summary>
    /// Absteigend, D3D11 nimmt das erste, was die Karte kann. Explizit statt
    /// implizit, damit auf einer alten Onboard-Grafik nicht plötzlich die
    /// Gerätekreierung scheitert.
    /// </summary>
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private readonly string _deviceName;
    private readonly ILogger _logger;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;

    private RawRect[] _dirtyBuffer = new RawRect[64];
    private OutduplMoveRect[] _moveBuffer = new OutduplMoveRect[16];

    public DesktopDuplicator(string deviceName, ILogger logger)
    {
        _deviceName = deviceName;
        _logger = logger;

        Initialize();
    }

    /// <summary>Breite des aufgenommenen Monitors in echten Pixeln.</summary>
    public int Width { get; private set; }

    /// <summary>Höhe des aufgenommenen Monitors in echten Pixeln.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Holt das nächste Bild. Der Consumer bekommt es synchron — nach seiner
    /// Rückkehr gibt der Duplicator den Speicher wieder frei.
    /// </summary>
    public CaptureStatus TryCapture(int timeoutMs, FrameConsumer consumer)
    {
        if (_duplication is null || _context is null || _staging is null)
        {
            Reinitialize();
            return CaptureStatus.Lost;
        }

        var result = _duplication.AcquireNextFrame(
            (uint)timeoutMs, out var frameInfo, out var resource);

        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return CaptureStatus.Timeout;
        }

        if (result.Failure)
        {
            // AccessLost kommt bei Auflösungswechsel, Vollbildspielen, UAC-Dialog
            // und beim Sperrbildschirm — alles normale Ereignisse, kein Fehler.
            _logger.LogDebug("Duplikation verloren ({Code}), baue neu auf.", result);
            Reinitialize();
            return CaptureStatus.Lost;
        }

        try
        {
            // LastPresentTime 0 heißt: nur der Mauszeiger hat sich bewegt. Der
            // Zeiger wird nicht mitgesendet (die App zeigt einen eigenen), also
            // gibt es hier nichts zu tun.
            if (frameInfo.LastPresentTime == 0)
            {
                return CaptureStatus.Timeout;
            }

            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            _context.CopyResource(_staging, texture);

            var dirty = ReadDirtyRegions(frameInfo);
            var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

            try
            {
                consumer(
                    new FrameBuffer(mapped.DataPointer, (int)mapped.RowPitch, Width, Height),
                    dirty);
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }

            return CaptureStatus.Frame;
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    /// <summary>
    /// Änderungs- und Verschiebungsrechtecke des Frames.
    ///
    /// Verschiebungen (gescrollter Text, ein gezogenes Fenster) meldet Windows
    /// als Quelle→Ziel. Wir behandeln nur das Ziel als geändert: die App hat
    /// keinen Kopierbefehl im Protokoll, sie bekommt den Bereich neu gemalt.
    /// </summary>
    private IReadOnlyList<CaptureRegion> ReadDirtyRegions(OutduplFrameInfo frameInfo)
    {
        if (_duplication is null || frameInfo.TotalMetadataBufferSize == 0)
        {
            return [];
        }

        // Windows nennt die Gesamtgröße beider Blöcke in Bytes. Danach die
        // Puffer zu dimensionieren, spart den sonst üblichen Ping-Pong mit
        // DXGI_ERROR_MORE_DATA.
        EnsureCapacity(frameInfo.TotalMetadataBufferSize);

        var regions = new List<CaptureRegion>();

        if (_duplication.GetFrameMoveRects(
                (uint)(_moveBuffer.Length * SizeOf<OutduplMoveRect>()),
                _moveBuffer,
                out var moveBytes).Success)
        {
            foreach (var move in _moveBuffer.Take((int)moveBytes / SizeOf<OutduplMoveRect>()))
            {
                regions.Add(ToRegion(move.DestinationRect));
            }
        }

        if (_duplication.GetFrameDirtyRects(
                (uint)(_dirtyBuffer.Length * SizeOf<RawRect>()),
                _dirtyBuffer,
                out var dirtyBytes).Success)
        {
            foreach (var rect in _dirtyBuffer.Take((int)dirtyBytes / SizeOf<RawRect>()))
            {
                regions.Add(ToRegion(rect));
            }
        }

        return regions;
    }

    private void EnsureCapacity(uint totalMetadataBytes)
    {
        var moves = (int)(totalMetadataBytes / SizeOf<OutduplMoveRect>()) + 1;
        var rects = (int)(totalMetadataBytes / SizeOf<RawRect>()) + 1;

        if (_moveBuffer.Length < moves)
        {
            _moveBuffer = new OutduplMoveRect[moves];
        }

        if (_dirtyBuffer.Length < rects)
        {
            _dirtyBuffer = new RawRect[rects];
        }
    }

    private static int SizeOf<T>() where T : unmanaged => Unsafe.SizeOf<T>();

    private static CaptureRegion ToRegion(RawRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private void Initialize()
    {
        var (adapter, output) = FindOutput(_deviceName);

        using (adapter)
        using (output)
        {
            // DriverType.Unknown ist Pflicht, sobald ein Adapter mitgegeben
            // wird — sonst lehnt D3D11 die Kombination ab. Und der Adapter muss
            // derselbe sein wie der des Outputs, sonst gibt es kein Frame.
            var result = D3D11.D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                FeatureLevels,
                out ID3D11Device device,
                out ID3D11DeviceContext context);

            result.CheckError();

            _device = device;
            _context = context;

            var description = output.Description;
            var bounds = description.DesktopCoordinates;

            Width = bounds.Right - bounds.Left;
            Height = bounds.Bottom - bounds.Top;

            using var output1 = output.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(device);

            _staging = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)Width,
                Height = (uint)Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            });
        }
    }

    /// <summary>
    /// Position eines Monitors in der DXGI-Aufzählung: welcher Grafikkarte er
    /// hängt und der wievielte Ausgang er dort ist.
    ///
    /// Genau diese beiden Zahlen braucht ffmpeg für <c>ddagrab</c> — unser
    /// eigener Monitor-Index ist nach Bildschirmposition sortiert und stimmt
    /// damit nicht überein.
    /// </summary>
    public static (int Adapter, int Output) LocateOutput(string deviceName)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        if (output.Description.DeviceName == deviceName)
                        {
                            return ((int)a, (int)o);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"Kein Grafikausgang für Monitor '{deviceName}' gefunden.");
    }

    /// <summary>Adapter und Output zum Windows-Gerätenamen (<c>\\.\DISPLAY1</c>).</summary>
    private static (IDXGIAdapter1 Adapter, IDXGIOutput Output) FindOutput(string deviceName)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
            {
                if (output.Description.DeviceName == deviceName)
                {
                    return (adapter, output);
                }

                output.Dispose();
            }

            adapter.Dispose();
        }

        throw new InvalidOperationException(
            $"Kein Grafikausgang für Monitor '{deviceName}' gefunden. " +
            "Wurde die Anzeige gerade umgesteckt?");
    }

    private void Reinitialize()
    {
        Release();

        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            // Beim Sperrbildschirm und während eines UAC-Dialogs schlägt der
            // Neuaufbau zuverlässig fehl. Der Streamer versucht es weiter.
            _logger.LogDebug(ex, "Neuaufbau der Duplikation fehlgeschlagen.");
        }
    }

    private void Release()
    {
        _staging?.Dispose();
        _staging = null;

        _duplication?.Dispose();
        _duplication = null;

        _context?.Dispose();
        _context = null;

        _device?.Dispose();
        _device = null;
    }

    public void Dispose() => Release();
}
