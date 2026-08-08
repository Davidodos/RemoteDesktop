using System.Security.Principal;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>Was nur mit Administratorrechten geht.</summary>
public enum AdminTask
{
    /// <summary>
    /// Den Agent als geplante Aufgabe eintragen — siehe
    /// <see cref="RemoteDesktopSetup.AgentTask"/> für den Grund, warum es kein
    /// Dienst mehr ist.
    /// </summary>
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
    ServiceStartType,

    /// <summary>
    /// Die ganze Einrichtung in einem Zug: Netzprofil schreiben, Dienst
    /// eintragen, Starttyp setzen, starten.
    ///
    /// Einer statt vier, weil jeder einzelne Sprung eine Rückfrage von Windows
    /// kostet — siehe <see cref="RemoteDesktopSetup.SetupRequest"/>.
    /// </summary>
    Complete
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

    /// <summary>
    /// Der Datenordner: <c>data\</c> neben dem Programm. Dort liegt alles, was
    /// Rechte verlangt — Schlüssel, Zertifikate, Kopplungen, Netzprofil.
    ///
    /// <para>
    /// Ein Ordner statt zweier: siehe <see cref="AgentPaths"/>. Lesen darf hier
    /// jeder, schreiben nur der erhöhte Aufruf.
    /// </para>
    /// </summary>
    public static string DataDirectory { get; } = AgentPaths.For(AppContext.BaseDirectory);

