using RemoteDesktopSetup;
using Xunit;

namespace RemoteDesktopSetup.Tests;

/// <summary>
/// Das Kürzel für den Vollzugriff.
///
/// <para>
/// Es ist das Einzige, was während einer Übernahme auf diesem Rechner bleibt —
/// jeder andere Anschlag geht zum fernen. Was hier durchrutscht, merkt jemand
/// erst in dem Moment, in dem er die Kontrolle zurück haben will.
/// </para>
/// </summary>
public sealed class HotkeyFileTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"rd-hotkey-{Guid.NewGuid():N}");

    public HotkeyFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// Kein Rückfall auf irgendeine Vorgabe: genau daran erkennt die App, dass
    /// sie beim ersten Verbinden fragen muss. Ein Kürzel, das niemand gesehen
    /// hat, wäre keins.
    /// </summary>
    [Fact]
    public void OhneDateiGibtEsKeinKuerzel()
    {
        Assert.Null(HotkeyFile.Read(_folder));
    }

    [Fact]
    public void GeschriebenesKommtZurueck()
    {
        HotkeyFile.Write(_folder, "ctrl+alt+KeyK");

        Assert.Equal("ctrl+alt+KeyK", HotkeyFile.Read(_folder));
    }

    [Fact]
    public void UmgebenderLeerraumZaehltNicht()
    {
        HotkeyFile.Write(_folder, "  ctrl+alt+KeyK \n");

        Assert.Equal("ctrl+alt+KeyK", HotkeyFile.Read(_folder));
    }

    /// <summary>
    /// Die Datei liegt offen im Datenordner und wird irgendwann von Hand
    /// angefasst. Was dann darin steht, ist nicht zwangsläufig ein Kürzel.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl + alt + KeyK")]
    public void UnbrauchbaresGiltAlsNichtVergeben(string content)
    {
        File.WriteAllText(HotkeyFile.In(_folder), content);

        Assert.Null(HotkeyFile.Read(_folder));
    }

    [Fact]
    public void EinLeeresKuerzelWirdNichtGeschrieben()
    {
        Assert.Throws<ArgumentException>(() => HotkeyFile.Write(_folder, "   "));
        Assert.False(File.Exists(HotkeyFile.In(_folder)));
    }

    [Fact]
    public void ZuLangesWirdVerworfen()
    {
        Assert.Null(HotkeyFile.Sanitize(new string('x', HotkeyFile.MaxLength + 1)));
    }
}
