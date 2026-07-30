using RemoteDesktopAgent.Api;
using RemoteDesktopAgent.Native;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Das Protokoll zwischen Handy und Agent. Alles, was hier durchrutscht,
/// führt auf dem PC eine Aktion aus — auch Fehleingaben müssen sauber
/// abgewiesen werden.
/// </summary>
public class InputCommandParserTests
{
    [Fact]
    public void Parst_absolute_Bewegung_mit_Monitor()
    {
        // Act
        var result = InputCommandParser.Parse("""{"t":"move","monitor":2,"x":0.25,"y":0.75}""");

        // Assert
        var move = Assert.IsType<InputCommand.MoveAbsolute>(result.Command);
        Assert.Equal(2, move.Monitor);
        Assert.Equal(0.25, move.X);
        Assert.Equal(0.75, move.Y);
    }

    [Fact]
    public void Nimmt_Monitor_null_an_wenn_nicht_angegeben()
    {
        var result = InputCommandParser.Parse("""{"t":"move","x":0.5,"y":0.5}""");

        var move = Assert.IsType<InputCommand.MoveAbsolute>(result.Command);
        Assert.Equal(0, move.Monitor);
    }

    [Fact]
    public void Parst_relative_Bewegung()
    {
        var result = InputCommandParser.Parse("""{"t":"moverel","dx":12,"dy":-8}""");

        var move = Assert.IsType<InputCommand.MoveRelative>(result.Command);
        Assert.Equal(12, move.Dx);
        Assert.Equal(-8, move.Dy);
    }

    [Fact]
    public void Akzeptiert_Ganzzahlen_die_als_Fliesskomma_ankommen()
    {
        // Die App rechnet in JS — dort ist 12 und 12.0 dasselbe.
        var result = InputCommandParser.Parse("""{"t":"moverel","dx":12.0,"dy":-8.0}""");

        var move = Assert.IsType<InputCommand.MoveRelative>(result.Command);
        Assert.Equal(12, move.Dx);
    }

    [Theory]
    [InlineData("left", MouseButton.Left)]
    [InlineData("right", MouseButton.Right)]
    [InlineData("middle", MouseButton.Middle)]
    [InlineData("RIGHT", MouseButton.Right)]
    public void Parst_alle_Maustasten(string name, MouseButton expected)
    {
        var result = InputCommandParser.Parse($$"""{"t":"click","button":"{{name}}"}""");

        var click = Assert.IsType<InputCommand.Click>(result.Command);
        Assert.Equal(expected, click.Button);
    }

    [Fact]
    public void Nimmt_linke_Maustaste_an_wenn_nicht_angegeben()
    {
        var result = InputCommandParser.Parse("""{"t":"click"}""");

        var click = Assert.IsType<InputCommand.Click>(result.Command);
        Assert.Equal(MouseButton.Left, click.Button);
    }

    [Fact]
    public void Trennt_Down_und_Up_fuer_Halten_und_Ziehen()
    {
        var down = InputCommandParser.Parse("""{"t":"down","button":"left"}""");
        var up = InputCommandParser.Parse("""{"t":"up","button":"left"}""");

        Assert.IsType<InputCommand.ButtonDown>(down.Command);
        Assert.IsType<InputCommand.ButtonUp>(up.Command);
    }

    [Fact]
    public void Parst_Scrollen_in_beide_Richtungen()
    {
        var result = InputCommandParser.Parse("""{"t":"scroll","dy":3,"dx":-1}""");

        var scroll = Assert.IsType<InputCommand.Scroll>(result.Command);
        Assert.Equal(3, scroll.Vertical);
        Assert.Equal(-1, scroll.Horizontal);
    }

    [Fact]
    public void Lehnt_Scrollen_ohne_Richtung_ab()
    {
        var result = InputCommandParser.Parse("""{"t":"scroll","dy":0,"dx":0}""");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Lehnt_absurde_Scroll_Betraege_ab()
    {
        var result = InputCommandParser.Parse("""{"t":"scroll","dy":99999}""");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parst_Tastenkombination_mit_mehreren_Modifiern()
    {
        // Strg+Shift+Esc — Task-Manager.
        var result = InputCommandParser.Parse(
            """{"t":"key","key":"escape","mods":["ctrl","shift"]}""");

        var combo = Assert.IsType<InputCommand.KeyCombo>(result.Command);
        Assert.Equal(0x1B, combo.VirtualKey);
        Assert.Equal([VirtualKeys.VK_CONTROL, VirtualKeys.VK_SHIFT], combo.Modifiers);
    }

    [Fact]
    public void Parst_Tastenkombination_ohne_Modifier()
    {
        var result = InputCommandParser.Parse("""{"t":"key","key":"f5"}""");

        var combo = Assert.IsType<InputCommand.KeyCombo>(result.Command);
        Assert.Equal(0x74, combo.VirtualKey);
        Assert.Empty(combo.Modifiers);
    }

    [Fact]
    public void Parst_Text_Eingabe()
    {
        var result = InputCommandParser.Parse("""{"t":"text","text":"Hallo Welt"}""");

        var text = Assert.IsType<InputCommand.TypeText>(result.Command);
        Assert.Equal("Hallo Welt", text.Text);
    }

    [Fact]
    public void Lehnt_uebermaessig_langen_Text_ab()
    {
        var payload = $$"""{"t":"text","text":"{{new string('a', 5000)}}"}""";

        var result = InputCommandParser.Parse(payload);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("[]")]
    [InlineData("""{"x":1}""")]
    [InlineData("""{"t":"unbekannt"}""")]
    [InlineData("""{"t":"move"}""")]
    [InlineData("""{"t":"key","key":"gibtsnicht"}""")]
    [InlineData("""{"t":"key","key":"a","mods":["gibtsnicht"]}""")]
    [InlineData("""{"t":"click","button":"viertetaste"}""")]
    public void Lehnt_fehlerhafte_Eingaben_ab_ohne_zu_werfen(string payload)
    {
        // Act
        var result = InputCommandParser.Parse(payload);

        // Assert — sauberes Fail-Ergebnis statt Exception, damit ein einzelner
        // kaputter Frame nicht den ganzen Socket reißt.
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Lehnt_negativen_Monitor_Index_ab()
    {
        var result = InputCommandParser.Parse("""{"t":"move","monitor":-1,"x":0.5,"y":0.5}""");

        Assert.False(result.IsSuccess);
    }
}
