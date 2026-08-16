namespace RemoteDesktopSetup;

/// <summary>
/// Wo die Daten liegen, die zu dieser Installation gehören: Schlüssel,
/// Zertifikate, gekoppelte Geräte und das Netzprofil.
///
/// <para>
/// **Der Befund dahinter:** bis v1.2.0 lagen sie an zwei Orten. Der private
/// Schlüssel des Agents, die Kopplungen und die Aktionen standen neben der
/// <c>.exe</c> in <c>C:\Program Files\RemoteDesktop</c>; die Zertifikate und
/// das Netzprofil in <c>C:\ProgramData\RemoteDesktopAgent</c>. Wer aufräumen
/// wollte, musste beides wissen — und eine Deinstallation ließ die Hälfte
/// liegen.
/// </para>
///
/// <para>
/// Jetzt ist es ein Ordner: <c>data\</c> neben dem Programm. Ein Update
/// überschreibt ihn nicht (der Installer kennt ihn nicht), eine Deinstallation
/// räumt ihn weg (<c>[UninstallDelete]</c>), und wer von Hand aufräumt, löscht
/// einen Ordner statt zwei.
/// </para>
/// </summary>
public static class AgentPaths
{
    /// <summary>Der Unterordner neben der Programmdatei.</summary>
    public const string FolderName = "data";

    /// <summary>Wo die Daten bis v1.2.0 lagen — nur noch zum Übernehmen.</summary>
    public const string LegacyFolderName = "RemoteDesktopAgent";

    /// <summary>
    /// Die Dateien, die dorthin gehören.
    ///
    /// Ausdrücklich aufgezählt und nicht „alles, was da liegt": neben der
    /// <c>.exe</c> steht auch das Programm selbst, und das gehört nicht in den
    /// Datenordner.
    /// </summary>
    public static readonly IReadOnlyList<string> Files =
    [
        "agentkey.txt",
        "clients.json",
        "cert.crt",
        "cert.key",
        ServerCertificateFile,
        AuthorityFile,
        AuthorityPublicFile,
        ClientKeyFile.FileName,
        CoordinatorConfig.FileName,
        DeviceNameFile.FileName
    ];

    /// <summary>
    /// Das Zertifikat, das der Agent beim Verbinden vorzeigt — mitsamt seinem
    /// privaten Schlüssel.
    ///
    /// Die drei Namen stehen hier und nicht nur im Agent, weil das Fenster sie
    /// ebenfalls braucht: bei gestopptem Agent liest es den eigenen Steckbrief
    /// aus denselben Dateien, die der Agent sonst anböte.
    /// </summary>
    public const string ServerCertificateFile = "agent.pfx";

    /// <summary>Die eigene CA — der Anker, dem ein Client einmal vertraut.</summary>
    public const string AuthorityFile = "agentca.pfx";

    /// <summary>Ihr öffentlicher Teil, so wie ihn ein Client zum Bestätigen bekommt.</summary>
    public const string AuthorityPublicFile = "agentca.crt";

    /// <summary>Der private Schlüssel des Agents — seine Kennung nach außen.</summary>
    public const string IdentityFile = "agentkey.txt";

    /// <summary>Wer diesen Rechner steuern darf.</summary>
    public const string ClientsFileName = "clients.json";

    /// <summary>Die Steckbriefe, die beim Koppeln hier abgegeben wurden.</summary>
    public const string PeersFileName = "peers.json";

    /// <summary>Der Datenordner zu einer Installation.</summary>
    public static string For(string installDirectory) =>
        Path.Combine(installDirectory, FolderName);

    /// <summary>
    /// Was von wo übernommen werden muss — jeweils Quelle und Ziel.
    ///
    /// <para>
    /// Zwei alte Orte, ein neuer. Übernommen wird nur, was drüben liegt und
    /// hier noch fehlt: eine bestehende Datei zu überschreiben hieße, eine
    /// laufende Installation mit ihrem eigenen Vorgänger zu übermalen.
    /// </para>
    /// </summary>
    /// <param name="installDirectory">Der Ordner der Programmdateien.</param>
    /// <param name="legacyDirectory">
    /// Der alte Ordner unter <c>ProgramData</c>.
    /// </param>
    public static IReadOnlyList<(string From, string To)> Moves(
        string installDirectory, string legacyDirectory)
    {
        var target = For(installDirectory);

        return Files
            .SelectMany(file => new[]
            {
                (From: Path.Combine(legacyDirectory, file), To: Path.Combine(target, file)),
                (From: Path.Combine(installDirectory, file), To: Path.Combine(target, file))
            })
            .ToList();
    }

    /// <summary>
    /// Holt die Daten einer älteren Installation herüber.
    ///
    /// <para>
    /// Verschoben und nicht kopiert: der alte Ordner soll danach leer sein,
    /// sonst gibt es wieder zwei Orte. Was sich nicht verschieben lässt — eine
    /// Datei in Benutzung, ein fehlendes Recht —, wird kopiert; und was auch
    /// das nicht überlebt, bleibt liegen. Ein gescheiterter Umzug darf den Start
    /// nicht aufhalten: die Kopplungen wären dann weg, aber der Rechner liefe
    /// wenigstens.
    /// </para>
    /// </summary>
    /// <returns>Die Dateien, die tatsächlich umgezogen sind.</returns>
    public static IReadOnlyList<string> Adopt(string installDirectory, string legacyDirectory)
    {
        var adopted = new List<string>();

        foreach (var (from, to) in Moves(installDirectory, legacyDirectory))
        {
            if (!File.Exists(from) || File.Exists(to) || from == to)
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(For(installDirectory));
                File.Move(from, to);
                adopted.Add(Path.GetFileName(to));
            }
            catch (Exception)
            {
                try
                {
                    File.Copy(from, to, overwrite: false);
                    adopted.Add(Path.GetFileName(to));
                }
                catch (Exception)
                {
                    // Bleibt liegen. Der Agent stellt sich dann neue Schlüssel
                    // aus — unangenehm, aber kein Grund, nicht zu starten.
                }
            }
        }

        return adopted;
    }

    /// <summary>
    /// Ein Pfad aus einer alten Konfiguration, auf den neuen Ordner gebogen.
    ///
    /// <para>
    /// Die <c>appsettings.json</c> wird bei einem Update **nicht** ersetzt —
    /// sonst wären eingetragene Sonderfälle weg. In einer alten steht aber der
    /// alte Ordner, und ohne diese Umleitung suchte der Agent sein Zertifikat
    /// weiter dort, während alles andere längst umgezogen ist.
    /// </para>
    /// </summary>
    public static string? Redirect(string? configured, string dataDirectory, string legacyDirectory)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var folder = Path.GetDirectoryName(configured);

        return folder is not null
               && folder.TrimEnd(Path.DirectorySeparatorChar)
                   .Equals(legacyDirectory.TrimEnd(Path.DirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(dataDirectory, Path.GetFileName(configured))
            : configured;
    }
}
