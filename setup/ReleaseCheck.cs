using System.Text.Json;

namespace RemoteDesktopSetup;

/// <summary>Eine bereitstehende neue Fassung.</summary>
public sealed record ReleaseOffer(string Version, string Url);

/// <summary>
/// Sucht im jüngsten GitHub-Release den Installer.
///
/// Warum der Installer und nicht die einzelnen Dateien: der Client ist kein
/// einzelnes Programm mehr, sondern ein Ordner mit Abhängigkeiten, und der Agent
/// ist ein Dienst, der gestoppt werden muss, bevor man ihn austauscht. Beides
/// kann der Installer, und er kennt außerdem die Komponenten, die beim letzten
/// Mal gewählt wurden — ein Update ändert also nicht ungefragt, was auf diesem
/// Rechner installiert ist.
///
/// Die Antwort wird als Text hereingereicht statt selbst geholt: nur so lässt
/// sich das ohne Netz prüfen.
/// </summary>
public static class ReleaseCheck
{
    /// <summary>Der Anhang, nach dem gesucht wird. Der Installer heißt so (siehe <c>installer/</c>).</summary>
    public const string AssetPrefix = "RemoteDesktop-Setup";

    public const string LatestReleaseUrl =
        "https://api.github.com/repos/Davidodos/RemoteDesktop/releases/latest";

    /// <summary>
    /// <c>null</c> heißt: nichts gefunden. Kein Netz, kein Release, ein Anhang
    /// mit anderem Namen — für die Frage „gibt es etwas Neues?" ist das alles
    /// dasselbe, und keiner der Fälle ist eine Fehlermeldung wert.
    /// </summary>
    public static ReleaseOffer? Find(string? releaseJson)
    {
        if (string.IsNullOrWhiteSpace(releaseJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(releaseJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tag)
                || !root.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (name is null || !name.StartsWith(AssetPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var url = asset.TryGetProperty("browser_download_url", out var u)
                    ? u.GetString()
                    : null;

                if (!string.IsNullOrEmpty(url))
                {
                    return new ReleaseOffer(StripTagPrefix(tag.GetString()), url);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ob sich das Installieren lohnt.
    ///
    /// Verglichen wird auf **Ungleichheit**, nicht auf „größer": Fassungen
    /// kommen aus Git-Tags, und ein Zurückrollen auf eine ältere ist ein
    /// legitimer Vorgang. Ist die eigene Fassung unbekannt, wird angeboten —
    /// eine angebotene Aktualisierung ist harmloser als eine verschwiegene.
    /// </summary>
    public static bool IsWorthInstalling(ReleaseOffer? offer, string? installed) =>
        offer is not null
        && (string.IsNullOrWhiteSpace(installed)
            || !string.Equals(
                StripTagPrefix(installed),
                StripTagPrefix(offer.Version),
                StringComparison.OrdinalIgnoreCase));

    /// <summary><c>v1.2.0</c> und <c>1.2.0</c> sollen dasselbe bedeuten.</summary>
    private static string StripTagPrefix(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.StartsWith('v') ? trimmed[1..] : trimmed;
    }

    /// <summary>
    /// Die Argumente für den heruntergeladenen Installer.
    ///
    /// <c>/SILENT</c> statt <c>/VERYSILENT</c>: ein Fortschrittsbalken darf man
    /// sehen, wenn gerade der eigene Rechner umgebaut wird. Die Komponentenwahl
    /// kommt aus der letzten Installation — Inno merkt sie sich an der AppId, und
    /// ein Update soll nicht ungefragt einen Dienst nachinstallieren, den jemand
    /// bewusst weggelassen hat.
    /// </summary>
    public static IReadOnlyList<string> InstallArguments() =>
        ["/SILENT", "/NORESTART", "/SUPPRESSMSGBOXES"];
}
