using System.Security.Principal;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>Was nur mit Administratorrechten geht.</summary>
public enum AdminTask
{
    /// <summary>Den Agent als Dienst eintragen.</summary>
    InstallService,

    /// <summary>Den Eintrag entfernen. Die Dateien und die Kopplungen bleiben.</summary>
    RemoveService,

    StartService,

    StopService,

    /// <summary>
    /// Das Zertifikat von Tailscale holen und dorthin schreiben, wo der Agent es
    /// sucht.
    /// </summary>
    FetchCertificate,

    /// <summary>
    /// Das Netzprofil in die Datei des Agents schreiben. Auch das braucht
    /// Rechte: der Ordner gehört Administratoren und dem System, weil daneben
    /// der private Schlüssel des Agents liegt.
    /// </summary>
    WriteNetwork,

    /// <summary>Den Starttyp des Dienstes umstellen.</summary>
    ServiceStartType
}

/// <summary>
/// Der Sprung auf Administratorrechte.
///
/// <para>
/// Das Fenster läuft bewusst ohne sie — es zeigt einen Bildschirm an und
/// braucht sonst nichts. Für die wenigen Handgriffe, die mehr verlangen, ruft es
/// sich selbst noch einmal auf, diesmal erhöht, mit genau einem Auftrag. Windows
/// fragt dabei nach; danach endet der erhöhte Aufruf sofort wieder.
/// </para>
///
/// <para>
/// Das Ergebnis kommt über eine Datei zurück und nicht über den Rückgabewert
/// allein: „hat nicht geklappt" ohne Grund ist die schlechteste aller Meldungen,
/// und über die Shell lässt sich keine Ausgabe mitlesen.
/// </para>
/// </summary>
public static class Elevation
{
    public const string TaskSwitch = "--admin-task";
    public const string ResultSwitch = "--result";

    /// <summary>Der Ordner des Agents. Dort liegt alles, was Rechte verlangt.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RemoteDesktopAgent");

    public static bool IsElevated =>
        OperatingSystem.IsWindows()
        && new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>
    /// Führt einen Auftrag mit Adminrechten aus — erhöht sich dafür selbst,
    /// falls nötig.
    /// </summary>
    /// <param name="argument">
    /// Der eine Zusatz, den ein Auftrag braucht: der Tailnet-Name, der
    /// Starttyp, oder der Pfad einer vorbereiteten Datei. Mehr als einen gibt es
    /// mit Absicht nicht — jede weitere Zeichenkette wäre eine weitere Stelle,
    /// an der etwas falsch zusammengesetzt werden kann.
    /// </param>
    public static RunResult Run(AdminTask task, string argument = "")
    {
        if (IsElevated)
        {
            return Perform(task, argument);
        }

        var own = Environment.ProcessPath;

        if (own is null)
        {
            return new RunResult(-1, string.Empty, "Der eigene Programmpfad ist unbekannt.");
        }

        var resultPath = Path.Combine(
            Path.GetTempPath(), $"remotedesktop-{Guid.NewGuid():N}.txt");

        var outcome = ProcessRunner.RunElevated(
            own,
            [TaskSwitch, task.ToString(), argument, ResultSwitch, resultPath]);

        var message = ReadAndDelete(resultPath);

        return message is null
            ? outcome
            : outcome with { Error = outcome.Ok ? string.Empty : message, Output = message };
    }

    /// <summary>
    /// Die Gegenseite: dieser Aufruf *ist* der erhöhte. Er tut genau eine Sache
    /// und beendet sich.
    /// </summary>
    /// <returns>Der Rückgabewert für das Programm.</returns>
    public static int Execute(IReadOnlyList<string> args)
    {
        var task = Enum.TryParse<AdminTask>(Value(args, TaskSwitch), out var parsed)
            ? parsed
            : (AdminTask?)null;

        if (task is null)
        {
            return 2;
        }

        var argument = Argument(args);
        var result = Perform(task.Value, argument);

        if (Value(args, ResultSwitch) is { Length: > 0 } resultPath)
        {
            try
            {
                File.WriteAllText(resultPath, result.Message);
            }
            catch (IOException)
            {
                // Der Rückgabewert kommt trotzdem an. Ein Fehlschlag beim
                // Berichten darf den Bericht nicht ersetzen.
            }
        }

        return result.Ok ? 0 : 1;
    }

    /// <summary>
    /// Was tatsächlich geschieht. Getrennt vom Sprung, damit derselbe Code
    /// läuft, egal ob das Fenster schon erhöht war oder nicht.
    /// </summary>
    private static RunResult Perform(AdminTask task, string argument) => task switch
    {
        AdminTask.InstallService => InstallService(argument),
        AdminTask.RemoveService => Sc(["delete", Autostart.ServiceName]),
        AdminTask.StartService => Tolerate(
            Sc(["start", Autostart.ServiceName]), AlreadyRunning, "Der Agent läuft bereits."),
        AdminTask.StopService => Tolerate(
            Sc(["stop", Autostart.ServiceName]), NotRunning, "Der Agent läuft gar nicht."),
        AdminTask.ServiceStartType => Sc(
            ["config", Autostart.ServiceName, "start=", argument == "auto" ? "auto" : "demand"]),
        AdminTask.FetchCertificate => FetchCertificate(argument),
        _ => WriteNetwork(argument)
    };

