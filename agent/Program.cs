using System.Security.Cryptography.X509Certificates;
using RemoteDesktopAgent.Actions;
using RemoteDesktopAgent.Api;
using RemoteDesktopAgent.Auth;
using RemoteDesktopAgent.Capture.H264;
using RemoteDesktopAgent.Native;
using RemoteDesktopAgent.Services;
using RemoteDesktopSetup;

// Ohne das meldet Windows bei skalierten Displays virtuelle statt echter
// Pixel — die Klicks landen dann systematisch daneben.
Win32.SetProcessDPIAware();

// ContentRoot ausdrücklich auf das Verzeichnis der .exe legen. Ohne das nimmt
// ASP.NET Core das Arbeitsverzeichnis des Prozesses — und dann findet der Agent
// seine appsettings.json nicht mehr, sobald ihn jemand anders als die
// Aufgabenplanung startet (etwa das Update-Skript). Er beendet sich dann sofort
// mit „Kein Token konfiguriert", was von außen wie ein toter Rechner aussieht.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Host.UseWindowsService(options => options.ServiceName = "RemoteDesktopAgent");

// Alles, was zu dieser Installation gehört, liegt seit v1.3.0 in einem Ordner
// neben der Programmdatei. Was eine ältere Fassung woanders hinterlassen hat,
// wird beim Start übernommen — sonst wären nach einem Update alle Kopplungen
// weg und der Rechner meldete sich mit einer neuen Kennung.
RemoteDesktopSetup.AgentPaths.Adopt(
    AppContext.BaseDirectory,
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        RemoteDesktopSetup.AgentPaths.LegacyFolderName));

var settings = AgentSettings.Load(builder.Configuration);

// Der Ordner muss stehen, bevor irgendetwas hineingeschrieben wird — der
// Installer legt ihn zwar an, aber ein Entwicklungsbau hat keinen Installer.
Directory.CreateDirectory(settings.DataDirectory);

// Der Agent ist eine WinExe und hat damit keine Konsole. Ohne diese Zeile
// schreibt der voreingestellte Logger in ein Fenster, das es nicht gibt — und
// alles, was er meldet, ist weg. Siehe AgentLog.
var log = new AgentLog(settings.DataDirectory);

builder.Logging.AddProvider(new AgentLogProvider(log));

// Wie dieser Rechner erreichbar sein soll: im Heimnetz, über Tailscale oder
// über ein fremdes VPN. Aus derselben Datei, die auch die Oberfläche schreibt.
var profile = RemoteDesktopSetup.NetworkConfig.Read(
    File.Exists(settings.NetworkConfigPath)
        ? File.ReadAllText(settings.NetworkConfigPath)
        : null);

// Einmal geladen und dann dreifach gebraucht: Kestrel zeigt es vor, der QR-Code
// der Kopplung liest den Namen daraus, und die eigene CA — falls es eine gibt —
// wird zum Abholen bereitgelegt.
var chosen = CertificateLoader.LoadOrCreate(
    settings.CertificatePath,
    settings.KeyPath,
    new CertificateVault(settings.DataDirectory),
    Environment.MachineName,
    CertificateLoader.Names(
        profile.AdvertisedAddress, Environment.MachineName, LocalAddresses.List()));

var certificate = chosen.Certificate;

// Nur wenn der Agent sich selbst beglaubigt hat, gibt es überhaupt etwas
// abzuholen: ein Zertifikat von Tailscale kennt jeder Browser bereits.
var authority = chosen.Authority is null
    ? null
    : new
    {
        Fingerprint = SelfSignedCertificate.Fingerprint(chosen.Authority),
        Der = chosen.Authority.Export(X509ContentType.Cert)
    };

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(settings.Port, listen => listen.UseHttps(certificate));

    // Der zweite, unverschlüsselte Port trägt genau eine Datei: das eigene
    // CA-Zertifikat. Ohne ihn gäbe es ein Henne-Ei-Problem — ein Client kann
    // die Datei nicht über eine Verbindung holen, der er noch nicht traut.
    // Vertretbar ist das, weil dort ausschließlich ein öffentliches Zertifikat
    // liegt und der Client es gegen den Fingerabdruck aus der Kopplung prüft.
    if (authority is not null)
    {
        kestrel.ListenAnyIP(settings.TrustPort);
    }
});

