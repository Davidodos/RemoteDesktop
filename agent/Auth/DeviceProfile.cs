using System.Text.Json.Serialization;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Der Steckbrief eines Geräts: alles, was die Gegenseite braucht, um es später
/// von sich aus zu erreichen.
///
/// <para>
/// Er geht bei der Kopplung in beide Richtungen über die Leitung — die Anfrage
/// trägt den des Anrufers, die Antwort den der Gegenstelle. Danach hat jede
/// Seite, was sie braucht, und **niemand muss noch einmal ins Netz**: der
/// Client-Schlüssel wandert in die eigene <c>clients.json</c>, der Rest in die
/// eigene Geräteliste.
/// </para>
///
/// <para>
/// Das ist der Unterschied zum Entwurf davor. Der reichte einen Kopplungscode
/// weiter, den die andere Seite binnen fünf Minuten einlösen musste — und band
/// die Gegenrichtung damit an einen laufenden Server, ein offenes Fenster und
/// eine Uhr. Ein Steckbrief hat keine Frist: er ist eine Beschreibung, kein
/// Geheimnis.
/// </para>
/// </summary>
/// <param name="Host">Unter welcher Adresse das Gerät erreichbar ist.</param>
/// <param name="Name">Wie es heißt — für die Anzeige in der Geräteliste.</param>
/// <param name="CaFingerprint">
/// Womit es sich ausweist. <c>null</c> bei einem Zertifikat von Tailscale, dem
/// ohnehin jeder glaubt — dann gibt es nichts zu bestätigen.
/// </param>
/// <param name="AgentFingerprint">
/// Der Fingerabdruck seines Agent-Schlüssels. Er ist die Kennung des Geräts in
/// der Liste: er bleibt gleich, auch wenn Name oder Adresse wechseln.
/// </param>
/// <param name="ClientKey">
/// Der öffentliche Schlüssel, mit dem sich die **Oberfläche** dieses Geräts
/// später anmeldet. Er gehört in die <c>clients.json</c> der Gegenseite — das
/// ist die ganze Gegenrichtung, in einem Feld.
/// </param>
public sealed record DeviceProfile(
    string Host,
    int Port,
    string Name,
    string? CaFingerprint,
    string? AgentFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ClientKey)
{
    /// <summary>Länger nennt sich kein Gerät; alles darüber ist ein Versehen.</summary>
    private const int MaxNameLength = 64;

    /// <summary>
    /// Prüft einen hereingereichten Steckbrief. Unbrauchbares wird verworfen und
    /// nicht halb übernommen: die Kopplung selbst gelingt trotzdem, nur eben in
    /// eine Richtung. Ein halber Eintrag führte später zu einem Fehlschlag an
    /// einer Stelle, an der niemand mehr weiß, woher er kam.
    /// </summary>
    public static DeviceProfile? Sanitize(
        string? host,
        int? port,
        string? name,
        string? caFingerprint,
        string? agentFingerprint,
        string? clientKey)
    {
        var address = (host ?? string.Empty).Trim();

        if (address.Length == 0 || address.Length > 255)
        {
            return null;
        }

        if (port is not (> 0 and <= 65535))
        {
            return null;
        }

        var label = (name ?? string.Empty).Trim();

        return new DeviceProfile(
            address,
            port.Value,
            label.Length is > 0 and <= MaxNameLength ? label : address,
            Hex(caFingerprint, 64),
            Hex(agentFingerprint, 16),

            // Ein Schlüssel, den dieser Agent nicht prüfen kann, ist keiner. Er
            // landete sonst als Karteileiche in der clients.json — und die Liste
            // der zugelassenen Geräte ist der letzte Ort, an dem etwas stehen
            // soll, das niemand mehr zuordnen kann.
            PairingService.IsUsablePublicKey(clientKey ?? string.Empty) ? clientKey : null);
    }

    private static string? Hex(string? value, int length)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();

        return trimmed.Length == length && trimmed.All(Uri.IsHexDigit) ? trimmed : null;
    }
}
