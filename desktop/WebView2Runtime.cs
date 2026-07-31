using Microsoft.Web.WebView2.Core;

namespace RemoteDesktopClient;

/// <summary>
/// Prüft, ob die WebView2-Runtime da ist.
///
/// Auf Windows 11 ist sie es immer, auf Windows 10 meist über Edge. Fehlt sie,
/// stürzt das Fenster beim ersten Zugriff ohne Erklärung ab — deshalb wird
/// vorher gefragt und im Zweifel ein Satz mit Download-Adresse gezeigt.
/// </summary>
public static class WebView2Runtime
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static readonly string MissingMessage =
        "Die WebView2-Runtime fehlt auf diesem Rechner.\n\n" +
        "Sie ist Teil von Microsoft Edge und lässt sich einzeln nachinstallieren:\n" +
        DownloadUrl;

    /// <summary>Die gefundene Fassung, oder <c>null</c>, wenn es keine gibt.</summary>
    public static string? InstalledVersion()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();

            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
        // Auf einem Rechner ohne Edge-Installation wirft die Suche auch
        // Datei-Ausnahmen. Für den Aufrufer ist das derselbe Fall.
        catch (Exception ex) when (ex is DllNotFoundException or IOException)
        {
            return null;
        }
    }
}
