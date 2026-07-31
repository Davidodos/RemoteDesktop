using RemoteDesktopAgent.Actions;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Katalog ist die Stelle, an der aus einer Textdatei die Erlaubnis wird,
/// auf diesem Rechner etwas zu starten. Was hier durchrutscht, ist absichtlich
/// gebaute Remote-Code-Execution — deshalb prüft der Agent beim Start und nicht
/// erst, wenn jemand auf den Knopf drückt.
///
/// Die zweite Zusage dieser Tests ist unscheinbarer und genauso wichtig: die
/// Meldungen nennen die Aktion, um die es geht. Wer die Datei geschrieben hat,
/// soll den Fehler finden, ohne raten zu müssen.
/// </summary>
public class ActionCatalogTests
{
    /// <summary>Im Test liegt jede Datei da, außer sie soll ausdrücklich fehlen.</summary>
    private static readonly Func<string, bool> AllesDa = _ => true;

    private static readonly Func<string, bool> NichtsDa = _ => false;

    [Fact]
    public void Eine_fehlende_Datei_ist_kein_Fehler()
    {
        // Arrange — ein frisch eingerichteter Rechner hat keine actions.json.
        var pfad = Path.Combine(Path.GetTempPath(), $"gibt-es-nicht-{Guid.NewGuid():N}.json");

        // Act
        var katalog = ActionCatalog.Load(pfad);

        // Assert — er startet, er kann nur nichts.
        Assert.Empty(katalog.Summaries());
    }

    [Fact]
    public void Ein_Prozess_mit_Argumenten_wird_angenommen()
    {
        // Act
        var katalog = ActionCatalog.Parse(
            """
            [{ "id": "obs-aufnahme", "label": "OBS aufnehmen", "icon": "record",
               "type": "process", "file": "C:\\obs\\obs64.exe",
               "args": ["--startrecording"] }]
            """,
            AllesDa);

        // Assert
        var aktion = katalog.Find("obs-aufnahme");

        Assert.NotNull(aktion);
        Assert.Equal(ActionType.Process, aktion.Type);
        Assert.Equal(["--startrecording"], aktion.Args);
    }

