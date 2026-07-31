using System.Diagnostics;
using RemoteDesktopAgent.Actions;
using RemoteDesktopAgent.Native;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Zeichnet auf, was der Läufer nach draußen gibt, statt es zu tun. Nur so
/// lässt sich belegen, <b>wie</b> gestartet wird — und genau das ist die Zusage,
/// an der hier alles hängt.
/// </summary>
public sealed class HostAufzeichnung : IActionHost
{
    public List<ProcessStartInfo> Gestartet { get; } = [];

    /// <summary>Tastenereignisse in der Reihenfolge, in der sie kamen.</summary>
    public List<(ushort Key, bool Down)> Tasten { get; } = [];

    public void Start(ProcessStartInfo start) => Gestartet.Add(start);

    public void KeyDown(ushort virtualKey) => Tasten.Add((virtualKey, true));

    public void KeyUp(ushort virtualKey) => Tasten.Add((virtualKey, false));
}

/// <summary>
/// Der Läufer führt aus, was der Katalog freigegeben hat. Zwei Dinge müssen
/// dabei nachweisbar sein, weil sie den Unterschied zwischen einer Fernbedienung
/// und einer Hintertür ausmachen: Argumente gehen einzeln hinaus, und keine
/// Shell bekommt je eine zusammengesetzte Zeile zu sehen.
/// </summary>
public class ActionRunnerTests
{
    private static readonly Func<string, bool> AllesDa = _ => true;

    /// <summary>
    /// Merkt sich die Pausen, statt sie abzusitzen — sonst dauerte der Testlauf
    /// so lange wie die längste Sequenz.
    /// </summary>
    private sealed class Uhr
    {
        public List<TimeSpan> Pausen { get; } = [];

        public Task Wait(TimeSpan span, CancellationToken _)
        {
            Pausen.Add(span);
            return Task.CompletedTask;
        }
    }

    private static async Task<(HostAufzeichnung Host, Uhr Uhr)> AusfuehrenAsync(string json, string id)
    {
        var katalog = ActionCatalog.Parse(json, AllesDa);
        var host = new HostAufzeichnung();
        var uhr = new Uhr();

        await new ActionRunner(host, uhr.Wait).RunAsync(katalog.Find(id)!, katalog, CancellationToken.None);

        return (host, uhr);
    }

    [Fact]
    public async Task Argumente_gehen_einzeln_hinaus_und_nie_ueber_eine_Shell()
    {
        // Arrange — das zweite Argument enthält genau das, was in einer
        // zusammengesetzten Zeile ein zweiter Befehl wäre.
        var (host, _) = await AusfuehrenAsync(
            """
            [{ "id": "obs", "label": "OBS", "type": "process", "file": "C:\\obs\\obs64.exe",
               "args": ["--startrecording", "& calc.exe"] }]
            """,
            "obs");

        // Assert
        var start = Assert.Single(host.Gestartet);

        Assert.False(start.UseShellExecute);
        Assert.Equal("C:\\obs\\obs64.exe", start.FileName);
        Assert.Equal(["--startrecording", "& calc.exe"], start.ArgumentList);

        // Die zusammengesetzte Zeile bleibt leer: .NET baut sie erst beim Start
        // aus der Liste und maskiert dabei selbst.
        Assert.True(string.IsNullOrEmpty(start.Arguments));
    }

    [Fact]
    public async Task Ein_Prozess_ohne_Argumente_bekommt_eine_leere_Liste()
    {
        var (host, _) = await AusfuehrenAsync(
            """[{ "id": "rechner", "label": "Rechner", "type": "process", "file": "C:\\calc.exe" }]""",
            "rechner");

        Assert.Empty(Assert.Single(host.Gestartet).ArgumentList);
    }

    [Fact]
    public async Task Das_Arbeitsverzeichnis_wird_uebernommen()
    {
        var (host, _) = await AusfuehrenAsync(
            """
            [{ "id": "obs", "label": "OBS", "type": "process", "file": "C:\\obs\\obs64.exe",
               "workingDirectory": "C:\\obs" }]
            """,
            "obs");

        Assert.Equal("C:\\obs", Assert.Single(host.Gestartet).WorkingDirectory);
    }

