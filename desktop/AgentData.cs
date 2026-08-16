using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RemoteDesktopSetup;

namespace RemoteDesktopClient;

/// <summary>
/// Der Datenordner, gelesen ohne laufenden Agent.
///
/// <para>
/// **Warum es das gibt:** koppeln soll gehen, wenn der Agent zwar eingerichtet,
/// aber gestoppt ist. Wer an einem Rechner sitzt, den er nur zum Steuern
/// anderer benutzt, hat keinen Grund, ihn dafür zu starten — und der Steckbrief
/// dieses Rechners ist keine Auskunft über den Augenblick, sondern eine
/// Beschreibung: <em>so wäre er erreichbar</em>. Sie steht vollständig in den
/// Dateien, die der Agent ohnehin angelegt hat.
/// </para>
///
/// <para>
/// Was hier <b>nicht</b> steht, ist der Posteingang der Steckbriefe: dort landet
/// nur etwas, wenn ein anderes Gerät diesen Rechner gekoppelt hat, und das
/// setzt einen laufenden Agent voraus. Ein Leser ohne Schreiber wäre eine
/// Ansicht, die immer leer ist.
/// </para>
///
/// <para>
/// Die Werte entstehen genauso wie im Agent — <c>CertificateLoader</c>,
/// <c>SelfSignedCertificate.Fingerprint</c> und <c>AgentIdentity</c>. Wer eine
/// der beiden Seiten ändert, ändert beide, sonst beschreibt der Steckbrief
/// einen Rechner, den es so nicht gibt.
/// </para>
/// </summary>
public static class AgentData
{
    /// <summary>Der Agent lauscht immer hier; andere Ports sieht die Einrichtung nicht vor.</summary>
    public const int AgentPort = 8443;

    /// <summary>
    /// Der eigene Steckbrief aus den Dateien.
    /// </summary>
    /// <returns>
    /// <c>null</c>, solange keine Adresse eingetragen ist. Dann ist dieser
    /// Rechner kein mögliches Ziel — ein Steckbrief ohne Adresse beschreibt
    /// nichts.
    /// </returns>
    public static LocalProfile? Profile()
    {
        var address = Network().AdvertisedAddress?.ToLowerInvariant();

        return address is null
            ? null
            : new LocalProfile(
                address,
                AgentPort,
                Environment.MachineName,
                AuthorityFingerprint(),
                AgentFingerprint(),
                ClientKey()?.PublicKey,
                DevicePlatform.Windows);
    }

