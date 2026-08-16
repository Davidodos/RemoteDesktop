using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Die Zertifizierungsstellen, denen dieses Fenster vertraut.
///
/// <para>
/// **Warum eine eigene Liste und nicht der Windows-Zertifikatspeicher:** dort
/// hinein zu schreiben gilt für den ganzen Rechner und jedes Programm darauf.
/// Was hier bestätigt wird, gilt für eine Fernsteuerung und für sonst nichts —
/// also gehört es hierher und nicht in den Speicher des Betriebssystems. Ein
/// Handy, das im Heimnetz seinen Bildschirm freigibt, soll nicht nebenbei zur
/// Stelle werden, der jeder Browser auf diesem Rechner glaubt.
/// </para>
///
/// <para>
/// Durchgesetzt wird sie über <c>ServerCertificateErrorDetected</c> in
/// <see cref="Pages.RemotePage"/>: WebView2 fragt bei einem Zertifikat, dem es
/// nicht traut, hier nach — steht die ausstellende Stelle in der Liste, geht es
/// weiter, sonst bleibt es beim Fehler.
/// </para>
/// </summary>
public sealed class TrustedAuthorities
{
    private readonly string _path;
    private readonly object _gate = new();
    private HashSet<string> _fingerprints;

    public TrustedAuthorities(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "trusted.json");
        _fingerprints = Read(_path);
    }

    /// <summary>Der Ordner, in dem alles zu dieser Installation liegt.</summary>
    public static TrustedAuthorities Default() =>
        new(AgentPaths.For(AppContext.BaseDirectory));

    /// <summary>Ob dieser Stelle vertraut wird.</summary>
    public bool Contains(string fingerprint)
    {
        var wanted = Normalize(fingerprint);

        lock (_gate)
        {
            return wanted.Length > 0 && _fingerprints.Contains(wanted);
        }
    }

    /// <summary>
    /// Nimmt eine Stelle auf. Bestätigt hat sie an dieser Stelle bereits
    /// jemand — hier wird nur noch festgehalten, dass es so ist.
    /// </summary>
    public void Add(string fingerprint)
    {
        var wanted = Normalize(fingerprint);

        if (wanted.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_fingerprints.Add(wanted))
            {
                return;
            }

            Write();
        }
    }

    /// <summary>
    /// Vergisst eine Stelle wieder.
    ///
    /// <para>
    /// Gebraucht beim Entfernen eines Geräts: danach soll nichts mehr davon
    /// übrig sein, als hätten sich die beiden nie gekannt. Eine Stelle, der
    /// dieses Fenster weiter glaubt, wäre genau so ein Rest — und der
    /// unangenehmste, weil ihn niemand sieht.
    /// </para>
    /// </summary>
    /// <returns><c>false</c>, wenn dieser Stelle ohnehin niemand glaubte.</returns>
    public bool Remove(string fingerprint)
    {
        var wanted = Normalize(fingerprint);

        lock (_gate)
        {
            if (wanted.Length == 0 || !_fingerprints.Remove(wanted))
            {
                return false;
            }

            Write();

            return true;
        }
    }

    /// <summary>
    /// Ob eine Kette hier endet.
    ///
    /// Geprüft wird jedes Glied und nicht nur das erste: das Serverzertifikat
    /// wechselt, sobald der Rechner eine neue Adresse bekommt, die Stelle
    /// darüber bleibt. Genau dafür gibt es sie.
    /// </summary>
    public bool Accepts(X509Certificate2? certificate, X509Certificate2Collection? chain)
    {
        if (certificate is not null && Contains(FingerprintOf(certificate)))
        {
            return true;
        }

        foreach (var link in chain ?? [])
        {
            if (Contains(FingerprintOf(link)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Derselbe Wert, den der Agent und das Handy melden.</summary>
    public static string FingerprintOf(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    private void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");

        var temporary = _path + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(_fingerprints.Order()));
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>
    /// Eine fehlende Datei ist der Normalfall. Eine kaputte kostet hier nichts
    /// als eine erneute Bestätigung — deshalb keine Ausnahme, sondern eine
    /// leere Liste.
    /// </summary>
    private static HashSet<string> Read(string path)
    {
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            var entries = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];

            return new HashSet<string>(entries.Select(Normalize), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Kleingeschrieben, ohne Trennzeichen, und nur echte 64 Hexzeichen. Alles
    /// andere ist kein Fingerabdruck und hat in der Liste nichts verloren.
    /// </summary>
    private static string Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Replace(":", string.Empty).Trim().ToLowerInvariant();

        return trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit) ? trimmed : string.Empty;
    }
}
