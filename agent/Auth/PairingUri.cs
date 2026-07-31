using System.Text.RegularExpressions;

namespace RemoteDesktopAgent.Auth;

/// <summary>
/// Was im QR-Code der Kopplung steht.
///
/// Die Leseseite ist <c>app/src/lib/pairingUri.ts</c>. Beide Seiten werden
/// getrennt aktualisiert, deshalb steht der Vertrag hier noch einmal
/// ausgeschrieben und wird von <c>PairingUriTests</c> gegen dieselben Fälle
/// geprüft wie dort.
///
/// Der Code selbst bleibt der aus Phase 10 — sechs Ziffern, fünf Minuten
/// gültig, einmal verwendbar. Der QR spart nur das Abtippen, er ersetzt kein
/// Geheimnis. Deshalb darf er auch offen auf dem Bildschirm stehen.
/// </summary>
public static partial class PairingUri
{
    public static string Build(string host, int port, string code)
    {
        var trimmed = (host ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "Ohne Rechnernamen ergibt der QR-Code keinen Sinn.", nameof(host));
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port), port, "Der Port liegt außerhalb des möglichen Bereichs.");
        }

        if (!SixDigits().IsMatch(code ?? string.Empty))
        {
            throw new ArgumentException(
                "Der Kopplungscode besteht aus sechs Ziffern.", nameof(code));
        }

        // Kleingeschrieben, weil MagicDNS die Namen so führt und
        // Environment.MachineName unter Windows Großbuchstaben liefert. Wer
        // abtippt, merkt den Unterschied nicht; wer scannt, liefe in einen
        // Namen, den es nicht gibt.
        var name = Uri.EscapeDataString(trimmed.ToLowerInvariant());

        return $"remotedesktop://pair?host={name}&port={port}&code={code}";
    }

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex SixDigits();
}