// Die PWA wird vom Hub auf der NAS ausgeliefert, spricht den Agent aber direkt
// an — jeder REST-Aufruf ist damit Cross-Origin. Ohne diese Freigabe verwirft
// der Browser die Antworten, und zwar lautlos: die WebSockets laufen weiter,
// nur /api/* kommt nie an.
//
// Beliebige Herkunft ist hier vertretbar, weil der Agent ausschließlich über
// das Bearer-Token autorisiert. Cookies gibt es nicht, also kann eine fremde
// Seite im Browser nichts erreichen, was sie nicht ohnehin dürfte.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

// ---- Kopplung und Zugangsprüfung -----------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(AgentIdentity.LoadOrCreate(settings.IdentityPath));
builder.Services.AddSingleton(new ClientStore(settings.ClientsPath));
builder.Services.AddSingleton<PairingCodes>();
builder.Services.AddSingleton<ChallengeStore>();
builder.Services.AddSingleton<SessionStore>();

// Wer gerade Bild oder Eingabe offen hält. Ohne diese Liste überlebte eine
// stehende Verbindung ihren eigenen Widerruf — siehe LiveConnections.
builder.Services.AddSingleton<LiveConnections>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton(provider =>
    new ClientAuth(provider.GetRequiredService<SessionStore>(), settings.Token));

// Die Aktionen werden hier und nicht erst beim Auslösen geprüft: ein
// Tippfehler im Pfad soll auffallen, solange jemand am Rechner sitzt. Wirft
// Load(), startet der Agent gar nicht erst — mit einer Meldung, die sagt,
// welcher Eintrag falsch ist.
builder.Services.AddSingleton(ActionCatalog.Load(settings.ActionsPath));
builder.Services.AddSingleton<IActionHost, WindowsActionHost>();
builder.Services.AddSingleton(provider =>
    new ActionRunner(provider.GetRequiredService<IActionHost>()));

builder.Services.AddSingleton<InputSender>();
builder.Services.AddSingleton<MonitorEnumerator>();
builder.Services.AddSingleton<InputExecutor>();
builder.Services.AddSingleton<InputSocket>();
builder.Services.AddSingleton<PowerService>();
builder.Services.AddSingleton<MediaSessionReader>();

// Der Bild-Stream hält pro Verbindung eigene Grafikressourcen — deshalb
// transient und nicht als Singleton wie der Rest.
builder.Services.AddTransient<ScreenSocket>();

// Zustand pro Thread, deshalb pro Stream eine eigene Instanz.
builder.Services.AddTransient<DesktopBinder>();

// Selbst-Update über GitHub-Releases. Läuft nur, wenn ein Release-Schlüssel
// einkompiliert ist — ohne den wird nichts geprüft und nichts getauscht.
builder.Services.AddHttpClient();
builder.Services.AddSingleton(new ManifestVerifier(ReleaseKeys.PublicKey));
builder.Services.AddSingleton(provider => new AgentUpdater(
    provider.GetRequiredService<IHttpClientFactory>(),
    provider.GetRequiredService<ManifestVerifier>(),
    settings.UpdateRepository,
    provider.GetRequiredService<ILogger<AgentUpdater>>()));
builder.Services.AddHostedService<SelfUpdater>();

// Wecken als Netz-Fähigkeit: ein wacher Rechner weckt den schlafenden im
// selben Netz. Wo „dasselbe Netz" ist, sagt die Standort-Kennung unten.
builder.Services.AddSingleton<IMagicPacketSender>(
    new BroadcastSender(settings.BroadcastAddress));
builder.Services.AddSingleton<WakeService>();

// Hält die WebRTC-Sitzungen samt ihrer ffmpeg-Prozesse — einer für alle.
builder.Services.AddSingleton<WebRtcRegistry>();

