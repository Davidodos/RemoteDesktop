namespace RemoteDesktopSetup;

/// <summary>
/// Wo sich die Geräte finden.
///
/// **Die Adresse kommt aus der Konfiguration, nie aus dem Code** — das ist die
/// Entscheidung aus <c>docs/PLAN-V2.md</c>, Abschnitt 4b, und sie fällt jetzt,
/// weil sie nachträglich teuer wäre. Heute zeigt sie auf Tailscale; wer später
/// einen eigenen Koordinator betreibt (Headscale oder das dort skizzierte
/// <c>rdcoord</c>), trägt ihn hier ein, ohne dass eine Zeile Programm sich
/// ändert.
/// </summary>
public sealed record Coordinator(string Address)
{
    /// <summary>Der Dienst von Tailscale — die Vorgabe, solange niemand etwas anderes will.</summary>
    public const string TailscaleAddress = "https://controlplane.tailscale.com";

    public static Coordinator Default { get; } = new(TailscaleAddress);

    public bool IsTailscale =>
        string.Equals(Address.TrimEnd('/'), TailscaleAddress, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Liest die Adresse aus der Konfiguration. Fehlt sie oder ist sie leer,
    /// gilt die Vorgabe — ein Rechner ohne Eintrag soll sich einrichten lassen,
    /// nicht mit einer Fehlermeldung stehenbleiben.
    /// </summary>
    public static Coordinator From(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? Default : new Coordinator(configured.Trim());

    /// <summary>
    /// Warum diese Adresse nicht taugt — <c>null</c>, wenn sie taugt.
    ///
    /// Nur <c>https</c>: über die Koordination läuft der Schlüsselaustausch des
    /// ganzen Netzes. Eine Klartextverbindung dorthin wäre die Vordertür weit
    /// offen, und zwar für jeden auf dem Weg.
    /// </summary>
    public string? Rejection
    {
        get
        {
            if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri))
            {
                return $"„{Address}“ ist keine vollständige Adresse. Erwartet wird etwas wie "
                    + TailscaleAddress + ".";
            }

            return uri.Scheme == Uri.UriSchemeHttps
                ? null
                : "Der Koordinator muss über https erreichbar sein — über ihn läuft der "
                  + "Schlüsselaustausch des ganzen Netzes.";
        }
    }

    /// <summary>
    /// Die Argumente für <c>tailscale up</c>, einzeln und nie als eine Zeile.
    ///
    /// Dieselbe Regel wie bei den Aktionen aus Phase 13: es gibt keine Stelle,
    /// an der Windows entscheidet, was eine Zeichenkette bedeutet. Bei der
    /// Vorgabe entfällt <c>--login-server</c> ganz — Tailscale kennt seinen
    /// eigenen Dienst, und ein überflüssiges Argument ist eine überflüssige
    /// Fehlerquelle.
    /// </summary>
    public IReadOnlyList<string> UpArguments()
    {
        var arguments = new List<string> { "up" };

        if (!IsTailscale)
        {
            arguments.Add("--login-server=" + Address.TrimEnd('/'));
        }

        return arguments;
    }
}

/// <summary>
/// Die Konfigurationsdatei, in der die Koordinator-Adresse steht.
///
/// Eine Datei statt eines Eintrags im Programm: der Installer schreibt sie, das
/// Einstellungsfenster ändert sie, und wer weder das eine noch das andere
/// benutzen will, macht sie mit einem Texteditor auf. Genau das ist mit „aus der
/// Konfiguration, nie aus dem Code" gemeint.
/// </summary>
public static class CoordinatorConfig
{
    /// <summary>Wie die Datei heißt. Der Ort steht in <c>desktop/</c>, weil er Windows ist.</summary>
    public const string FileName = "setup.json";

    /// <summary>
    /// Liest die Adresse. Alles, was nicht lesbar ist, ergibt die Vorgabe: eine
    /// beschädigte Konfigurationsdatei darf die Einrichtung nicht verhindern,
    /// sondern nur die Abweichung von der Vorgabe kosten.
    ///
    /// Seit V3 steht in derselben Datei mehr als nur der Koordinator; gelesen
    /// wird sie deshalb an einer Stelle (<see cref="NetworkConfig"/>) und hier
    /// nur noch der Teil herausgegriffen, den ältere Aufrufer erwarten.
    /// </summary>
    public static Coordinator Read(string? json) => NetworkConfig.Read(json).Coordinator;

    public static string Write(Coordinator coordinator) =>
        NetworkConfig.Write(NetworkProfile.Default with { Coordinator = coordinator });
}
