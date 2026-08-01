using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteDesktopAgent.Services;

/// <summary>Was ein Release über die bereitgestellte Agent-Datei sagt.</summary>
/// <param name="Version">Die Fassung, die dahinter liegt — nur für Meldungen.</param>
/// <param name="Protocol">
/// Die Protokollfassung dieser Ausgabe. Sie steht mit im Manifest, damit sich
/// vor dem Tausch sehen lässt, ob die App danach noch mitspricht.
/// </param>
/// <param name="File">Name des Assets im Release.</param>
/// <param name="Sha256">Prüfsumme der Datei, hex und klein geschrieben.</param>
public sealed record ReleaseManifest(string Version, int Protocol, string File, long Size, string Sha256);

/// <summary>
/// Prüft die Unterschrift unter einem Release-Manifest.
///
/// Eine Prüfsumme aus derselben Quelle wie die Datei schützt gegen einen
/// abgebrochenen Download, nicht gegen ein übernommenes GitHub-Konto: wer die
/// Datei austauschen kann, kann auch die Prüfsumme daneben austauschen. Der
/// Agent hat vollständige Kontrolle über den Rechner — das rechtfertigt eine
/// echte Signatur. Der private Schlüssel liegt außerhalb des Repos, der
/// öffentliche ist in den Agent kompiliert (<see cref="ReleaseKeys"/>).
///
/// Unterschrieben werden die <b>Bytes der Manifestdatei</b>, nicht ein daraus
/// gebautes Objekt. Sonst hinge die Prüfung daran, dass beide Seiten dieselbe
/// JSON-Schreibweise wählen — und das ist keine Grundlage für eine Signatur.
/// </summary>
public sealed class ManifestVerifier(string? publicKeyBase64)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ohne einkompilierten Schlüssel gibt es kein Selbst-Update. Das ist der
    /// Auslieferungszustand: den Schlüssel erzeugt, wer Releases baut
    /// (<c>scripts/release-key.mjs</c>).
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(publicKeyBase64);

    /// <returns>
    /// Das Manifest, oder <c>null</c> — bei fehlendem Schlüssel, falscher
    /// Unterschrift, verändertem Inhalt oder unvollständigen Angaben. Alle
    /// Fälle laufen zusammen, weil die Folge dieselbe ist: nicht installieren.
    /// </returns>
    public ReleaseManifest? Verify(byte[] manifestBytes, string? signatureBase64)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return null;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64!), out _);

            var signature = Convert.FromBase64String(signatureBase64);

            // Dasselbe Format wie bei der Kopplung: r und s hintereinander,
            // nicht DER. Zwei Formate im selben Projekt wären eine Falle.
            var valid = key.VerifyData(
                manifestBytes,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            if (!valid)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }

        return Parse(manifestBytes);
    }

    private static ReleaseManifest? Parse(byte[] manifestBytes)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonOptions);

            if (manifest is null ||
                string.IsNullOrWhiteSpace(manifest.Sha256) ||
                string.IsNullOrWhiteSpace(manifest.File) ||
                manifest.Size <= 0)
            {
                return null;
            }

            return manifest with { Sha256 = manifest.Sha256.ToLowerInvariant() };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Der öffentliche Schlüssel, mit dem Release-Manifeste geprüft werden.
///
/// Leer im Repo, und das ist Absicht: der zugehörige private Schlüssel darf
/// nirgends hier liegen. Wer Releases baut, erzeugt einmal ein Paar
/// (<c>node scripts/release-key.mjs</c>), trägt den öffentlichen Teil hier ein
/// und legt den privaten als Repository-Secret ab. Solange hier nichts steht,
/// prüft der Agent beim Start nichts und aktualisiert sich nicht — er sagt das
/// im Log, statt still nichts zu tun.
/// </summary>
public static class ReleaseKeys
{
    /// <summary>Base64 des öffentlichen Schlüssels im SPKI-Format (ECDSA P-256).</summary>
    public const string PublicKey = "";
}