var app = builder.Build();

// Der unverschlüsselte Port, noch vor allem anderen: was dort hereinkommt,
// bekommt das CA-Zertifikat oder gar nichts. Er darf unter keinen Umständen
// dieselben Endpunkte bedienen wie der verschlüsselte — deshalb steht die
// Weiche hier oben und nicht als Route weiter unten, wo sie jemand übersehen
// könnte.
app.Use(async (context, next) =>
{
    if (authority is null || context.Connection.LocalPort != settings.TrustPort)
    {
        await next();
        return;
    }

    if (context.Request.Path == "/ca.crt" && HttpMethods.IsGet(context.Request.Method))
    {
        // Kopfzeilen vor dem Rumpf: danach sind sie hinaus und jede Zuweisung
        // wäre wirkungslos.
        context.Response.ContentType = "application/x-x509-ca-cert";
        context.Response.Headers.Append("X-Certificate-Fingerprint", authority.Fingerprint);

        await context.Response.Body.WriteAsync(authority.Der);

        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
});

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseCors();
app.UseClientAuth();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Die eingetragene Adresse schlägt den Namen im Zertifikat: bei einem selbst
// ausgestellten Zertifikat steht dort zwar dasselbe, aber wer im Heimnetz eine
// IP einträgt, soll genau diese im QR-Code wiederfinden. Nur wo nichts
// eingetragen ist — also bei Tailscale —, bleibt das Zertifikat die Quelle.
app.MapPairingEndpoints(
    profile.AdvertisedAddress ?? CertificateLoader.DnsName(certificate),
    settings.Port,
    authority?.Fingerprint);
app.MapActionEndpoints();
app.MapWakeEndpoints();
app.MapUpdateEndpoints();

// Wo dieser Rechner steht. Einmal beim Start ermittelt: die ARP-Abfrage kostet
// im ungünstigen Fall eine Sekunde, und /api/info wird bei jedem Gerätewechsel
// abgefragt. Wandert der Rechner in ein anderes Netz, stimmt der Wert bis zum
// nächsten Start nicht mehr — geweckt wird ohnehin erst nach einem Neustart,
// und der setzt ihn richtig.
var site = SiteIdentity.Resolve(NetworkAdapters.List());

app.Logger.LogInformation(
    "Standort-Kennung {SiteId}, eigene MAC {Mac}.",
    site.SiteId ?? "unbekannt",
    site.Mac ?? "unbekannt");

// Hostname und Monitor-Layout — die App baut daraus ihre Monitor-Tabs.
app.MapGet("/api/info", (InputExecutor executor) =>
{
    var (monitors, desktop) = executor.GetLayout();

    return Results.Ok(new
    {
        hostname = Environment.MachineName,

        // Getrennt aktualisierbar heißt: die App trifft irgendwann auf einen
        // älteren Agent. Bei ungleichem `protocol` sagt sie klar, welche Seite
        // zu alt ist, statt an einer unbekannten Nachricht zu scheitern.
        version = AgentVersion.Current,
        protocol = AgentVersion.Protocol,

        // Damit der Client diesen Rechner später wecken lassen kann: die MAC
        // gehört ins Magic Packet, die Standort-Kennung sagt, wen er danach
        // fragen muss. Beide merkt er sich, solange der Rechner wach ist.
        siteId = site.SiteId,
        mac = site.Mac,

        // Dieser Rechner kann seinerseits Nachbarn wecken.
        canWake = true,

        // Womit dieser Rechner sich ausweist. Steht hier ein Fingerabdruck, hat
        // er sich selbst beglaubigt — der Client weiß dann, dass er die CA
        // einmal bestätigen muss, und woran er die richtige erkennt.
        caFingerprint = authority?.Fingerprint,
        trustPort = authority is null ? (int?)null : settings.TrustPort,
        monitors = monitors.Select(m => new
        {
            index = m.Index,
            width = m.Width,
            height = m.Height,
            x = m.X,
            y = m.Y,
            primary = m.IsPrimary,
            name = m.DeviceName
        }),
        virtualDesktop = new { desktop.X, desktop.Y, desktop.Width, desktop.Height }
    });
});

app.MapPost("/api/power", (PowerRequest request, PowerService power, ILogger<Program> logger) =>
{
    if (!Enum.TryParse<PowerAction>(request.Action, ignoreCase: true, out var action))
    {
        return Results.BadRequest(new
        {
            error = $"Unbekannte Aktion '{request.Action}'.",
            allowed = Enum.GetNames<PowerAction>().Select(n => n.ToLowerInvariant())
        });
    }

    try
    {
        power.Execute(action);
        return Results.Ok(new { status = "ok", action = action.ToString().ToLowerInvariant() });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Power-Aktion {Action} fehlgeschlagen.", action);
        return Results.Problem($"Power-Aktion fehlgeschlagen: {ex.Message}");
    }
});

// Was gerade läuft — Titel, Interpret und Wiedergabestatus je Anwendung.
app.MapGet("/api/media/sessions", async (MediaSessionReader reader) =>
    Results.Ok(new { sessions = await reader.GetSessionsAsync() }));

app.MapGet("/api/media/thumbnail", async (string session, MediaSessionReader reader) =>
{
    var image = await reader.GetThumbnailAsync(session);

    // Windows liefert das Titelbild in dem Format, in dem die App es hinterlegt
    // hat — meist JPEG, gelegentlich PNG. Der Browser erkennt beides selbst.
    return image is null ? Results.NotFound() : Results.File(image, "image/jpeg");
});

app.MapPost("/api/media", async (
    MediaRequest request, InputSender sender, MediaSessionReader reader, ILogger<Program> logger) =>
{
    // Ist eine Sitzung benannt, wird sie direkt angesprochen. Das trifft
    // zuverlässig die gemeinte App — die Medien-Tasten landen dagegen immer bei
    // der, die Windows gerade für die vorderste hält. Klappt es nicht (die App
    // kann die Aktion nicht), geht es unten über die Taste weiter.
    if (!string.IsNullOrWhiteSpace(request.Session) &&
        await reader.ControlAsync(request.Session, request.Action ?? string.Empty))
    {
        return Results.Ok(new { status = "ok", action = request.Action, session = request.Session });
    }

    // Ohne benannte Sitzung laufen die Aktionen über dieselben Virtual-Keys wie
    // die Tasten einer Multimedia-Tastatur — die bedient jede App, die darauf
    // hört, und als Einziges auch die Lautstärke.
    var virtualKey = request.Action?.ToLowerInvariant() switch
    {
        "playpause" => VirtualKeys.VK_MEDIA_PLAY_PAUSE,
        "next" => VirtualKeys.VK_MEDIA_NEXT_TRACK,
        "prev" => VirtualKeys.VK_MEDIA_PREV_TRACK,
        "stop" => VirtualKeys.VK_MEDIA_STOP,
        "volup" => VirtualKeys.VK_VOLUME_UP,
        "voldown" => VirtualKeys.VK_VOLUME_DOWN,
        "mute" => VirtualKeys.VK_VOLUME_MUTE,
        _ => (ushort)0
    };

    if (virtualKey == 0)
    {
        return Results.BadRequest(new
        {
            error = $"Unbekannte Medien-Aktion '{request.Action}'.",
            allowed = new[] { "playpause", "next", "prev", "stop", "volup", "voldown", "mute" }
        });
    }

    try
    {
        // Lautstärke ist der einzige Fall, bei dem Wiederholung Sinn ergibt.
        var repeat = Math.Clamp(request.Repeat ?? 1, 1, 10);

        for (var i = 0; i < repeat; i++)
        {
            sender.KeyDown(virtualKey);
            sender.KeyUp(virtualKey);
        }

        return Results.Ok(new { status = "ok", action = request.Action, repeat });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Medien-Aktion {Action} fehlgeschlagen.", request.Action);
        return Results.Problem($"Medien-Aktion fehlgeschlagen: {ex.Message}");
    }
});

// Die beiden Dauerverbindungen melden sich an, solange sie stehen. Der Token,
// mit dem sie arbeiten, endet damit auch beim Widerruf des Geräts und nicht
// erst, wenn die Gegenseite von sich aus auflegt.
app.MapGet("/ws/input", async (HttpContext context, InputSocket socket, LiveConnections live) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "Erwartet wird eine WebSocket-Verbindung." });
    }

    using var lease = live.Open(ClientAuthMiddleware.ClientId(context), context.RequestAborted);
    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    // Abbrechen allein genügt nicht immer: hängt der Sendepuffer, wartet die
    // Schleife weiter. Abort() reißt die Verbindung sofort ab.
    using var cut = lease.Token.Register(webSocket.Abort);

    await socket.HandleAsync(webSocket, lease.Token);

    return Results.Empty;
});

