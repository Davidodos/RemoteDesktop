using System.Text;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Das Protokoll des Agents — in eine Datei, weil es sonst nirgendwohin geht.
///
/// <para>
/// **Der Befund dahinter:** seit der Agent eine <c>WinExe</c> ist (damit kein
/// schwarzes Fenster aufblitzt), schreibt der voreingestellte Konsolen-Logger in
/// eine Konsole, die es nicht gibt. Alles, was der Agent meldet, war damit weg —
/// auch die eine Zeile, die verrät, welches Zertifikat er beim Start gewählt hat,
/// und die, die bei jedem Monitorwechsel Adapter und Ausgang nennt. Zwei Fehler
/// am echten Gerät ließen sich deshalb nicht auseinanderhalten: „passiert nicht"
/// und „passiert, wirkt aber nicht" sahen gleich aus.
/// </para>
///
/// <para>
/// Bewusst ohne fremdes Paket: es geht um Zeilen in eine Datei mit einer Grenze
/// nach oben. Eine Protokollbibliothek dafür einzuziehen wäre mehr Abhängigkeit
/// als Nutzen.
/// </para>
/// </summary>
public sealed class AgentLog : IDisposable
{
    /// <summary>Ab hier wird umgebrochen. Zwei Fassungen bleiben liegen, mehr nicht.</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    public const string FileName = "agent.log";

    private readonly object _gate = new();

    /// <summary>Wo die Datei liegt — dieselbe, die das Fenster zum Öffnen anbietet.</summary>
    public string Location { get; }

    public AgentLog(string directory)
    {
        Location = Path.Combine(directory, FileName);

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception)
        {
            // Ohne Protokoll läuft der Agent trotzdem. Es ist eine Auskunft,
            // keine Voraussetzung.
        }
    }

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Rotate();
                File.AppendAllText(Location, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Eine gesperrte oder volle Platte darf den Agent nicht
                // aufhalten — er tut gerade etwas Wichtigeres als zu berichten.
            }
        }
    }

    /// <summary>
    /// Die alte Fassung beiseitelegen, wenn die aktuelle zu groß wird. Eine
    /// Vorgängerdatei, nicht zehn: wer ein Protokoll liest, liest das von
    /// gerade eben.
    /// </summary>
    private void Rotate()
    {
        var file = new FileInfo(Location);

        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        var previous = Location + ".1";

        File.Delete(previous);
        File.Move(Location, previous);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Hängt die Protokolldatei in <c>ILogger</c> ein.
/// </summary>
public sealed class AgentLogProvider(AgentLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Writer(log, Shorten(categoryName));

    public void Dispose()
    {
    }

    /// <summary>
    /// Nur der letzte Teil des Namensraums. <c>RemoteDesktopAgent.Api.ScreenSocket</c>
    /// sagt in einer Protokollzeile nicht mehr als <c>ScreenSocket</c>, kostet
    /// aber die halbe Zeilenbreite.
    /// </summary>
    private static string Shorten(string category) =>
        category[(category.LastIndexOf('.') + 1)..];

    private sealed class Writer(AgentLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level != LogLevel.None;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Describe(level)} {category}: "
                       + formatter(state, exception);

            log.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }

        /// <summary>Vier Zeichen, damit die Zeilen untereinander stehen.</summary>
        private static string Describe(LogLevel level) => level switch
        {
            LogLevel.Trace => "SPUR",
            LogLevel.Debug => "DETL",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FEHL",
            _ => "KRIT"
        };
    }
}
