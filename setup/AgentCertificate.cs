using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopSetup;

/// <summary>
/// Was in der <c>cert.crt</c> steht, die der Agent vorzeigt.
///
/// <para>
/// **Der Befund dahinter:** die Einrichtung fragte bis v1.3.0 nur, *ob* die
/// beiden Dateien dalagen. Am echten Gerät meldete das Fenster daraufhin
/// „Einrichtung abgeschlossen", und das Handy bekam trotzdem die Rückfrage, der
/// Rechner habe sich sein Zertifikat selbst ausgestellt. Dazwischen passen drei
/// Fälle, die alle gleich aussehen: <c>tailscale cert</c> lief gar nicht,
/// weil <c>tailscale status</c> keinen Namen meldete; es lief für einen anderen
/// Namen als den, der später im QR-Code stand; oder es lag ein längst
/// abgelaufenes Zertifikat von vorletztem Jahr daneben.
/// </para>
///
/// <para>
/// Deshalb wird hier nachgesehen statt geraten. Die Einrichtung lässt „Weiter"
/// erst zu, wenn dieses Zertifikat auch wirklich auf die eingetragene Adresse
/// lautet und noch gilt.
/// </para>
/// </summary>
/// <param name="Names">
/// Alle Namen, für die es gilt. Aus der Erweiterung <c>subjectAltName</c>, denn
/// nur die zählt beim Verbinden — der Antragsteller allein wird von keinem
/// aktuellen Client mehr angesehen.
/// </param>
public sealed record AgentCertificate(IReadOnlyList<string> Names, DateTimeOffset ExpiresAt)
{
    /// <summary>Die Kennung der Erweiterung <c>subjectAltName</c>.</summary>
    private const string SubjectAlternativeName = "2.5.29.17";

    /// <summary>Ob es zu diesem Zeitpunkt noch gilt.</summary>
    public bool IsValidAt(DateTimeOffset now) => now < ExpiresAt;

    /// <summary>
    /// Ob es auf diese Adresse lautet.
    ///
    /// Ein Platzhalter (<c>*.tailnet.ts.net</c>) deckt genau eine Ebene ab — so
    /// prüfen es auch die Clients, und alles andere wäre eine Zusage, die beim
    /// Verbinden nicht hält.
    /// </summary>
    public bool Covers(string? address)
    {
        var wanted = (address ?? string.Empty).Trim().Trim('[', ']').ToLowerInvariant();

        if (wanted.Length == 0)
        {
            return false;
        }

        return Names.Any(name => Matches(name, wanted));
    }

    /// <summary>
    /// Liest das Zertifikat. Unlesbares ergibt <c>null</c> und keine Ausnahme:
    /// eine kaputte Datei ist für die Frage „habe ich ein brauchbares
    /// Zertifikat?" dasselbe wie gar keine.
    /// </summary>
    public static AgentCertificate? Read(string certificatePath)
    {
        if (!File.Exists(certificatePath))
        {
            return null;
        }

        try
        {
            using var certificate = X509Certificate2.CreateFromPem(
                File.ReadAllText(certificatePath));

            return From(certificate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Getrennt vom Dateizugriff, damit es sich ohne Datei prüfen lässt.</summary>
    public static AgentCertificate From(X509Certificate2 certificate)
    {
        var names = new List<string>();

        if (certificate.Extensions[SubjectAlternativeName] is { } raw)
        {
            var extension = new X509SubjectAlternativeNameExtension(raw.RawData, raw.Critical);

            names.AddRange(extension.EnumerateDnsNames().Select(name => name.ToLowerInvariant()));

            // IP-Adressen stehen in derselben Erweiterung, aber in einem eigenen
            // Feld. Im Heimnetz tippt man die IP und nicht den Namen — ohne sie
            // hielte diese Prüfung jedes Heimnetz-Zertifikat für unpassend.
            names.AddRange(extension.EnumerateIPAddresses().Select(address => address.ToString()));
        }

        if (names.Count == 0
            && certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false)
                is { Length: > 0 } subject)
        {
            names.Add(subject.ToLowerInvariant());
        }

        return new AgentCertificate(names, certificate.NotAfter);
    }

    private static bool Matches(string name, string wanted)
    {
        if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!name.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = name[1..];

        return wanted.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
               && !wanted[..^suffix.Length].Contains('.');
    }
}