    [Fact]
    public async Task Ein_Skript_startet_nur_die_hinterlegte_Datei()
    {
        // Arrange
        var (host, _) = await AusfuehrenAsync(
            """
            [{ "id": "backup", "label": "Backup", "type": "script",
               "file": "C:\\Scripts\\backup.ps1" }]
            """,
            "backup");

        // Assert — ausgeführt wird eine Datei, kein über das Netz gelieferter
        // Skripttext. -File beendet die Schalterliste, damit der Pfad nicht als
        // weiterer Schalter gelesen werden kann.
        var start = Assert.Single(host.Gestartet);

        Assert.False(start.UseShellExecute);
        Assert.Equal("powershell.exe", start.FileName);
        Assert.Equal(
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "C:\\Scripts\\backup.ps1"],
            start.ArgumentList);
    }

    [Fact]
    public async Task Eine_Adresse_geht_ohne_Shell_hinaus()
    {
        // Arrange
        var (host, _) = await AusfuehrenAsync(
            """[{ "id": "jira", "label": "Jira", "type": "url", "url": "https://example.invalid/x" }]""",
            "jira");

        // Assert — UseShellExecute wäre der bequeme Weg zum Standardbrowser und
        // im ganzen Agent die einzige Stelle, an der Windows entscheidet, was
        // eine Zeichenkette bedeutet. Diese Stelle soll es nicht geben.
        var start = Assert.Single(host.Gestartet);

        Assert.False(start.UseShellExecute);
        Assert.Equal("explorer.exe", start.FileName);
        Assert.Equal(["https://example.invalid/x"], start.ArgumentList);
    }

    [Fact]
    public async Task Eine_Kombination_wird_rueckwaerts_losgelassen()
    {
        // Arrange
        var (host, _) = await AusfuehrenAsync(
            """[{ "id": "m2", "label": "Monitor 2", "type": "keys", "chord": ["LWin", "p"] }]""",
            "m2");

        // Assert — von vorn loszulassen gäbe bei Strg+Umschalt+Esc kurz
        // Umschalt+Esc frei, und das ist eine andere Eingabe.
        VirtualKeys.TryResolve("p", out var p);

        Assert.Equal(
            [
                (VirtualKeys.VK_LWIN, true),
                (p, true),
                (p, false),
                (VirtualKeys.VK_LWIN, false)
            ],
            host.Tasten);
    }

    [Fact]
    public async Task Eine_Kombination_bleibt_kurz_gedrueckt()
    {
        // Windows verschluckt Kombinationen, die im selben Augenblick kommen und
        // gehen — vor allem die mit der Windows-Taste.
        var (_, uhr) = await AusfuehrenAsync(
            """[{ "id": "m2", "label": "Monitor 2", "type": "keys", "chord": ["LWin", "p"] }]""",
            "m2");

        Assert.Equal(TimeSpan.FromMilliseconds(30), Assert.Single(uhr.Pausen));
    }

    [Fact]
    public async Task Eine_Sequenz_haelt_ihre_Verzoegerungen_ein()
    {
        // Arrange — zwei Aktionen mit einer erklärten Pause dazwischen.
        var (host, uhr) = await AusfuehrenAsync(
            """
            [{ "id": "m2", "label": "Monitor 2", "type": "keys", "chord": ["LWin", "p"] },
             { "id": "obs", "label": "OBS", "type": "process", "file": "C:\\obs\\obs64.exe" },
             { "id": "abendmodus", "label": "Abendmodus", "type": "sequence",
               "steps": [{ "action": "m2" }, { "delayMs": 500 }, { "action": "obs" }] }]
            """,
            "abendmodus");

        // Assert — die 500 ms stehen zwischen den beiden Schritten. Die 30 ms
        // davor sind das Halten der Kombination aus dem ersten Schritt; ohne die
        // Reihenfolge wäre die Aussage wertlos.
        Assert.Equal(
            [TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(500)],
            uhr.Pausen);

        Assert.Equal("C:\\obs\\obs64.exe", Assert.Single(host.Gestartet).FileName);
    }

    [Fact]
    public async Task Eine_Sequenz_fuehrt_ihre_Schritte_der_Reihe_nach_aus()
    {
        var (host, _) = await AusfuehrenAsync(
            """
            [{ "id": "eins", "label": "Eins", "type": "process", "file": "C:\\eins.exe" },
             { "id": "zwei", "label": "Zwei", "type": "process", "file": "C:\\zwei.exe" },
             { "id": "beide", "label": "Beide", "type": "sequence",
               "steps": [{ "action": "eins" }, { "action": "zwei" }] }]
            """,
            "beide");

        Assert.Equal(
            ["C:\\eins.exe", "C:\\zwei.exe"],
            host.Gestartet.Select(start => start.FileName));
    }

    [Fact]
    public async Task Eine_Sequenz_darf_verschachtelt_sein()
    {
        // Kreise hat der Katalog beim Start ausgeschlossen; Verschachtelung an
        // sich ist erlaubt und praktisch — „Abendmodus" ruft „Bildschirm" auf.
        var (host, _) = await AusfuehrenAsync(
            """
            [{ "id": "innen", "label": "Innen", "type": "process", "file": "C:\\innen.exe" },
             { "id": "mitte", "label": "Mitte", "type": "sequence", "steps": [{ "action": "innen" }] },
             { "id": "aussen", "label": "Außen", "type": "sequence", "steps": [{ "action": "mitte" }] }]
            """,
            "aussen");

        Assert.Equal("C:\\innen.exe", Assert.Single(host.Gestartet).FileName);
    }

    [Fact]
    public async Task Ein_Abbruch_haelt_die_Sequenz_an()
    {
        // Arrange — der Client hat die Verbindung getrennt, während die Sequenz
        // lief. Die restlichen Schritte sollen dann nicht mehr kommen.
        var katalog = ActionCatalog.Parse(
            """
            [{ "id": "eins", "label": "Eins", "type": "process", "file": "C:\\eins.exe" },
             { "id": "beide", "label": "Beide", "type": "sequence",
               "steps": [{ "action": "eins" }, { "action": "eins" }] }]
            """,
            AllesDa);

        var host = new HostAufzeichnung();
        using var abbruch = new CancellationTokenSource();
        abbruch.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ActionRunner(host).RunAsync(katalog.Find("beide")!, katalog, abbruch.Token));

        Assert.Empty(host.Gestartet);
    }

    [Fact]
    public void Kein_Fenster_bleibt_stehen()
    {
        // Der Agent ist ein Dienst ohne Konsole. Ein Fenster, das niemand sieht,
        // blockierte nur die Sitzung.
        var katalog = ActionCatalog.Parse(
            """[{ "id": "x", "label": "X", "type": "process", "file": "C:\\x.exe" }]""",
            AllesDa);

        var host = new HostAufzeichnung();

        new ActionRunner(host).RunAsync(katalog.Find("x")!, katalog, CancellationToken.None).Wait();

        Assert.True(Assert.Single(host.Gestartet).CreateNoWindow);
    }
}
