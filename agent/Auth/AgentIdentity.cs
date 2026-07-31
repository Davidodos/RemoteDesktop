using System.Security.Cryptography;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Das Schlüsselpaar des Rechners selbst, beim ersten Start erzeugt.
///
/// Es beantwortet die Frage, die sich der Client stellt: „Ist das noch derselbe
/// PC, mit dem ich mich damals gekoppelt habe?" Der Fingerabdruck bleibt gleich,
/// auch wenn der Rechner umbenannt wird oder eine andere Adresse bekommt —
/// deshalb merkt sich der Client ihn statt des Hostnamens.
/// </summary>
public sealed class AgentIdentity
{
    private readonly ECDsa _key;

    private AgentIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        Fingerprint = Convert.ToHexString(
            SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16].ToLowerInvariant();
    }

    /// <summary>Öffentlicher Schlüssel als Base64 im SPKI-Format.</summary>
    public string PublicKey { get; }

    /// <summary>
    /// Die ersten 16 Hex-Stellen des SHA-256 über den öffentlichen Schlüssel.
    /// 64 Bit reichen hier: der Wert wird nicht geraten, sondern bei der
    /// Kopplung übernommen und danach nur noch verglichen.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Lädt das Schlüsselpaar oder erzeugt es. Die Datei enthält den privaten
    /// Schlüssel im Klartext — sie muss dort liegen, wo nur der Dienst
    /// hinkommt, genau wie der TLS-Schlüssel daneben.
    /// </summary>
    public static AgentIdentity LoadOrCreate(string path)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (File.Exists(path))
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(path).Trim()), out _);
            return new AgentIdentity(key);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, Convert.ToBase64String(key.ExportPkcs8PrivateKey()));

        return new AgentIdentity(key);
    }

    /// <summary>Nur für Tests: eine Identität, die nirgends landet.</summary>
    public static AgentIdentity CreateTransient() =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>
    /// Prüft die Unterschrift eines Clients über eine Challenge.
    ///
    /// Erwartet wird das Format, das die WebCrypto-API des Browsers liefert:
    /// r und s hintereinander mit fester Länge, nicht DER. Wer hier das falsche
    /// Format annimmt, bekommt eine Prüfung, die immer fehlschlägt — oder,
    /// schlimmer, eine, die zu viel durchlässt.
    /// </summary>
    public static bool VerifyClientSignature(string publicKeyBase64, byte[] data, string signatureBase64)
    {
        try
        {
            using var client = ECDsa.Create();
            client.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);

            return client.VerifyData(
                data,
                Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Ein unbrauchbarer Schlüssel oder eine unbrauchbare Unterschrift
            // sind kein Sonderfall, sondern einfach „nicht bestanden".
            return false;
        }
    }
}