app.MapGet("/ws/screen", async (HttpContext context, ScreenSocket screen, LiveConnections live) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "Erwartet wird eine WebSocket-Verbindung." });
    }

    var options = ScreenStreamOptions.FromQuery(context.Request.Query);

    using var lease = live.Open(ClientAuthMiddleware.ClientId(context), context.RequestAborted);
    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    using var cut = lease.Token.Register(webSocket.Abort);

    await screen.HandleAsync(webSocket, options, lease.Token);

    return Results.Empty;
});

// ---- WebRTC (Bild, Stufe 2) ----------------------------------------------
//
// Signalisierung in einem Schritt: die App schickt ihr Angebot, der Agent
// antwortet fertig. Trickle-ICE wäre hier nur Aufwand ohne Gewinn — im Tailnet
// gibt es genau einen Kandidaten, der zählt.

app.MapPost("/api/webrtc/offer", async (
    WebRtcOffer request, WebRtcRegistry registry, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Sdp))
    {
        return Results.BadRequest(new { error = "Angebot ohne SDP." });
    }

    var session = await registry.CreateAsync(
        request.Sdp,
        Math.Max(request.Monitor ?? 0, 0),
        Math.Clamp(request.Fps ?? 30, 1, 60),
        ClientAuthMiddleware.ClientId(context),
        context.RequestAborted);

    if (session is null)
    {
        // Kein Fehler, sondern eine Auskunft: die App nimmt dann den
        // JPEG-Stream. Deshalb 503 statt 500.
        return Results.Json(
            new { error = "H.264 steht nicht zur Verfügung (ffmpeg fehlt oder kein Encoder)." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        id = session.Id,
        sdp = session.AnswerSdp,
        encoder = session.Encoder,
        monitor = session.Monitor
    });
});

