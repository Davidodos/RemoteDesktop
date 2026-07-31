using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RemoteDesktopClient;

/// <summary>
/// Das Fenster mit der React-App — dieselbe, die auch als PWA auf dem Handy
/// läuft.
///
/// Geschlossen wird es nie wirklich: das Kreuz versteckt es nur, weil der
/// Client im Tray weiterläuft. Ein tatsächliches Beenden gibt es genau an einer
/// Stelle, und die ist im Tray-Menü.
/// </summary>
public sealed class MainWindow : Form
{
    /// <summary>
    /// Ein erfundener Name, unter dem die lokalen Dateien ausgeliefert werden.
    ///
    /// Nötig, weil <c>file://</c> in Chromium eine eigene, sehr enge Herkunft
    /// hat: localStorage und die Anfragen an den Agent würden dort scheitern.
    /// Unter einer https-Adresse verhält sich die App wie im Browser.
    /// </summary>
    private const string VirtualHost = "app.remotedesktop.invalid";

    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    private readonly string _appDirectory;

    public MainWindow(string appDirectory)
    {
        _appDirectory = appDirectory;

        Text = "RemoteDesktop";
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_view);
    }

    /// <summary>
    /// Baut die WebView auf und lädt die App. Beim zweiten Öffnen des Fensters
    /// passiert nichts mehr — die Sitzung samt Stream bleibt bestehen.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_view.CoreWebView2 is not null)
        {
            return;
        }

        await _view.EnsureCoreWebView2Async();

        var core = _view.CoreWebView2
                   ?? throw new InvalidOperationException(
                       "WebView2 hat sich ohne Fehler, aber auch ohne Kern initialisiert.");

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, _appDirectory, CoreWebView2HostResourceAccessKind.Allow);

        // Muss vor dem Laden gesetzt sein: die App fragt die Angaben schon beim
        // ersten Rendern ab, um die Selbstverbindung zu sperren.
        var host = JsonSerializer.Serialize(new { machineName = Environment.MachineName });

        await core.AddScriptToExecuteOnDocumentCreatedAsync($"window.remoteDesktopHost = {host};");

        // Das Kontextmenü der WebView gehört zum Browser, nicht zu dieser App —
        // "Seite neu laden" mitten in einer Sitzung stiftet nur Verwirrung.
        core.Settings.AreDefaultContextMenusEnabled = false;

        core.Navigate($"https://{VirtualHost}/index.html");
    }

    /// <summary>
    /// Das Kreuz versteckt das Fenster, statt den Client zu beenden. Wer
    /// wirklich aufhören will, tut das über das Tray-Menü — sonst wäre eine
    /// laufende Sitzung mit einem Fehlklick weg.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
