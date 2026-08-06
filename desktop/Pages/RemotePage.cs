using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RemoteDesktopClient.Ui;

namespace RemoteDesktopClient.Pages;

/// <summary>
/// Die Fernsteuerung — dieselbe React-App, die auch auf dem Handy läuft, hier
/// als Seite im gemeinsamen Fenster.
///
/// <para>
/// Bis V3 war sie ein eigenes Fenster. Sie ist es nicht mehr, weil sie keines
/// ist: wer einen Rechner steuert, wechselt zwischendurch auf „Geräte" oder
/// „Netz" und zurück, und dafür zwei Fenster nebeneinander zu schieben war der
/// Umweg, den dieser Umbau abschafft.
/// </para>
///
/// <para>
/// Die Sitzung überlebt jeden Seitenwechsel: die WebView wird einmal aufgebaut
/// und danach nur noch versteckt. Ein Neuladen mitten in einer Sitzung wäre ein
/// Abbruch.
/// </para>
/// </summary>
public sealed class RemotePage : Control
{
    /// <summary>
    /// Ein erfundener Name, unter dem die lokalen Dateien ausgeliefert werden.
    ///
    /// Nötig, weil <c>file://</c> in Chromium eine eigene, sehr enge Herkunft
    /// hat: localStorage und die Anfragen an den Agent würden dort scheitern.
    /// Unter einer https-Adresse verhält sich die App wie im Browser.
    /// </summary>
    private const string VirtualHost = "app.remotedesktop.invalid";

    /// <summary>Das Einzige, was die Seite dem Fenster zu sagen hat.</summary>
    private const string FullscreenMessage = "remotedesktop:fullscreen";

    private readonly string? _appDirectory;
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Stack _fallback;

    private bool _loaded;

    public RemotePage(string? appDirectory)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);

        _appDirectory = appDirectory;
        BackColor = Theme.Window;

        _fallback = new Stack
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(LogicalToDeviceUnits(28))
        };

        _view.DefaultBackgroundColor = Theme.Window;

        Controls.Add(_view);
        Controls.Add(_fallback);
    }

    public event Action? FullscreenToggled;

    /// <summary>
    /// Baut die WebView beim ersten Anzeigen auf. Nicht früher: der Aufbau
    /// dauert eine knappe Sekunde, und wer nur etwas einstellen will, soll nicht
    /// darauf warten.
    /// </summary>
    public async Task ShowRemoteAsync()
    {
        if (_loaded)
        {
            return;
        }

        if (_appDirectory is null)
        {
            Explain(
                "Die Oberfläche fehlt",
                WebAppLocator.MissingMessage,
                null);

            return;
        }

        if (WebView2Runtime.InstalledVersion() is null)
        {
            Explain(
                "Windows fehlt die Anzeigekomponente",
                WebView2Runtime.MissingMessage,
                ("Herunterladen", WebView2Runtime.DownloadUrl));

            return;
        }

        try
        {
            await BuildAsync(_appDirectory);

            _loaded = true;
            _fallback.Visible = false;
            _view.Visible = true;
        }
        catch (Exception failure)
        {
            Explain("Die Oberfläche ließ sich nicht laden", failure.Message, null);
        }
    }

    private async Task BuildAsync(string appDirectory)
    {
        await _view.EnsureCoreWebView2Async();

        var core = _view.CoreWebView2
                   ?? throw new InvalidOperationException(
                       "WebView2 hat sich ohne Fehler, aber auch ohne Kern initialisiert.");

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, appDirectory, CoreWebView2HostResourceAccessKind.Allow);

        // Muss vor dem Laden gesetzt sein: die App fragt die Angaben schon beim
        // ersten Rendern ab, um die Selbstverbindung zu sperren.
        var host = JsonSerializer.Serialize(new { machineName = Environment.MachineName });

        await core.AddScriptToExecuteOnDocumentCreatedAsync($"window.remoteDesktopHost = {host};");

        // F11 gehört dem Fenster, nicht der Seite darin — sonst schaltete
        // Chromium seinen eigenen Vollbildmodus, der die Seitenleiste stehen
        // ließe. Der Umweg über die Seite ist nötig, weil Tastendrücke im
        // Browserfenster gar nicht erst bei WinForms ankommen: die WebView ist
        // ein eigenes Fenster mit eigener Nachrichtenschlange.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            """
            window.addEventListener('keydown', (event) => {
              if (event.key === 'F11') {
                event.preventDefault();
                window.chrome.webview.postMessage('remotedesktop:fullscreen');
              }
            });
            """);

        core.WebMessageReceived += (_, message) =>
        {
            if (message.TryGetWebMessageAsString() == FullscreenMessage)
            {
                FullscreenToggled?.Invoke();
            }
        };

        // Das Kontextmenü der WebView gehört zum Browser, nicht zu dieser App —
        // „Seite neu laden" mitten in einer Sitzung stiftet nur Verwirrung.
        core.Settings.AreDefaultContextMenusEnabled = false;

        core.Navigate($"https://{VirtualHost}/index.html");
    }

    /// <summary>
    /// Ein leerer schwarzer Bereich sähe aus wie ein Absturz. Steht die
    /// Fernsteuerung nicht zur Verfügung, steht hier, warum — und wenn es einen
    /// Weg gibt, auch der Knopf dorthin.
    /// </summary>
    private void Explain(string title, string message, (string Label, string Url)? action)
    {
        _fallback.Clear();

        var card = new Card(title);

        card.Body.Add(new TextBlock(message));

        if (action is { } step)
        {
            var button = new ThemedButton(step.Label, ButtonTone.Primary);

            button.Click += (_, _) => OverviewPage.Open(step.Url);
            card.Body.Add(Row.Buttons(button));
        }

        _fallback.Add(card);
        _fallback.Visible = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Theme.Window);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }
}
