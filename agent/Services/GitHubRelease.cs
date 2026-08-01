using System.Text.Json;

namespace RemoteDesktopAgent.Services;

/// <summary>Ein Release auf GitHub, so weit der Agent es braucht.</summary>
/// <param name="Tag">Der Git-Tag, für Meldungen.</param>
/// <param name="Assets">Dateiname → Download-Adresse.</param>
public sealed record GitHubRelease(string Tag, IReadOnlyDictionary<string, string> Assets)
{
    /// <summary>Die Manifestdatei und ihre Unterschrift daneben.</summary>
    public const string ManifestAsset = "manifest.json";
    public const string SignatureAsset = "manifest.json.sig";

    public string? Download(string asset) => Assets.GetValueOrDefault(asset);

    /// <summary>
    /// Liest die Antwort von
    /// <c>GET /repos/{owner}/{repo}/releases/latest</c>.
    ///
    /// Bewusst von Hand statt über ein GitHub-Paket: gebraucht werden zwei
    /// Felder, und die Antwort ist die einzige Stelle, an der der Agent fremdes
    /// JSON liest. <c>null</c> bei allem, was nicht danach aussieht — eine
    /// Fehlermeldung von GitHub ist ebenfalls gültiges JSON.
    /// </summary>
    public static GitHubRelease? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var tag = root.TryGetProperty("tag_name", out var tagName) &&
                      tagName.ValueKind == JsonValueKind.String
                ? tagName.GetString() ?? string.Empty
                : string.Empty;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object ||
                    !asset.TryGetProperty("name", out var name) ||
                    !asset.TryGetProperty("browser_download_url", out var url) ||
                    name.ValueKind != JsonValueKind.String ||
                    url.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                map[name.GetString()!] = url.GetString()!;
            }

            return new GitHubRelease(tag, map);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
