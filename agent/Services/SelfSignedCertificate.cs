using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Das Zertifikat, das sich der Agent selbst ausstellt, wenn es keins von
/// Tailscale gibt.
///
/// <para>
/// **Warum es das gibt:** bis Release v1.0.0 beendete sich der Agent sofort
/// wieder, wenn <c>tailscale cert</c> nie gelaufen war. Wer den Rechner nur aus
/// dem eigenen WLAN steuern will, braucht aber überhaupt kein VPN — und
/// bekäme einen Dienst, der nicht startet. Seitdem ist Tailscale eine
/// Möglichkeit und keine Voraussetzung.
/// </para>
///
/// <para>
/// **Warum eine eigene CA und nicht ein einzelnes Zertifikat:** ein Client muss
/// dem Ding einmal vertrauen, und dieses Vertrauen soll ein Ablaufdatum
/// überleben. Vertraut das Handy der CA, kann der Agent sein Serverzertifikat
/// jederzeit erneuern, ohne dass jemand erneut koppeln oder etwas bestätigen
/// muss. Vertraute es dem Serverzertifikat selbst, wäre jede Erneuerung ein
/// erneuter Gang durch die Systemeinstellungen.
/// </para>
///
/// <para>
/// **Was es nicht ist:** ein Ersatz für ein öffentlich ausgestelltes
/// Zertifikat. Ein Browser, der die CA nicht kennt, warnt weiterhin — zu Recht.
/// Deshalb bleibt <c>tailscale cert</c> der bequemere Weg, wo Tailscale ohnehin
/// läuft.
/// </para>
/// </summary>
public static class SelfSignedCertificate
{
    /// <summary>
    /// Zehn Jahre für die CA. Sie ist der Anker, den ein Mensch von Hand auf dem
    /// Handy bestätigt hat — den will niemand jährlich neu bestätigen.
    /// </summary>
    public static readonly TimeSpan AuthorityLifetime = TimeSpan.FromDays(3650);

    /// <summary>
    /// Gut zwei Jahre für das Serverzertifikat. Es wird still erneuert, also
    /// kostet ein kurzer Zeitraum nichts außer einem Neustart des Dienstes.
    /// </summary>
    public static readonly TimeSpan ServerLifetime = TimeSpan.FromDays(825);

    /// <summary>Ab wann vorsorglich erneuert wird, statt auf den Ablauf zu warten.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    /// <summary>Serverauthentifizierung — mehr darf dieses Zertifikat nicht.</summary>
    private static readonly Oid ServerAuthentication = new("1.3.6.1.5.5.7.3.1");

