using RemoteDesktopAgent.Native;
using Xunit;

namespace RemoteDesktopAgent.Tests;

public class VirtualKeysTests
{
    [Theory]
    [InlineData("a", 'A')]
    [InlineData("Z", 'Z')]
    [InlineData("5", '5')]
    public void Loest_Buchstaben_und_Ziffern_auf(string name, char expected)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal((ushort)expected, vk);
    }

    [Theory]
    [InlineData("f1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("f24", 0x87)]
    public void Loest_Funktionstasten_auf(string name, int expected)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal((ushort)expected, vk);
    }

    [Theory]
    [InlineData("ctrl")]
    [InlineData("CTRL")]
    [InlineData("Control")]
    public void Ist_unabhaengig_von_Gross_und_Kleinschreibung(string name)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal(VirtualKeys.VK_CONTROL, vk);
    }

    [Theory]
    [InlineData("f0")]
    [InlineData("f25")]
    [InlineData("gibtsnicht")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ä")]
    public void Lehnt_unbekannte_Namen_ab(string name)
    {
        Assert.False(VirtualKeys.TryResolve(name, out _));
    }

    [Theory]
    [InlineData("arrowup")]
    [InlineData("arrowdown")]
    [InlineData("delete")]
    [InlineData("home")]
    [InlineData("playpause")]
    [InlineData("volumeup")]
    public void Markiert_Tasten_die_das_Extended_Flag_brauchen(string name)
    {
        // Ohne Extended-Flag landen Pfeiltasten als Nummernblock-Eingabe und
        // Media-Keys kommen gar nicht erst an.
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.True(VirtualKeys.IsExtended(vk));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("f5")]
    [InlineData("escape")]
    [InlineData("space")]
    public void Markiert_normale_Tasten_nicht_als_extended(string name)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.False(VirtualKeys.IsExtended(vk));
    }

    [Fact]
    public void Loest_alle_Medientasten_auf()
    {
        string[] mediaKeys = ["playpause", "nexttrack", "prevtrack", "stop", "volumeup", "volumedown", "mute"];

        foreach (var name in mediaKeys)
        {
            Assert.True(VirtualKeys.TryResolve(name, out var vk), $"'{name}' nicht aufgelöst.");
            Assert.InRange(vk, 0xAD, 0xB3);
        }
    }
}
