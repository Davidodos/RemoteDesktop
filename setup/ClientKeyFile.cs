using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteDesktopSetup;

/// <summary>
/// Der Ausweis dieses Rechners als <em>Client</em>: das Schlüsselpaar, mit dem
/// sich seine Oberfläche bei fremden Geräten anmeldet.
///
/// <para>
/// **Der Befund dahinter (16.08.2026):** er lag im localStorage der WebView,
/// und der Agent kannte ihn nur, weil die React-App ihn beim Start hinterlegte.
/// Damit hing der Ausweis am Lebenslauf einer Weboberfläche — wer das Fenster
/// öffnete, direkt auf „Geräte" ging und dort einen Code erzeugte, hatte nie
/// eine laufende React-App. Die Gegenseite bekam beim Koppeln ein leeres
/// <c>clientKey</c> und konnte diesen Rechner danach nicht steuern, ohne dass
/// irgendwo stand, warum.
/// </para>
///
/// <para>
/// Jetzt ist es eine Datei im Datenordner, und beide lesen dieselbe: der Agent,
/// um sie beim Koppeln mitzuschicken, und das Fenster, um sich damit anzumelden.
/// Wer zuerst kommt, legt sie an. Ein halber Umbau wäre schlechter als keiner —
/// benutzten Fenster und Agent verschiedene Schlüssel, käme bei jeder Anmeldung
/// ein 401 heraus.
/// </para>
///
/// <para>
/// Sie liegt neben <c>agentkey.txt</c> und damit an derselben Stelle wie der
/// Schlüssel des Agents: eine Deinstallation räumt sie weg, ein Update nicht.
/// Genau das war der zweite Teil des Befunds — der Ordner der WebView überlebte
/// jede Neuinstallation, und mit ihm Kopplungen, die längst gelöscht waren.
/// </para>
/// </summary>
public static class ClientKeyFile
{
    /// <summary>Der Dateiname im Datenordner.</summary>
    public const string FileName = "clientkey.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Wo die Datei zu einem Datenordner liegt.</summary>
    public static string In(string dataDirectory) => Path.Combine(dataDirectory, FileName);

    /// <summary>
    /// Das abgelegte Paar, oder <c>null</c>.
    ///
    /// <para>
    /// Eine unlesbare Datei zählt als „keine" und nicht als Fehler: der Agent
    /// soll deswegen nicht am Starten scheitern. Ein <em>halbes</em> Paar zählt
    /// ebenfalls als keins — mit einem öffentlichen Schlüssel ohne privaten
    /// liefe jede Kopplung durch und jede Anmeldung danach ins Leere.
    /// </para>
    /// </summary>
    public static ClientKey? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<ClientKey>(File.ReadAllText(path), JsonOptions);

            return stored is null
                   || string.IsNullOrWhiteSpace(stored.PublicKey)
                   || string.IsNullOrWhiteSpace(stored.PrivateKey)
                ? null
                : stored;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Das abgelegte Paar, oder ein neues an derselben Stelle.
    ///
    /// <para>
    /// ECDSA P-256 — dieselbe Kurve, die der Browser über WebCrypto anbietet und
    /// die der Agent bei der Anmeldung prüft. Der öffentliche Teil im
    /// SPKI-Format, der private im PKCS-8-Format, beide Base64: so nimmt sie die
    /// WebCrypto-API des Fensters ohne Umrechnung an.
    /// </para>
    /// </summary>
    /// <exception cref="IOException">
    /// Wenn sich die Datei nicht anlegen lässt. Sie wird ausdrücklich nicht
    /// verschluckt: ein Rechner ohne Ausweis kann sich nirgends anmelden, und
    /// ein stiller Fehlschlag sähe später aus wie eine Kopplung, die nie
    /// angeboten wurde.
    /// </exception>
    public static ClientKey LoadOrCreate(string path)
    {
        if (Read(path) is { } existing)
        {
            return existing;
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var created = new ClientKey(
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()));

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        // Erst daneben, dann umbenennen: ein Absturz mitten im Schreiben ließe
        // sonst eine halbe Datei zurück, und die wäre ab dann der Ausweis
        // dieses Rechners.
        var temporary = path + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(created, JsonOptions));
        File.Move(temporary, path, overwrite: true);

        return created;
    }

    /// <summary>
    /// Die Kennung, unter der dieser Rechner in der <c>clients.json</c> der
    /// Gegenseite steht: die ersten 16 Hex-Stellen des SHA-256 über den
    /// öffentlichen Schlüssel.
    ///
    /// Beide Gegenstellen rechnen genauso (<c>PairingService.FingerprintOf</c>,
    /// <c>shortFingerprint</c>, <c>clientFingerprint</c>), und deshalb kann sie
    /// jede Seite ausrechnen, statt sie sich sagen zu lassen.
    /// </summary>
    public static string Fingerprint(string publicKey) =>
        Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKey)))[..16]
            .ToLowerInvariant();
}

/// <summary>
/// Ein Schlüsselpaar, wie es in <c>clientkey.json</c> steht. Beide Hälften
/// Base64: der öffentliche Teil im SPKI-Format, der private im PKCS-8-Format.
/// </summary>
public sealed record ClientKey(string PublicKey, string PrivateKey);
