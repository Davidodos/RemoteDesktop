using System.Diagnostics;
using RemoteDesktopAgent.Native;

namespace RemoteDesktopAgent.Actions;

/// <summary>
/// Führt eine Aktion aus, die der Katalog schon für gut befunden hat.
///
/// <para>
/// Hier wird nichts mehr entschieden und nichts mehr zusammengesetzt: der
/// Läufer bekommt eine fertige Aktion und übersetzt sie in Aufrufe. Alle
/// Prüfungen sind beim Start passiert (<see cref="ActionCatalog"/>), weil ein
/// Fehler dort auffällt, solange jemand am Rechner sitzt.
/// </para>
///
/// <para>
/// <b>Kein Aufruf geht über eine Shell.</b> Argumente wandern einzeln in
/// <see cref="ProcessStartInfo.ArgumentList"/>; .NET setzt daraus die Zeile für
/// Windows zusammen und maskiert dabei selbst. Eine von Hand zusammengefügte
/// Zeichenkette wäre die eine Stelle, an der sich etwas einschleusen ließe.
/// </para>
/// </summary>
public sealed class ActionRunner
{
    /// <summary>
    /// Wie lange eine Tastenkombination gedrückt bleibt. Windows verschluckt
    /// Kombinationen, die im selben Augenblick kommen und gehen — vor allem die
    /// mit der Windows-Taste.
    /// </summary>
    private static readonly TimeSpan ChordHold = TimeSpan.FromMilliseconds(30);

    private readonly IActionHost _host;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <param name="wait">
    /// Das Warten wird hereingereicht, damit die Prüfungen belegen können, dass
    /// eine Sequenz ihre Pausen einhält, ohne sie wirklich abzusitzen.
    /// </param>
    public ActionRunner(IActionHost host, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        _host = host;
        _wait = wait ?? ((span, token) => Task.Delay(span, token));
    }

    public async Task RunAsync(
        AgentAction action, ActionCatalog catalog, CancellationToken cancellationToken)
    {
        switch (action.Type)
        {
            case ActionType.Process:
                _host.Start(ProcessStart(action));
                break;

            case ActionType.Script:
                _host.Start(ScriptStart(action));
                break;

            case ActionType.Url:
                _host.Start(UrlStart(action));
                break;

            case ActionType.Keys:
                await SendChordAsync(action, cancellationToken);
                break;

            case ActionType.Sequence:
                await RunSequenceAsync(action, catalog, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unbehandelte Art '{action.Type}'.");
        }
    }

    private static ProcessStartInfo ProcessStart(AgentAction action)
    {
        var start = Bare(action.File!);

        foreach (var argument in action.Args ?? [])
        {
            start.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
        {
            start.WorkingDirectory = action.WorkingDirectory;
        }

        return start;
    }

    /// <summary>
    /// Startet die hinterlegte Datei — nie einen über das Netz gelieferten
    /// Skripttext. <c>-NoProfile</c> hält das Profil des angemeldeten Nutzers
    /// heraus, <c>-File</c> beendet die Argumentliste, sodass der Pfad nicht als
    /// weiterer Schalter gelesen werden kann.
    /// </summary>
    private static ProcessStartInfo ScriptStart(AgentAction action)
    {
        var start = Bare("powershell.exe");

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(action.File!);

        if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
        {
            start.WorkingDirectory = action.WorkingDirectory;
        }

        return start;
    }

    /// <summary>
    /// Der Umweg über den Explorer statt über <c>UseShellExecute</c>: der würde
    /// zwar denselben Standardbrowser öffnen, wäre aber im ganzen Agent die
    /// einzige Stelle, an der Windows entscheidet, was eine Zeichenkette
    /// bedeutet. Diese Stelle soll es nicht geben — auch nicht für eine
    /// Adresse, die der Katalog schon als http(s) bestätigt hat.
    /// </summary>
    private static ProcessStartInfo UrlStart(AgentAction action)
    {
        var start = Bare("explorer.exe");

        start.ArgumentList.Add(action.Url!);

        return start;
    }

    private static ProcessStartInfo Bare(string fileName) => new()
    {
        FileName = fileName,

        // Ohne das läuft der Aufruf über die Shell von Windows, und dann
        // entscheidet sie, was der Dateiname bedeutet. Sie steht hier auf
        // false, damit es keine zweite Auslegung gibt.
        UseShellExecute = false,

        // Der Agent ist ein Dienst ohne Konsole; ein Fenster, das niemand
        // sieht, würde nur die Sitzung blockieren.
        CreateNoWindow = true
    };

    private async Task SendChordAsync(AgentAction action, CancellationToken cancellationToken)
    {
        var keys = action.Chord!
            .Select(name => VirtualKeys.TryResolve(name, out var code) ? code : (ushort)0)
            .ToArray();

        foreach (var key in keys)
        {
            _host.KeyDown(key);
        }

        await _wait(ChordHold, cancellationToken);

        // Rückwärts loslassen: bei Strg+Umschalt+Esc gäbe ein Loslassen von
        // vorn kurzzeitig Umschalt+Esc frei, und das ist eine andere Eingabe.
        foreach (var key in keys.Reverse())
        {
            _host.KeyUp(key);
        }
    }

    private async Task RunSequenceAsync(
        AgentAction action, ActionCatalog catalog, CancellationToken cancellationToken)
    {
        foreach (var step in action.Steps!)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step.DelayMs is { } pause)
            {
                await _wait(TimeSpan.FromMilliseconds(pause), cancellationToken);
                continue;
            }

            // Der Katalog hat beim Start bestätigt, dass es den Schritt gibt und
            // dass die Verweise keinen Kreis bilden.
            await RunAsync(catalog.Find(step.Action!)!, catalog, cancellationToken);
        }
    }
}