app.MapPost("/api/webrtc/{id}/monitor", async (
    string id, MonitorRequest request, WebRtcRegistry registry, HttpContext context) =>
{
    var session = registry.Find(id);

    if (session is null)
    {
        app.Logger.LogWarning("Monitorwechsel auf unbekannte Sitzung {Id}.", id);

        return Results.NotFound(new { error = "Unbekannte Sitzung." });
    }

    // Der Abbruch der Anfrage taugt hier **nicht** als Abbruchmarke für den
    // Strom: er gehört zu diesem einen Aufruf, der Strom läuft danach weiter.
    // Er begrenzt nur, wie lange auf das erste Bild gewartet wird.
    var switched = await session.SwitchMonitorAsync(
        Math.Max(request.Monitor, 0), context.RequestAborted);

    app.Logger.LogInformation(
        "Monitorwechsel auf {Wanted}: {Result}. Sitzung steht jetzt auf {Actual}.",
        request.Monitor,
        switched ? "übernommen" : "abgelehnt",
        session.Monitor);

    return switched
        ? Results.Ok(new { monitor = session.Monitor, encoder = session.Encoder })
        : Results.BadRequest(new { error = $"Monitor {request.Monitor} lässt sich nicht aufnehmen." });
});

app.MapDelete("/api/webrtc/{id}", async (string id, WebRtcRegistry registry) =>
    await registry.CloseAsync(id) ? Results.Ok(new { closed = true }) : Results.NotFound());