    [Fact]
    public void Args_als_Zeichenkette_bricht_den_Start_ab()
    {
        // Genau der Fall, den die Regel verhindern soll: eine Zeichenkette wäre
        // eine Kommandozeile, und eine Kommandozeile lässt sich zusammensetzen.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """
            [{ "id": "obs", "label": "OBS", "type": "process",
               "file": "C:\\obs\\obs64.exe", "args": "--startrecording --x" }]
            """,
            AllesDa));

        Assert.Contains("args", fehler.Message);
        Assert.Contains("Array", fehler.Message);
    }

    [Fact]
    public void Eine_unbekannte_Art_bricht_den_Start_ab()
    {
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """[{ "id": "x", "label": "X", "type": "shell", "file": "C:\\x.exe" }]""",
            AllesDa));

        Assert.Contains("type", fehler.Message);
    }

    [Fact]
    public void Eine_fehlende_Programmdatei_bricht_den_Start_ab()
    {
        // Der Tippfehler soll auffallen, solange jemand am Rechner sitzt — nicht
        // Wochen später, wenn der Knopf am Handy nichts tut.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """[{ "id": "obs", "label": "OBS", "type": "process", "file": "C:\\obs\\obs64.exe" }]""",
            NichtsDa));

        Assert.Contains("obs", fehler.Message);
        Assert.Contains("obs64.exe", fehler.Message);
    }

    [Fact]
    public void Ein_Skript_muss_auf_ps1_enden()
    {
        // Sonst wäre `type: script` ein zweiter Weg, eine beliebige .exe zu
        // starten — vorbei an der Prüfung, die dieser Typ eigentlich ist.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """[{ "id": "backup", "label": "Backup", "type": "script", "file": "C:\\böse.exe" }]""",
            AllesDa));

        Assert.Contains(".ps1", fehler.Message);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("nicht mal eine Adresse")]
    public void Nur_http_und_https_sind_erlaubte_Adressen(string adresse)
    {
        // Ein anderes Schema wäre ein zweiter Weg, Beliebiges zu starten — genau
        // an den Typen vorbei, die das hier absichern sollen.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            $$"""[{ "id": "x", "label": "X", "type": "url", "url": "{{adresse}}" }]""",
            AllesDa));

        Assert.Contains("http", fehler.Message);
    }

    [Fact]
    public void Eine_https_Adresse_wird_angenommen()
    {
        var katalog = ActionCatalog.Parse(
            """[{ "id": "jira", "label": "Jira", "type": "url", "url": "https://example.invalid/x" }]""",
            AllesDa);

        Assert.NotNull(katalog.Find("jira"));
    }

    [Fact]
    public void Eine_unbekannte_Taste_bricht_den_Start_ab()
    {
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """[{ "id": "m2", "label": "Monitor 2", "type": "keys", "chord": ["LWin", "Zauberstab"] }]""",
            AllesDa));

        Assert.Contains("Zauberstab", fehler.Message);
    }

    [Fact]
    public void Eine_bekannte_Kombination_wird_angenommen()
    {
        var katalog = ActionCatalog.Parse(
            """[{ "id": "m2", "label": "Monitor 2", "type": "keys", "chord": ["win", "p"] }]""",
            AllesDa);

        Assert.Equal(["win", "p"], katalog.Find("m2")!.Chord);
    }

    [Fact]
    public void Eine_Sequenz_darf_nur_auf_vorhandene_Aktionen_zeigen()
    {
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """
            [{ "id": "abendmodus", "label": "Abendmodus", "type": "sequence",
               "steps": [{ "action": "gibt-es-nicht" }] }]
            """,
            AllesDa));

        Assert.Contains("gibt-es-nicht", fehler.Message);
    }

    [Fact]
    public void Eine_Sequenz_die_sich_selbst_aufruft_bricht_den_Start_ab()
    {
        // Sonst liefe sie endlos und nähme den Rechner mit — und zwar erst dann,
        // wenn jemand auf den Knopf drückt.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """
            [{ "id": "a", "label": "A", "type": "sequence", "steps": [{ "action": "b" }] },
             { "id": "b", "label": "B", "type": "sequence", "steps": [{ "action": "a" }] }]
            """,
            AllesDa));

        Assert.Contains("selbst auf", fehler.Message);
    }

    [Fact]
    public void Ein_Schritt_ist_entweder_Aktion_oder_Pause_niemals_beides()
    {
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """
            [{ "id": "x", "label": "X", "type": "keys", "chord": ["a"] },
             { "id": "s", "label": "S", "type": "sequence",
               "steps": [{ "action": "x", "delayMs": 500 }] }]
            """,
            AllesDa));

        Assert.Contains("weder genau eine Aktion noch genau eine Pause", fehler.Message);
    }

    [Fact]
    public void Eine_masslose_Pause_bricht_den_Start_ab()
    {
        // Eine Minute ist reichlich für „Fenster geht auf". Alles darüber ist
        // ein vertippter Wert, der die Sequenz still hängen ließe.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """
            [{ "id": "s", "label": "S", "type": "sequence", "steps": [{ "delayMs": 600000 }] }]
            """,
            AllesDa));

        Assert.Contains("60000", fehler.Message);
    }

    [Fact]
    public void Zwei_gleiche_Kennungen_brechen_den_Start_ab()
    {
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """
            [{ "id": "x", "label": "Eins", "type": "keys", "chord": ["a"] },
             { "id": "x", "label": "Zwei", "type": "keys", "chord": ["b"] }]
            """,
            AllesDa));

        Assert.Contains("zweimal", fehler.Message);
    }

    [Theory]
    [InlineData("Groß")]
    [InlineData("mit leerzeichen")]
    [InlineData("../../etc")]
    [InlineData("")]
    public void Eine_untaugliche_Kennung_bricht_den_Start_ab(string id)
    {
        // Die Kennung steht im Pfad von POST /api/actions/{id}/invoke. Was dort
        // nicht hingehört, kommt hier gar nicht erst hinein.
        Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            $$"""[{ "id": "{{id}}", "label": "X", "type": "keys", "chord": ["a"] }]""",
            AllesDa));
    }

    [Fact]
    public void Eine_Aktion_ohne_Beschriftung_bricht_den_Start_ab()
    {
        // Ein Knopf ohne Aufschrift ist am Handy nicht zu unterscheiden.
        var fehler = Assert.Throws<ActionConfigurationException>(() => ActionCatalog.Parse(
            """[{ "id": "x", "type": "keys", "chord": ["a"] }]""",
            AllesDa));

        Assert.Contains("Beschriftung", fehler.Message);
    }

    [Fact]
    public void Die_Uebersicht_verraet_keine_Pfade()
    {
        // Arrange — wer die Liste abfragen darf, muss nicht auch erfahren,
        // welche Software auf dem Rechner liegt und wo.
        var katalog = ActionCatalog.Parse(
            """
            [{ "id": "backup", "label": "Backup", "icon": "archive", "type": "script",
               "file": "C:\\Scripts\\backup.ps1", "confirm": true }]
            """,
            AllesDa);

        // Act
        var uebersicht = Assert.Single(katalog.Summaries());

        // Assert
        Assert.Equal("backup", uebersicht.Id);
        Assert.Equal("Backup", uebersicht.Label);
        Assert.Equal("archive", uebersicht.Icon);
        Assert.Equal("script", uebersicht.Type);
        Assert.True(uebersicht.Confirm);

        var serialisiert = System.Text.Json.JsonSerializer.Serialize(katalog.Summaries());

        Assert.DoesNotContain("backup.ps1", serialisiert);
        Assert.DoesNotContain("Scripts", serialisiert);
    }

    [Fact]
    public void Ohne_confirm_steht_der_Merker_auf_false()
    {
        var katalog = ActionCatalog.Parse(
            """[{ "id": "x", "label": "X", "type": "keys", "chord": ["a"] }]""",
            AllesDa);

        // Die Rückfrage ist die Ausnahme, nicht die Vorgabe — sonst gewöhnt sich
        // jeder das Wegklicken an, und dann schützt sie nichts mehr.
        Assert.False(katalog.Find("x")!.Confirm);
    }
}
