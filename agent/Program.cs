using RemoteDesktopAgent.Api;
using RemoteDesktopAgent.Auth;
using RemoteDesktopAgent.Capture.H264;
using RemoteDesktopAgent.Native;
using RemoteDesktopAgent.Services;

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

var settings = AgentSettings.Load(builder.Configuration);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(settings.Port, listen =>
        listen.UseHttps(CertificateLoader.Load(settings.CertificatePath, settings.KeyPath)));
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
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton(provider =>
    new ClientAuth(provider.GetRequiredService<SessionStore>(), settings.Token));

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

// Selbst-Update: läuft nur, wenn Agent:HubUrl und Agent:HubToken gesetzt sind.
builder.Services.AddHttpClient();
builder.Services.AddHostedService<SelfUpdater>();

// Hält die WebRTC-Sitzungen samt ihrer ffmpeg-Prozesse — einer für alle.
builder.Services.AddSingleton<WebRtcRegistry>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseCors();
app.UseClientAuth();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPairingEndpoints();

// Hostname und Monitor-Layout — die App baut daraus ihre Monitor-Tabs.
app.MapGet("/api/info", (InputExecutor executor) =>
{
    var (monitors, desktop) = executor.GetLayout();

    return Results.Ok(new
    {
        hostname = Environment.MachineName,
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

app.MapGet("/ws/input", async (HttpContext context, InputSocket socket) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "Erwartet wird eine WebSocket-Verbindung." });
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await socket.HandleAsync(webSocket, context.RequestAborted);

    return Results.Empty;
});

app.MapGet("/ws/screen", async (HttpContext context, ScreenSocket screen) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "Erwartet wird eine WebSocket-Verbindung." });
    }

    var options = ScreenStreamOptions.FromQuery(context.Request.Query);

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await screen.HandleAsync(webSocket, options, context.RequestAborted);

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
        return Results.NotFound(new { error = "Unbekannte Sitzung." });
    }

    var switched = await session.SwitchMonitorAsync(
        Math.Max(request.Monitor, 0), context.RequestAborted);

    return switched
        ? Results.Ok(new { monitor = session.Monitor, encoder = session.Encoder })
        : Results.BadRequest(new { error = $"Monitor {request.Monitor} lässt sich nicht aufnehmen." });
});

app.MapDelete("/api/webrtc/{id}", async (string id, WebRtcRegistry registry) =>
    await registry.CloseAsync(id) ? Results.Ok(new { closed = true }) : Results.NotFound());

app.Logger.LogInformation(
    "RemoteDesktop-Agent lauscht auf Port {Port} als {Host}", settings.Port, Environment.MachineName);

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
internal sealed record AgentSettings(
    string? Token,
    int Port,
    string CertificatePath,
    string KeyPath,
    string FfmpegPath,
    string ClientsPath,
    string IdentityPath)
{
    public static AgentSettings Load(IConfiguration configuration)
    {
        var token = configuration["Agent:Token"]
                    ?? Environment.GetEnvironmentVariable("REMOTEDESKTOP_TOKEN");

        var port = configuration.GetValue("Agent:Port", 8443);

        var certificatePath = configuration["Agent:CertificatePath"]
                              ?? throw new InvalidOperationException(
                                  "Agent:CertificatePath fehlt (Ausgabe von 'tailscale cert').");

        var keyPath = configuration["Agent:KeyPath"]
                      ?? throw new InvalidOperationException("Agent:KeyPath fehlt.");

        // Ohne ffmpeg gibt es kein H.264 — der Agent läuft dann trotzdem, nur
        // eben mit dem JPEG-Stream. Deshalb hier kein Abbruch.
        var ffmpegPath = configuration["Agent:FfmpegPath"] ?? "ffmpeg";

        // Beide liegen neben der appsettings.json, also im Verzeichnis der .exe.
        // Ein absoluter Pfad in der Konfiguration schlägt das aus.
        var clientsPath = Resolve(configuration["Agent:ClientsPath"] ?? "clients.json");
        var identityPath = Resolve(configuration["Agent:IdentityPath"] ?? "agentkey.txt");

        return new AgentSettings(
            token, port, certificatePath, keyPath, ffmpegPath, clientsPath, identityPath);
    }

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