app.Logger.LogInformation(
    "RemoteDesktop-Agent lauscht auf Port {Port} als {Host}", settings.Port, Environment.MachineName);

app.Logger.LogInformation(
    authority is null
        ? "Zertifikat von Tailscale, ausgestellt auf {Name}. Nichts zu bestätigen."
        : "Selbst ausgestelltes Zertifikat für {Name}. Die eigene CA hat den Fingerabdruck "
          + "{Fingerprint} und liegt unter http://<adresse>:{TrustPort}/ca.crt bereit.",
    profile.AdvertisedAddress ?? CertificateLoader.DnsName(certificate) ?? "unbekannt",
    authority?.Fingerprint ?? string.Empty,
    settings.TrustPort);

app.Logger.LogInformation(
    "Gekoppelte Clients: {Count}. Altes Sammel-Token: {Legacy}.",
    app.Services.GetRequiredService<ClientStore>().List().Count,
    settings.Token is null ? "abgeschaltet" : "noch gültig");

app.Run();

/// <summary>Verbindungsangebot der App für den H.264-Stream.</summary>
internal sealed record WebRtcOffer(string Sdp, int? Monitor, int? Fps);

/// <summary>Monitorwechsel innerhalb einer laufenden WebRTC-Sitzung.</summary>
internal sealed record MonitorRequest(int Monitor);

internal sealed record PowerRequest(string Action);

internal sealed record MediaRequest(string Action, int? Repeat, string? Session);

