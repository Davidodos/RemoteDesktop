using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RemoteDesktopClient;

/// <summary>Was ein gestartetes Programm hinterlassen hat.</summary>
/// <param name="ExitCode">
/// <c>-1</c>, wenn es gar nicht erst startete — etwa weil die Datei nicht da ist.
/// </param>
public sealed record RunResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;

    /// <summary>
    /// Ein Satz, den man jemandem zeigen kann. Erst der Fehlertext, dann die
    /// gewöhnliche Ausgabe, und wenn beides leer ist, wenigstens die Zahl —
    /// „hat nicht geklappt" ohne jeden Hinweis ist die schlechteste aller
    /// Meldungen.
    /// </summary>
    public string Message
    {
        get
        {
            if (Error.Trim().Length > 0)
            {
                return Error.Trim();
            }

            if (Output.Trim().Length > 0)
            {
                return Output.Trim();
            }

            return Ok ? "Erledigt." : $"Das Programm endete mit Rückgabewert {ExitCode}.";
        }
    }
}

/// <summary>
/// Programme starten, ohne dass ein Fenster aufblitzt und ohne dass ein
/// Fehlschlag stumm bleibt.
///
/// <para>
/// **Der Befund dahinter:** „Zertifikat holen" öffnete kurz ein Terminal und
/// schloss es sofort wieder. Ursache war
/// <c>ProcessStartInfo { UseShellExecute = false }</c> ohne
/// <c>CreateNoWindow</c> — ein Konsolenprogramm aus einem Fenster heraus bringt
/// sein eigenes Fenster mit. Und weil die Ausgabe nirgends hinging, blieb auch
/// der Grund des Fehlschlags dort stehen, wo ihn niemand las.
/// </para>
/// </summary>
public static class ProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Startet ein Programm, wartet auf sein Ende und bringt mit, was es gesagt
    /// hat. Argumente einzeln, nie als eine Zeile — dieselbe Regel wie bei den
    /// Aktionen des Agents: es gibt keine Stelle, an der Windows entscheidet,
    /// was eine Zeichenkette bedeutet.
    /// </summary>
    public static RunResult Run(
        string file, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // Der eigentliche Punkt dieser Datei.
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);

            if (process is null)
            {
                return new RunResult(-1, string.Empty, $"„{file}“ ließ sich nicht starten.");
            }

            var output = new StringBuilder();
            var error = new StringBuilder();

            // Nebenläufig lesen: wer erst wartet und dann liest, hängt, sobald
            // das Programm mehr ausgibt, als in den Puffer passt.
            process.OutputDataReceived += (_, line) => output.AppendLine(line.Data);
            process.ErrorDataReceived += (_, line) => error.AppendLine(line.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)(timeout ?? DefaultTimeout).TotalMilliseconds))
            {
                return new RunResult(
                    -1, output.ToString(),
                    $"„{file}“ antwortet nicht und wurde abgebrochen.");
            }

            return new RunResult(process.ExitCode, output.ToString(), error.ToString());
        }
        catch (Win32Exception failure)
        {
            // Der häufige Fall: das Programm liegt nicht im Suchpfad. Vorher
            // sah das aus wie „es passiert nichts".
            return new RunResult(
                -1, string.Empty,
                $"„{file}“ wurde nicht gefunden oder ließ sich nicht starten: {failure.Message}");
        }
    }

    /// <summary>
    /// Dasselbe mit Adminrechten. Windows fragt dabei nach, und die Ausgabe
    /// lässt sich nicht mitlesen — beides gehört zusammen: eine erhöhte
    /// Anfrage geht über die Shell, und die reicht keine Kanäle durch.
    /// </summary>
    /// <returns>
    /// Nur der Rückgabewert. Was dabei schiefging, muss der erhöhte Aufruf
    /// selbst hinterlassen; dafür gibt es <see cref="Elevation"/>.
    /// </returns>
    public static RunResult RunElevated(
        string file, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);

            if (process is null)
            {
                return new RunResult(-1, string.Empty, $"„{file}“ ließ sich nicht starten.");
            }

            if (!process.WaitForExit((int)(timeout ?? DefaultTimeout).TotalMilliseconds))
            {
                return new RunResult(-1, string.Empty, "Der Vorgang antwortet nicht.");
            }

            return new RunResult(process.ExitCode, string.Empty, string.Empty);
        }
        catch (Win32Exception failure)
        {
            // 1223 heißt: der Nutzer hat die Nachfrage weggeklickt. Das ist kein
            // Fehler, sondern eine Entscheidung, und sie soll auch so klingen.
            return new RunResult(
                -1, string.Empty,
                failure.NativeErrorCode == 1223
                    ? "Abgebrochen — für diesen Schritt braucht es Administratorrechte."
                    : failure.Message);
        }
    }
}
