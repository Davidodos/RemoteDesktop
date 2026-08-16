using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    /// <summary>
    /// Welchen selbst ausgestellten Stellen dieses Fenster glaubt. Ohne sie
    /// scheitert jede Verbindung zu einem Gerät ohne Tailscale, noch bevor ein
    /// Ausweis geprüft wird.
    /// </summary>
    private readonly TrustedAuthorities _trusted = TrustedAuthorities.Default();

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

    /// <summary>
    /// Wohin WebView2 seinen eigenen Zwischenspeicher legt.
    ///
    /// <para>
    /// **Der Befund dahinter:** ohne Angabe legt WebView2 ihn **neben die
    /// Programmdatei**, also nach <c>C:\Program Files\RemoteDesktop</c>. Dort
    /// darf ein Programm ohne Administratorrechte nicht schreiben, und genau
    /// das ist das Fenster. Am echten Gerät stand deshalb „Die Oberfläche ließ
    /// sich nicht laden — Zugriff verweigert (0x80070005)“. Der Ordner gehört
    /// dem angemeldeten Menschen, also liegt er in seinem Profil.
    /// </para>
    /// </summary>
    private static string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RemoteDesktop",
        "WebView2");

    private async Task BuildAsync(string appDirectory)
    {
        Directory.CreateDirectory(UserDataFolder);

        _view.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = UserDataFolder
        };

        await _view.EnsureCoreWebView2Async();

        var core = _view.CoreWebView2
                   ?? throw new InvalidOperationException(
                       "WebView2 hat sich ohne Fehler, aber auch ohne Kern initialisiert.");

        // **Erst den Zwischenspeicher wegräumen, dann laden.**
        //
        // Der Befund dahinter: die Oberfläche kommt über einen virtuellen Host
        // und damit über `https` — für WebView2 ist das eine Website wie jede
        // andere, und sie wird zwischengespeichert. Der Ordner dafür liegt im
        // Profil des Benutzers und überlebt jede Deinstallation. Wer eine neue
        // Fassung installierte, bekam deshalb die alte zu sehen: dieselbe
        // Meldung, derselbe Fehler, an einem Code, der längst behoben war. Ein
        // Fehlerbild, das sich nicht ändert, obwohl man es geändert hat, ist das
        // teuerste, das es gibt — es lenkt jede Suche auf die falsche Fährte.
        //
        // Nur der Zwischenspeicher, ausdrücklich nicht der lokale Speicher: dort
        // liegen der Schlüssel dieses Fensters und die Geräteliste. Sie zu
        // löschen hieße, bei jedem Start alle Kopplungen zu verlieren.
        await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);

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

        // Ein Zertifikat, das WebView2 nicht kennt, ist hier der Normalfall und
        // kein Fehler: ein Handy im Heimnetz kann sich keins von einer
        // öffentlichen Stelle holen. Durchgelassen wird trotzdem nur, was
        // vorher jemand bestätigt hat — siehe TrustedAuthorities.
        core.ServerCertificateErrorDetected += (_, error) =>
        {
            if (_trusted.Accepts(null, Chain(error.ServerCertificate)))
            {
                error.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            }
        };

        core.WebMessageReceived += (_, message) => OnMessage(core, message);

        // Das Kontextmenü der WebView gehört zum Browser, nicht zu dieser App —
        // „Seite neu laden" mitten in einer Sitzung stiftet nur Verwirrung.
        core.Settings.AreDefaultContextMenusEnabled = false;

        core.Navigate($"https://{VirtualHost}/index.html");
    }

    /// <summary>
    /// Die Kette eines Zertifikats, so weit WebView2 sie mitliefert.
    ///
    /// Gebraucht, weil die Stelle über dem Serverzertifikat den Ausschlag gibt:
    /// das Serverzertifikat wechselt mit jeder neuen Adresse, die Stelle
    /// darüber bleibt. Genau dafür gibt es sie.
    /// </summary>
    private static X509Certificate2Collection Chain(CoreWebView2Certificate? certificate)
    {
        var chain = new X509Certificate2Collection();

        if (certificate is null)
        {
            return chain;
        }

        foreach (var encoded in new[] { certificate.ToPemEncoding() }
                     .Concat(certificate.PemEncodedIssuerCertificateChain))
        {
            var link = Parse(encoded);

            // Ein unlesbares Glied ist kein Grund, die ganze Kette zu
            // verwerfen — es ist nur eines, dem nicht vertraut wird.
            if (link is not null)
            {
                chain.Add(link);
            }
        }

        return chain;
    }

    /// <summary>
    /// WebView2 liefert je nach Fassung mit oder ohne PEM-Kopfzeilen. Beides
    /// wird angenommen: an dieser Stelle über die Fassung zu streiten hieße,
    /// dass die Verbindung stumm scheitert.
    /// </summary>
    private static X509Certificate2? Parse(string encoded)
    {
        try
        {
            return encoded.Contains("-----BEGIN")
                ? X509Certificate2.CreateFromPem(encoded)
                : new X509Certificate2(Convert.FromBase64String(
                    new string(encoded.Where(character => !char.IsWhiteSpace(character)).ToArray())));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Die Gegenrichtung der Brücke: was die Seite nicht selbst darf, erledigt
    /// das Fenster und antwortet mit derselben Kennung.
    ///
    /// <para>
    /// **Der Befund dahinter:** die Seite läuft unter <c>https</c>, das
    /// Zertifikat der Gegenstelle liegt unter <c>http://…:8442/ca.crt</c>.
    /// Chromium verwirft das als aktiven Mixed Content, bevor irgendetwas über
    /// das Netz geht — und die Ausnahme sieht aus wie ein Rechner, der nicht
    /// antwortet. Genau das stand am Gerät, während die Gegenstelle lief.
    /// </para>
    /// </summary>
    private void OnMessage(CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs message)
    {
        var raw = message.TryGetWebMessageAsString();

        if (raw == FullscreenMessage)
        {
            FullscreenToggled?.Invoke();
            return;
        }

        if (raw is null || !raw.StartsWith('{'))
        {
            return;
        }

        BridgeRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (request?.Id is null)
        {
            return;
        }

        _ = HandleAsync(core, request);
    }

    private async Task HandleAsync(CoreWebView2 core, BridgeRequest request)
    {
        object payload;

        try
        {
            payload = request.Kind switch
            {
                "trust-fetch" => await FetchAuthorityAsync(request.Host),

                "trust-install" => TrustAuthority(request.Fingerprint),

                // Die Gegenrichtung. Alle vier Wege führen zum eigenen Agent —
                // die Seite käme nicht an ihn heran, ohne ihm vorher zu
                // vertrauen.
                "local-self" => new { profile = await LocalNode.SelfAsync() },

                "local-peers" => new { peers = await LocalNode.PeersAsync() },

                "local-forget" => await Forgotten(request),

                "local-grant" => await Granted(request),

                "local-key" => Ausweis(),

                _ => throw new InvalidOperationException(
                    $"Das Fenster kennt '{request.Kind}' nicht.")
            };
        }
        catch (Exception failure)
        {
            Reply(core, request.Id!, new { error = failure.Message });
            return;
        }

        Reply(core, request.Id!, payload);
    }

    /// <summary>
    /// Was in der Geräteliste steht, braucht im Eingang nicht mehr zu liegen.
    /// </summary>
    private static async Task<object> Forgotten(BridgeRequest request)
    {
        await LocalNode.ForgetAsync(request.Ids ?? []);

        return new { forgotten = request.Ids?.Length ?? 0 };
    }

    /// <summary>
    /// Die Oberfläche der Gegenseite darf diesen Rechner steuern. Der Schlüssel
    /// kam über eine Verbindung, an deren Anfang jemand einen Code eingetippt
    /// hat — deshalb ohne erneute Rückfrage.
    /// </summary>
    private static async Task<object> Granted(BridgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PublicKey))
        {
            throw new InvalidOperationException("Ohne Schlüssel wird niemand eingetragen.");
        }

        await LocalNode.GrantAsync(request.PublicKey, request.Label);

        return new { granted = true };
    }

    /// <summary>
    /// Der Ausweis dieses Rechners, mit dem sich die Seite bei fremden Geräten
    /// anmeldet.
    ///
    /// <para>
    /// **Der Befund dahinter:** er lag im localStorage der WebView, und der
    /// Agent kannte ihn nur, weil die React-App ihn beim Start hinterlegte. Wer
    /// das Fenster öffnete und direkt auf „Geräte" ging, hatte nie eine
    /// laufende React-App — die Gegenseite bekam beim Koppeln ein leeres
    /// <c>clientKey</c>. Jetzt liegt er in einer Datei, die beide lesen, und
    /// niemand muss ihn irgendwo hinterlegen.
    /// </para>
    /// </summary>
    private static object Ausweis()
    {
        var key = AgentData.ClientKey()
                  ?? throw new InvalidOperationException(
                      "Der Ausweis dieses Rechners ließ sich nicht anlegen. Ohne ihn kann "
                      + "sich dieses Fenster bei keinem Gerät anmelden — sind die Rechte am "
                      + "Ordner data neben dem Programm noch in Ordnung?");

        return new { publicKey = key.PublicKey, privateKey = key.PrivateKey };
    }

    private static async Task<object> FetchAuthorityAsync(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Ohne Adresse gibt es nichts zu holen.");
        }

        var fetched = await TrustImport.FetchAsync(host);

        return new
        {
            base64 = Convert.ToBase64String(fetched.Certificate.RawData),
            fingerprint = fetched.Fingerprint
        };
    }

    private object TrustAuthority(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new InvalidOperationException("Ohne Fingerabdruck wird nichts bestätigt.");
        }

        _trusted.Add(fingerprint);

        // „dialog" heißt in der App: erledigt, es ist nichts mehr zu tun. Genau
        // so ist es hier — anders als auf Android führt kein Weg mehr durch die
        // Systemeinstellungen.
        return new { outcome = "dialog" };
    }

    private static void Reply(CoreWebView2 core, string id, object payload) =>
        core.PostWebMessageAsString(
            JsonSerializer.Serialize(new { id, payload }, JsonOptions));

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Was die Seite über die Brücke schickt.</summary>
    private sealed record BridgeRequest(
        string? Id, string? Kind, string? Host, string? Fingerprint, string? PublicKey,
        string? Label, string[]? Ids);

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
