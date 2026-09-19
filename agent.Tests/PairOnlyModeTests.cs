using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Der Agent, gestartet nur zum Koppeln: Kopplung ja, alles andere nein. Ein
/// Rechner, der nur andere steuert, darf dabei nicht nebenbei steuerbar werden.
/// </summary>
public class PairOnlyModeTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        var app = builder.Build();

        app.UsePairOnly();

        // Hinter der Sperre antwortet jeder Pfad mit 200 — was ankommt, hat sie
        // durchgelassen.
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await app.StartAsync();

        _host = app;
        _client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/info")]
    [InlineData("/api/pair")]
    [InlineData("/api/pair/code")]
    [InlineData("/api/pair/code/cancel")]
    [InlineData("/api/clients")]
    [InlineData("/api/quit")]
    public async Task Die_Kopplung_bleibt_erreichbar(string path)
    {
        var antwort = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    [Theory]
    [InlineData("/ws/screen")]
    [InlineData("/ws/input")]
    [InlineData("/api/session")]
    [InlineData("/api/power")]
    [InlineData("/api/webrtc/offer")]
    [InlineData("/api/pairing")]
    public async Task Alles_andere_ist_gesperrt(string path)
    {
        var antwort = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, antwort.StatusCode);
    }
}