    /// <summary>
    /// Der Ausweis dieses Rechners als Client — angelegt, falls es ihn noch
    /// nicht gibt.
    ///
    /// <para>
    /// Wer zuerst kommt, legt ihn an: der Agent beim Start, das Fenster hier.
    /// Beide lesen dieselbe Datei, und genau darauf kommt es an — mit zwei
    /// Schlüsseln liefe jede Anmeldung in ein 401.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>null</c>, wenn sich die Datei nicht anlegen lässt. Das ist ein echter
    /// Fehler und wird an der Oberfläche auch so gemeldet — ohne Ausweis kann
    /// sich dieses Fenster nirgends anmelden.
    /// </returns>
    public static ClientKey? ClientKey()
    {
        try
        {
            return ClientKeyFile.LoadOrCreate(ClientKeyFile.In(Elevation.DataDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Trägt die Oberfläche der Gegenseite in die <c>clients.json</c> ein.
    ///
    /// Nur bei gestopptem Agent: läuft er, hält er die Liste im Speicher, und
    /// was hier geschrieben würde, wäre beim nächsten Mal weg. Wer entscheidet,
    /// steht in <see cref="LocalNode"/>.
    /// </summary>
    public static void Grant(string publicKey, string label) =>
        ClientsFile.Grant(
            ClientsFile.In(Elevation.DataDirectory),
            publicKey,
            label,
            DateTimeOffset.UtcNow);

    /// <summary>
    /// Nimmt ein Gerät aus der <c>clients.json</c>. Nur bei gestopptem
    /// Agent — siehe <see cref="Grant"/>.
    /// </summary>
    public static void Revoke(string clientId)
    {
        var path = ClientsFile.In(Elevation.DataDirectory);

        ClientsFile.Write(
            path, ClientsFile.Read(path).Where(client => client.Id != clientId));
    }

    /// <summary>
    /// Wer diesen Rechner steuern darf, aus der Datei gelesen.
    ///
    /// Dieselben Felder, die der Agent unter <c>/api/clients</c> liefert: die
    /// Oberfläche soll nicht wissen müssen, woher die Liste gerade kommt.
    /// </summary>
    public static object[] Clients()
    {
        try
        {
            return
            [
                .. ClientsFile.Read(ClientsFile.In(Elevation.DataDirectory)).Select(client => new
                {
                    id = client.Id,
                    label = client.Label,
                    scopes = client.Scopes,
                    createdAt = client.CreatedAt,
                    lastSeenAt = client.LastSeenAt
                })
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            // Eine unlesbare Liste ist keine leere — aber hier ist der falsche
            // Ort, das zu klären. Sie steht dann nicht da, und das fällt auf.
            return [];
        }
    }

    /// <summary>Das eingetragene Netzprofil — daraus kommt die Adresse.</summary>
    private static NetworkProfile Network()
    {
        try
        {
            var path = Path.Combine(Elevation.DataDirectory, NetworkConfig.FileName);

            return NetworkConfig.Read(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NetworkProfile.Default;
        }
    }

    /// <summary>
    /// Der Fingerabdruck der eigenen CA — <c>null</c>, wenn das Zertifikat von
    /// Tailscale kommt und es also nichts zu bestätigen gibt.
    ///
    /// Dieselbe Reihenfolge wie im Agent: liegt ein Zertifikat von Tailscale da,
    /// gewinnt es, und die eigene CA bleibt ungenutzt liegen. Sie hier trotzdem
    /// zu melden hieße, die Gegenseite etwas bestätigen zu lassen, das ihr nie
    /// vorgezeigt wird.
    /// </summary>
    private static string? AuthorityFingerprint()
    {
        if (File.Exists(Path.Combine(Elevation.DataDirectory, "cert.crt"))
            && File.Exists(Path.Combine(Elevation.DataDirectory, "cert.key")))
        {
            return null;
        }

        var path = Path.Combine(Elevation.DataDirectory, AgentPaths.AuthorityPublicFile);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var authority = new X509Certificate2(path);

            return Convert.ToHexString(SHA256.HashData(authority.RawData)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is CryptographicException or IOException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Die Kennung des Agents: die ersten 16 Hex-Stellen des SHA-256 über
    /// seinen öffentlichen Schlüssel. Sie bleibt gleich, auch wenn der Rechner
    /// umbenannt wird oder eine andere Adresse bekommt — deshalb merkt sich die
    /// Gegenseite sie statt des Namens.
    /// </summary>
    private static string? AgentFingerprint()
    {
        var path = Path.Combine(Elevation.DataDirectory, AgentPaths.IdentityFile);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var key = ECDsa.Create();

            key.ImportPkcs8PrivateKey(
                Convert.FromBase64String(File.ReadAllText(path).Trim()), out _);

            return ClientKeyFile.Fingerprint(
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException
                                       or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Der Steckbrief dieses Rechners, wie ihn die Seite bekommt. Dieselben Felder,
/// die der Agent unter <c>/api/pair/self</c> liefert — die Oberfläche soll
/// nicht wissen müssen, woher er gerade kommt.
/// </summary>
public sealed record LocalProfile(
    string Host,
    int Port,
    string Name,
    string? CaFingerprint,
    string? AgentFingerprint,
    string? ClientKey,
    string Platform);