    /// <summary>
    /// Der Dienst wird angelegt und beschrieben, aber nicht gestartet — das ist
    /// ein eigener Knopf. Ein „Einrichten", das nebenbei losläuft, nähme dem
    /// Nutzer die Entscheidung ab, die er gerade trifft.
    /// </summary>
    private static RunResult InstallService(string startType)
    {
        var binary = AgentBinary.Locate();

        if (binary is null)
        {
            return new RunResult(
                -1, string.Empty,
                "RemoteDesktopAgent.exe liegt nicht neben diesem Programm. "
                + "Dann ist die Installation unvollständig — den Installer noch einmal ausführen.");
        }

        var start = startType == "demand" ? "demand" : "auto";

        // Jedes Stück einzeln: `sc.exe` erwartet den Wert als eigenes Wort hinter
        // dem Schlüssel mit Gleichheitszeichen. Als eine Zeichenkette
        // („binPath= C:\…") käme beides zusammen an, und der Dienst zeigte auf
        // nichts.
        var created = Sc([
            "create", Autostart.ServiceName,
            "binPath=", binary,
            "start=", start,
            "DisplayName=", "RemoteDesktop Agent"
        ]);

        if (!created.Ok)
        {
            return created;
        }

        Sc([
            "description", Autostart.ServiceName,
            "Macht diesen Rechner über RemoteDesktop fernsteuerbar."
        ]);


        return created;
    }

    /// <summary>
    /// <c>tailscale cert</c> — mit ausdrücklichem Ziel.
    ///
    /// <para>
    /// **Der zweite Teil des Befunds:** ohne <c>--cert-file</c> und
    /// <c>--key-file</c> legt Tailscale die beiden Dateien im Arbeitsverzeichnis
    /// ab. Das war bei einem Fenster aus <c>C:\Program Files</c> ein Ordner, in
    /// den es gar nicht schreiben darf — und selbst wenn: der Agent sucht sie
    /// woanders. Der Schritt konnte nie „erledigt" werden.
    /// </para>
    /// </summary>
    private static RunResult FetchCertificate(string tailnetName)
    {
        if (tailnetName.Trim().Length == 0)
        {
            return new RunResult(
                -1, string.Empty,
                "Dieser Rechner hat noch keinen Namen im Tailscale-Netz. "
                + "Zuerst anmelden, dann das Zertifikat holen.");
        }

        Directory.CreateDirectory(DataDirectory);

        return ProcessRunner.Run(Tailscale.Executable, [
            "cert",
            "--cert-file", Path.Combine(DataDirectory, "cert.crt"),
            "--key-file", Path.Combine(DataDirectory, "cert.key"),
            tailnetName.Trim()
        ]);
    }

    /// <summary>
    /// Das vorbereitete Netzprofil an seinen Platz kopieren. Der Inhalt kommt
    /// als Datei und nicht als Argument: JSON auf einer Kommandozeile ist eine
    /// Einladung an jedes Anführungszeichen, etwas anderes zu bedeuten.
    /// </summary>
    private static RunResult WriteNetwork(string preparedFile)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.Copy(preparedFile, Path.Combine(DataDirectory, NetworkConfig.FileName), true);

            return new RunResult(0, "Gespeichert.", string.Empty);
        }
        catch (Exception failure)
        {
            return new RunResult(-1, string.Empty, failure.Message);
        }
    }

    /// <summary>Windows-Fehler 1056: der Dienst läuft schon.</summary>
    private const int AlreadyRunning = 1056;

    /// <summary>Windows-Fehler 1062: der Dienst läuft gar nicht.</summary>
    private const int NotRunning = 1062;

    /// <summary>
    /// Ein Rückgabewert, der gar kein Fehlschlag ist.
    ///
    /// <para>
    /// „Starten“ an einem laufenden Dienst und „Beenden“ an einem stehenden sind
    /// keine Fehler, sondern Wünsche, die schon erfüllt sind. <c>sc.exe</c> sieht
    /// das anders und liefert einen Fehlercode; ungedeutet stand danach eine rote
    /// Meldung im Fenster, obwohl alles in Ordnung war.
    /// </para>
    /// </summary>
    private static RunResult Tolerate(RunResult result, int code, string message) =>
        result.ExitCode == code ? new RunResult(0, message, string.Empty) : result;

    private static RunResult Sc(IReadOnlyList<string> arguments) =>
        ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments);

    /// <summary>
    /// Der Wert hinter einem Schalter. Fehlt er, ist es eine leere Zeichenkette
    /// und kein Absturz — dieser Aufruf kommt zwar nur von uns selbst, aber
    /// darauf soll sich nichts verlassen, was mit Adminrechten läuft.
    /// </summary>
    private static string Value(IReadOnlyList<string> args, string name)
    {
        var index = args.ToList().IndexOf(name);

        return index >= 0 && index + 1 < args.Count ? args[index + 1] : string.Empty;
    }

    /// <summary>Der Zusatz steht unmittelbar hinter dem Auftrag.</summary>
    private static string Argument(IReadOnlyList<string> args)
    {
        var index = args.ToList().IndexOf(TaskSwitch);

        return index >= 0 && index + 2 < args.Count && args[index + 2] != ResultSwitch
            ? args[index + 2]
            : string.Empty;
    }

    private static string? ReadAndDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var content = File.ReadAllText(path);
            File.Delete(path);

            return content.Trim().Length == 0 ? null : content.Trim();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