/// <summary>Konfiguration des Agents, validiert beim Start statt beim ersten Zugriff.</summary>
/// <param name="Token">
/// Das alte geteilte Token. Seit Phase 10 freiwillig: fehlt es, kommt man nur
/// noch über eine Kopplung herein. Es bleibt bis Phase 12 unterstützt, damit
/// sich niemand vom eigenen Rechner aussperrt.
/// </param>
/// <param name="ClientsPath">Liste der gekoppelten Clients.</param>
/// <param name="IdentityPath">Privater Schlüssel des Agents selbst.</param>
/// <param name="ActionsPath">
/// Was dieser Rechner auf Zuruf tun darf. Fehlt die Datei, gibt es eben keine
/// Aktionen — das ist der Normalfall auf einem frisch eingerichteten Rechner.
/// </param>
/// <param name="BroadcastAddress">
/// Wohin das Magic Packet geht. Die allgemeine Broadcast-Adresse trifft jedes
/// Subnetz, das an derselben Leitung hängt; wer ein enges Netz hat, trägt die
/// eigene ein (etwa <c>192.168.178.255</c>).
/// </param>
/// <param name="UpdateRepository">
/// Wo die Releases liegen, als <c>owner/repo</c>. Konfigurierbar, damit ein
/// Fork sich aus seinem eigenen Repo aktualisiert statt aus dem fremden.
/// </param>
/// <param name="CertificatePath">
/// Das Zertifikat von <c>tailscale cert</c>. Seit V3 freiwillig: liegt keins da,
/// stellt sich der Agent selbst eins aus. Vorher war ein fehlender Eintrag der
/// Grund, gar nicht erst zu starten — und damit die Hürde, an der jeder hing,
/// der ohne VPN auskommen wollte.
/// </param>
/// <param name="DataDirectory">
/// Wo die selbst ausgestellten Zertifikate liegen. Vorgabe ist der Ordner des
/// konfigurierten Zertifikats, sonst <c>C:\ProgramData\RemoteDesktopAgent</c>.
/// </param>
/// <param name="TrustPort">
/// Der unverschlüsselte Port, auf dem ausschließlich das eigene CA-Zertifikat
/// abzuholen ist. Er wird nur geöffnet, wenn es eins gibt.
/// </param>
internal sealed record AgentSettings(
    string? Token,
    int Port,
    int TrustPort,
    string? CertificatePath,
    string? KeyPath,
    string DataDirectory,
    string NetworkConfigPath,
    string FfmpegPath,
    string ClientsPath,
    string IdentityPath,
    string ActionsPath,
    string BroadcastAddress,
    string UpdateRepository)
{
    public static AgentSettings Load(IConfiguration configuration)
    {
        var token = configuration["Agent:Token"]
                    ?? Environment.GetEnvironmentVariable("REMOTEDESKTOP_TOKEN");

        var port = configuration.GetValue("Agent:Port", 8443);
        var trustPort = configuration.GetValue("Agent:TrustPort", 8442);

        // Beide dürfen fehlen. Genau das war bis V3 ein Abbruch beim Start —
        // und von außen sah ein Rechner ohne Tailscale-Zertifikat aus wie einer,
        // der gar nicht läuft.
        var certificatePath = configuration["Agent:CertificatePath"];
        var keyPath = configuration["Agent:KeyPath"];

        // Ein Ordner für alles, neben der Programmdatei. Ein eingetragener Pfad
        // schlägt ihn — aber der aus einer alten appsettings.json zeigt in den
        // Ordner von damals, und der ist inzwischen leer.
        var dataDirectory = AgentPaths.Redirect(
            configuration["Agent:DataDirectory"], DefaultDataDirectory, LegacyDataDirectory)
            ?? DefaultDataDirectory;

        // Ohne Eintrag: dorthin legt „Zertifikat holen" die Dateien von
        // Tailscale. Liegen sie nicht da, stellt der Agent sich selbst eins aus
        // (siehe CertificateLoader) — der Eintrag ist also kein Zwang, sondern
        // die Stelle, an der nachgesehen wird.
        certificatePath = AgentPaths.Redirect(certificatePath, dataDirectory, LegacyDataDirectory)
                          ?? Path.Combine(dataDirectory, "cert.crt");

        keyPath = AgentPaths.Redirect(keyPath, dataDirectory, LegacyDataDirectory)
                  ?? Path.Combine(dataDirectory, "cert.key");

        var networkConfigPath = configuration["Agent:NetworkConfigPath"]
                                ?? Path.Combine(
                                    dataDirectory, RemoteDesktopSetup.NetworkConfig.FileName);

        // Ohne ffmpeg gibt es kein H.264 — der Agent läuft dann trotzdem, nur
        // eben mit dem JPEG-Stream. Deshalb hier kein Abbruch.
        var ffmpegPath = configuration["Agent:FfmpegPath"] ?? "ffmpeg";

        // Kopplungen und der eigene Schlüssel gehören zu den Daten, nicht zum
        // Programm: sie stehen im Datenordner. Die Aktionen sind Konfiguration
        // und bleiben neben der actions.example.json liegen.
        var clientsPath = Resolve(configuration["Agent:ClientsPath"], dataDirectory, "clients.json");
        var identityPath = Resolve(configuration["Agent:IdentityPath"], dataDirectory, "agentkey.txt");
        var actionsPath = Resolve(
            configuration["Agent:ActionsPath"], AppContext.BaseDirectory, "actions.json");

        var broadcastAddress = configuration["Agent:BroadcastAddress"] ?? "255.255.255.255";
        var updateRepository = configuration["Agent:UpdateRepository"] ?? "Davidodos/RemoteDesktop";

        return new AgentSettings(
            token, port, trustPort, certificatePath, keyPath, dataDirectory, networkConfigPath,
            ffmpegPath, clientsPath, identityPath, actionsPath, broadcastAddress,
            updateRepository);
    }

    /// <summary>
    /// Der Datenordner: <c>data\</c> neben der Programmdatei. Nur Administratoren
    /// und das System dürfen hinein — der Schlüssel des Agents liegt im Klartext,
    /// und wer ihn hat, ist der Agent.
    /// </summary>
    private static string DefaultDataDirectory => AgentPaths.For(AppContext.BaseDirectory);

    /// <summary>Wo die Daten bis v1.2.0 lagen.</summary>
    private static string LegacyDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        AgentPaths.LegacyFolderName);

    private static string Resolve(string? configured, string folder, string name) =>
        configured is null
            ? Path.Combine(folder, name)
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, configured);
}
