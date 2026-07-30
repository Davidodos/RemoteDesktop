using RemoteDesktopAgent.Capture.H264;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Zerlegen des H.264-Stroms in Bilder. Liegt die Bildgrenze falsch, zeigt der
/// Browser Streifen und Klötzchen — sichtbar, aber schwer zuzuordnen.
/// </summary>
public class AnnexBSplitterTests
{
    /// <summary>Startcode plus NAL-Kopf für einen Typ, danach ein Byte Nutzlast.</summary>
    private static byte[] Nal(int type, byte payload = 0x80) =>
        [0x00, 0x00, 0x00, 0x01, (byte)(type & 0x1F), payload];

    private static byte[] Concat(params byte[][] parts) =>
        parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Ohne_Startcode_kommt_nichts_heraus()
    {
        // Arrange
        var splitter = new AnnexBSplitter();

        // Act
        var frames = splitter.Push([0x11, 0x22, 0x33]);

        // Assert
        Assert.Empty(frames);
    }

    [Fact]
    public void Ein_einzelnes_Bild_bleibt_bis_zum_naechsten_offen()
    {
        // Arrange — solange nichts Neues anfängt, ist das Bild nicht fertig.
        var splitter = new AnnexBSplitter();

        // Act
        var frames = splitter.Push(Nal(5));

        // Assert
        Assert.Empty(frames);
    }

    [Fact]
    public void Der_Trenner_schliesst_das_vorige_Bild_ab()
    {
        // Arrange
        var splitter = new AnnexBSplitter();

        // Act — Bilddaten, dann der Trenner des nächsten Bildes.
        var frames = splitter.Push(Concat(Nal(5), Nal(9), Nal(1))).ToList();

        // Assert
        Assert.Single(frames);
    }

    [Fact]
    public void Die_zuletzt_gelesene_NAL_wird_zurueckgehalten()
    {
        // Arrange — sie könnte noch unvollständig sein, deshalb wartet der
        // Splitter auf den nächsten Startcode, bevor er sie verarbeitet.
        var splitter = new AnnexBSplitter();

        // Act
        var frames = splitter.Push(Concat(Nal(5), Nal(9))).ToList();

        // Assert
        Assert.Empty(frames);
    }

    [Fact]
    public void Sequenzparameter_leiten_ein_neues_Bild_ein()
    {
        // Arrange
        var splitter = new AnnexBSplitter();

        // Act
        var frames = splitter.Push(Concat(Nal(1), Nal(7), Nal(8), Nal(5), Nal(7), Nal(9))).ToList();

        // Assert — vor dem ersten und dem letzten SPS endet je ein Bild.
        Assert.Equal(2, frames.Count);
    }

    [Fact]
    public void Parametersaetze_bleiben_beim_Schluesselbild()
    {
        // Arrange
        var splitter = new AnnexBSplitter();

        // Act
        var frames = splitter.Push(Concat(Nal(7), Nal(8), Nal(5), Nal(7), Nal(9))).ToList();
        var keyframe = frames[0];

        // Assert — SPS, PPS und Bilddaten müssen zusammen ankommen, sonst kann
        // der Browser den Strom nicht anfangen zu dekodieren.
        Assert.Equal(3 * 6, keyframe.Length);
    }

    [Fact]
    public void Mehrere_Slices_eines_Bildes_bleiben_zusammen()
    {
        // Arrange — Fortsetzungs-Slices haben das oberste Bit nicht gesetzt.
        var splitter = new AnnexBSplitter();

        // Act
        var frames = splitter.Push(Concat(
            Nal(5, 0x80), Nal(1, 0x40), Nal(1, 0x20), Nal(5, 0x80), Nal(9))).ToList();

        // Assert
        Assert.Equal(3 * 6, frames[0].Length);
    }

    [Fact]
    public void Ueber_Lesegrenzen_hinweg_zerteilte_Startcodes_gehen_nicht_verloren()
    {
        // Arrange — genau hier gehen naive Implementierungen kaputt.
        var splitter = new AnnexBSplitter();
        var stream = Concat(Nal(5), Nal(9), Nal(1), Nal(9), Nal(1));

        // Act — bewusst mitten im Startcode getrennt.
        var frames = new List<byte[]>();

        for (var offset = 0; offset < stream.Length; offset += 3)
        {
            frames.AddRange(splitter.Push(stream.AsSpan(offset, Math.Min(3, stream.Length - offset))));
        }

        // Assert
        Assert.Equal(2, frames.Count);
    }