    /// <summary>Wo die Daten bis v1.2.0 lagen — nur noch zum Übernehmen.</summary>
    public static string LegacyDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        AgentPaths.LegacyFolderName);

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

        // Der erhöhte Aufruf ist die einzige Gelegenheit, in beide Ordner zu
        // schreiben. Also zieht er nebenbei um, was eine ältere Fassung
        // hinterlassen hat — auch dann, wenn der Agent nie startet, weil er
        // gar nicht eingerichtet ist.
        AgentPaths.Adopt(AppContext.BaseDirectory, LegacyDataDirectory);

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
        AdminTask.InstallService => InstallTask(argument),
        AdminTask.RemoveService => Remove(),
        AdminTask.StartService => Schtasks(["/Run", "/TN", AgentTask.Name]),
        AdminTask.StopService => Schtasks(["/End", "/TN", AgentTask.Name]),
        AdminTask.ServiceStartType => InstallTask(argument),
        AdminTask.FetchCertificate => FetchCertificate(argument),
        AdminTask.Complete => Complete(argument),
        _ => WriteNetwork(argument)
    };

    /// <summary>
    /// Der Abschluss der Einrichtung, erhöht und in einem Stück.
    ///
    /// <para>
    /// Die Reihenfolge ist nicht beliebig: erst das Netzprofil, dann der Dienst.
    /// Der Agent liest das Profil beim Start — stünde es noch nicht da, liefe er
    /// mit einem Zertifikat auf den falschen Namen los, und der QR-Code enthielte
    /// eine Adresse, die niemand erreicht.
    /// </para>
    /// </summary>
    private static RunResult Complete(string preparedFile)
    {
        SetupRequest? request;

        try
        {
            request = SetupRequest.Read(File.ReadAllText(preparedFile));
        }
        catch (Exception failure)
        {
            return new RunResult(-1, string.Empty, failure.Message);
        }

        if (request is null)
        {
            return new RunResult(
                -1, string.Empty, "Die vorbereitete Einrichtung war nicht lesbar.");
        }

        try
        {
            Directory.CreateDirectory(DataDirectory);

            File.WriteAllText(
                Path.Combine(DataDirectory, NetworkConfig.FileName),
                NetworkConfig.Write(request.Profile.Normalized()));
        }
        catch (Exception failure)
        {
            return new RunResult(-1, string.Empty, $"Das Netzprofil blieb ungeschrieben: {failure.Message}");
        }

        if (request.Agent == AgentSetup.None)
        {
            return new RunResult(0, "Eingerichtet — ohne Agent, dieser Rechner steuert nur.", string.Empty);
        }

        // Das Zertifikat von Tailscale vor dem Start: der Agent sieht beim
        // Hochfahren nach, ob es daliegt, und stellt sich sonst selbst eins aus.
        // Danach zu holen hieße, ihn gleich wieder neu starten zu müssen.
        var certificate = request.Certificate
            ? FetchCertificate(request.Profile.AdvertisedAddress ?? string.Empty)
            : new RunResult(0, string.Empty, string.Empty);

        var installed = InstallTask(
            AgentTask.Argument(request.Agent == AgentSetup.Automatic, request.User));

        if (!installed.Ok)
        {
            return installed;
        }

        var started = Schtasks(["/Run", "/TN", AgentTask.Name]);

        return started.Ok
            ? new RunResult(
                0,
                certificate.Ok
                    ? "Eingerichtet. Der Agent läuft."
                    : "Eingerichtet, der Agent läuft — nur das Zertifikat von Tailscale kam "
                      + $"nicht: {certificate.Message}",
                string.Empty)
            : new RunResult(
                -1,
                string.Empty,
                "Eingerichtet, aber der Agent ließ sich nicht starten: " + started.Message);
    }

    /// <summary>
    /// Die Aufgabe wird angelegt, aber nicht gestartet — das ist ein eigener
    /// Knopf. Ein „Einrichten", das nebenbei losläuft, nähme dem Nutzer die
    /// Entscheidung ab, die er gerade trifft.
    ///
    /// <para>
    /// Ein alter Dienst aus v1.2 wird dabei entfernt. Er muss weg, nicht nur der
    /// Ordnung halber: er hielte Port 8443 belegt, und die Aufgabe käme gar
    /// nicht erst zum Lauschen.
    /// </para>
    /// </summary>
    private static RunResult InstallTask(string argument)
    {
        var binary = AgentBinary.Locate();

        if (binary is null)
        {
            return new RunResult(
                -1, string.Empty,
                "RemoteDesktopAgent.exe liegt nicht neben diesem Programm. "
                + "Dann ist die Installation unvollständig — den Installer noch einmal ausführen.");
        }

        var (atLogon, user) = AgentTask.ReadArgument(argument);

        if (user.Length == 0)
        {
            // Ohne Benutzer wüsste die Aufgabe nicht, in wessen Sitzung sie
            // laufen soll — und genau darum geht es bei diesem Umbau.
            user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        }

        DropLegacyService();

        var definition = Path.Combine(
            Path.GetTempPath(), $"remotedesktop-task-{Guid.NewGuid():N}.xml");

        try
        {
            // UTF-16: `schtasks /Create /XML` liest die Datei als Unicode. Mit
            // UTF-8 quittiert es mit „Die Datei ist ungültig" — ohne zu sagen,
            // dass es an der Kodierung liegt.
            File.WriteAllText(
                definition,
                AgentTask.Definition(binary, user, atLogon),
                System.Text.Encoding.Unicode);

            return Schtasks(["/Create", "/TN", AgentTask.Name, "/XML", definition, "/F"]);
        }
        catch (Exception failure)
        {
            return new RunResult(-1, string.Empty, failure.Message);
        }
        finally
        {
            try
            {
                File.Delete(definition);
            }
            catch (IOException)
            {
                // Eine Datei im Temp-Ordner, die liegen bleibt, ist kein Grund,
                // dem Nutzer etwas zu melden.
            }
        }
    }

    private static RunResult Remove()
    {
        DropLegacyService();

        return Schtasks(["/Delete", "/TN", AgentTask.Name, "/F"]);
    }

    /// <summary>
    /// Der Dienst aus v1.2 — anhalten und austragen, falls er noch da ist.
    ///
    /// Sein Rückgabewert zählt nicht: dass es ihn nicht gibt, ist der
    /// Normalfall und kein Fehler.
    /// </summary>
    private static void DropLegacyService()
    {
        Sc(["stop", Autostart.ServiceName]);
        Sc(["delete", Autostart.ServiceName]);
    }

    private static RunResult Schtasks(IReadOnlyList<string> arguments) =>
        ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments);

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