    /// <summary>
    /// Die eigene CA. Sie unterschreibt genau ein Zertifikat, nämlich das
    /// dieses Rechners — deshalb <c>pathLength = 0</c>: sie darf keine weiteren
    /// CAs beglaubigen, selbst wenn jemand ihren Schlüssel bekäme.
    /// </summary>
    public static X509Certificate2 CreateAuthority(string machineName, DateTimeOffset now)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN=RemoteDesktop {Sanitize(machineName)} CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Fünf Minuten Vorlauf: die Uhren zweier Geräte gehen selten gleich, und
        // ein Zertifikat „aus der Zukunft" lehnt jeder Client ab.
        return request.CreateSelfSigned(
            now - TimeSpan.FromMinutes(5), now + AuthorityLifetime);
    }

    /// <summary>
    /// Das Serverzertifikat für diesen Rechner, unterschrieben von der eigenen
    /// CA.
    /// </summary>
    /// <param name="names">
    /// Alle Namen und Adressen, unter denen dieser Rechner angesprochen werden
    /// darf. Der erste ist der Antragsteller und damit der, den der QR-Code
    /// trägt.
    /// </param>
    public static X509Certificate2 Issue(
        X509Certificate2 authority, IReadOnlyList<string> names, DateTimeOffset now)
    {
        if (names.Count == 0)
        {
            throw new ArgumentException(
                "Ohne einen Namen ist ein Serverzertifikat wertlos.", nameof(names));
        }

        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={Sanitize(names[0])}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([ServerAuthentication], critical: false));

        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                authority, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        request.CertificateExtensions.Add(BuildNames(names));

        // Nie länger gültig als die CA selbst — ein Zertifikat, das seinen
        // Aussteller überlebt, ist ab dessen Ablauf wertlos und sieht trotzdem
        // gültig aus.
        var until = now + ServerLifetime;
        var notAfter = until > authority.NotAfter ? authority.NotAfter : until;

        using var issued = request.Create(
            authority,
            now - TimeSpan.FromMinutes(5),
            notAfter,
            RandomNumberGenerator.GetBytes(16));

        return issued.CopyWithPrivateKey(key);
    }

    /// <summary>
    /// Ob das vorliegende Serverzertifikat noch taugt.
    ///
    /// Drei Gründe sprechen dagegen, und alle drei enden im selben Handgriff:
    /// es ist abgelaufen, es läuft bald ab, oder es lautet nicht mehr auf die
    /// Adressen, unter denen der Rechner heute erreichbar ist. Der dritte Fall
    /// ist der häufige — ein Laptop, der den Router wechselt.
    /// </summary>
    public static bool NeedsRenewal(
        X509Certificate2 server, IReadOnlyList<string> names, DateTimeOffset now)
    {
        if (now + RenewBefore >= server.NotAfter || now < server.NotBefore)
        {
            return true;
        }

        var covered = SubjectNames(server);

        return names.Any(name => !covered.Contains(Normalize(name), StringComparer.Ordinal));
    }

    /// <summary>
    /// Auf welche Namen und Adressen ein Zertifikat lautet — kleingeschrieben
    /// und ohne Sortierung, weil es nur um „ist enthalten" geht.
    /// </summary>
    public static IReadOnlyCollection<string> SubjectNames(X509Certificate2 certificate)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var extension in certificate.Extensions)
        {
            if (extension is not X509SubjectAlternativeNameExtension names)
            {
                continue;
            }

            foreach (var name in names.EnumerateDnsNames())
            {
                found.Add(Normalize(name));
            }

            foreach (var address in names.EnumerateIPAddresses())
            {
                found.Add(Normalize(address.ToString()));
            }
        }

        return found;
    }

    /// <summary>
    /// Der Fingerabdruck, an dem ein Client erkennt, dass er die richtige CA
    /// bestätigt: <c>sha256</c> über das Zertifikat, kleingeschrieben und ohne
    /// Trennzeichen.
    ///
    /// Er geht über die Kopplung — also über einen Weg, den ein Angreifer im Netz
    /// nicht sieht. Deshalb darf das Zertifikat selbst unverschlüsselt
    /// ausgeliefert werden: was zählt, ist der Vergleich.
    /// </summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    /// <summary>
    /// Baut die Liste der Alternativnamen. Was als IP-Adresse lesbar ist, kommt
    /// als IP-Eintrag hinein und nicht als Name — ein Client, der
    /// <c>https://192.168.178.20:8443</c> aufruft, prüft ausschließlich die
    /// IP-Einträge, und ein DNS-Eintrag „192.168.178.20" hilft ihm nicht.
    /// </summary>
    private static X509Extension BuildNames(IReadOnlyList<string> names)
    {
        var builder = new SubjectAlternativeNameBuilder();

        foreach (var name in names.Select(Normalize).Distinct(StringComparer.Ordinal))
        {
            if (IPAddress.TryParse(name, out var address))
            {
                builder.AddIpAddress(address);
            }
            else
            {
                builder.AddDnsName(name);
            }
        }

        return builder.Build();
    }

    private static string Normalize(string name) => name.Trim().Trim('[', ']').ToLowerInvariant();

    /// <summary>
    /// Ein Antragsteller ist kein freier Text: Komma und Gleichheitszeichen
    /// trennen darin die Bestandteile. Ein Rechnername mit Komma ergäbe einen
    /// Namen, den niemand vorhergesehen hat.
    /// </summary>
    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '.' or ' ' or '_').ToArray());

        return cleaned.Trim().Length == 0 ? "Agent" : cleaned.Trim();
    }
}