    [Fact]
    public void Der_kurze_Startcode_wird_ebenso_erkannt()
    {
        // Arrange — ffmpeg mischt 3- und 4-Byte-Startcodes.
        var splitter = new AnnexBSplitter();
        var stream = Concat(
            [0x00, 0x00, 0x01, 0x65, 0x80],
            [0x00, 0x00, 0x01, 0x09, 0x80],
            [0x00, 0x00, 0x01, 0x65, 0x80],
            [0x00, 0x00, 0x01, 0x09, 0x80],
            [0x00, 0x00, 0x01, 0x65, 0x80]);

        // Act
        var frames = splitter.Push(stream).ToList();

        // Assert
        Assert.Equal(2, frames.Count);
    }

    [Fact]
    public void Flush_gibt_das_angefangene_Bild_heraus()
    {
        // Arrange
        var splitter = new AnnexBSplitter();
        splitter.Push(Concat(Nal(5), Nal(9)));

        // Act
        var last = splitter.Flush();

        // Assert
        Assert.NotNull(last);
    }

    [Fact]
    public void Flush_ohne_Daten_liefert_nichts()
    {
        // Arrange
        var splitter = new AnnexBSplitter();

        // Act + Assert
        Assert.Null(splitter.Flush());
    }
}

public class FfmpegCommandTests
{
    private static readonly EncoderProfile Nvenc = EncoderProfiles.All[0];

    [Fact]
    public void Der_gewaehlte_Monitor_landet_im_Filter()
    {
        // Act
        var arguments = FfmpegCommand.Build(Nvenc, adapterIndex: 0, outputIndex: 2, framerate: 30);

        // Assert
        Assert.Contains(arguments, a => a.Contains("output_idx=2"));
    }

    [Fact]
    public void Die_Grafikkarte_wird_ausdruecklich_benannt()
    {
        // Act
        var arguments = FfmpegCommand.Build(Nvenc, adapterIndex: 1, outputIndex: 0, framerate: 30);

        // Assert — auf Rechnern mit zwei Karten sonst schwarzes Bild.
        Assert.Contains("d3d11va=dx:1", arguments);
    }

    [Fact]
    public void Der_Encoder_steht_hinter_dem_Videoschalter()
    {
        // Act
        var arguments = FfmpegCommand.Build(Nvenc, 0, 0, 30).ToList();

        // Assert
        Assert.Equal(Nvenc.Name, arguments[arguments.IndexOf("-c:v") + 1]);
    }

    [Fact]
    public void Ausgegeben_wird_roher_H264_Strom_auf_die_Pipe()
    {
        // Act
        var arguments = FfmpegCommand.Build(Nvenc, 0, 0, 30).ToList();

        // Assert
        Assert.Equal("pipe:1", arguments[^1]);
        Assert.Equal("h264", arguments[arguments.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Keine_B_Bilder_wegen_der_Latenz()
    {
        // Act
        var arguments = FfmpegCommand.Build(Nvenc, 0, 0, 30).ToList();

        // Assert
        Assert.Equal("0", arguments[arguments.IndexOf("-bf") + 1]);
    }

    [Fact]
    public void Die_Bildrate_wird_uebernommen()
    {
        // Act
        var arguments = FfmpegCommand.Build(Nvenc, 0, 0, 60);

        // Assert
        Assert.Contains(arguments, a => a.Contains("framerate=60"));
    }

    [Fact]
    public void Unsinnige_Werte_werden_abgelehnt()
    {
        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegCommand.Build(Nvenc, 0, -1, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegCommand.Build(Nvenc, 0, 0, 0));
    }

    [Fact]
    public void Es_gibt_einen_Software_Encoder_als_letzten_Ausweg()
    {
        // Assert — ohne den bliebe das Bild auf älteren Rechnern ganz weg.
        Assert.Contains(EncoderProfiles.All, e => !e.IsHardware);
        Assert.Contains(EncoderProfiles.All, e => e.Name == "h264_nvenc" && e.Filter.Contains("hwdownload"));
        Assert.True(EncoderProfiles.All[0].IsHardware);
    }
}
