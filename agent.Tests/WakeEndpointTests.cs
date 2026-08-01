using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RemoteDesktopAgent.Api;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// <c>POST /api/wol</c> über einen echten Testserver.
///
/// Zwei Dinge lassen sich nur hier zeigen: dass die MAC aus der Anfrage kommt
/// und nirgends auf diesem Rechner konfiguriert ist, und dass die Begrenzung
/// als 429 nach außen geht statt als Fehler.
/// </summary>
public class WakeEndpointTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private AufgezeichneterSender _sender = null!;

    public async Task InitializeAsync()
    {
        _sender = new AufgezeichneterSender();

        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IMagicPacketSender>(_sender);
        builder.Services.AddSingleton<WakeService>();

        var app = builder.Build();
        app.MapWakeEndpoints();

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

    [Fact]
    public async Task Die_MAC_aus_der_Anfrage_geht_hinaus()
    {
        var antwort = await _client.PostAsJsonAsync("/api/wol", new { mac = "AA:BB:CC:DD:EE:FF" });

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal(["aa:bb:cc:dd:ee:ff"], _sender.Gesendet);
    }

    [Fact]
    public async Task Eine_unbrauchbare_MAC_ergibt_400_und_kein_Paket()
    {
        var antwort = await _client.PostAsJsonAsync("/api/wol", new { mac = "der-pc" });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Empty(_sender.Gesendet);
    }

    [Fact]
    public async Task Ohne_MAC_ergibt_es_400_statt_500()
    {
        var antwort = await _client.PostAsJsonAsync("/api/wol", new { });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Fact]
    public async Task Zu_viele_Versuche_ergeben_429()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await _client.PostAsJsonAsync("/api/wol", new { mac = "aa:bb:cc:dd:ee:ff" });
        }

        var antwort = await _client.PostAsJsonAsync("/api/wol", new { mac = "aa:bb:cc:dd:ee:ff" });

        Assert.Equal(HttpStatusCode.TooManyRequests, antwort.StatusCode);
    }

    /// <summary>
    /// Der Waker und der Agent führen bewusst keine Geräteliste. Ein Endpunkt,
    /// der eine ausliefert oder pflegt, wäre der Rückfall in das, was Phase 14
    /// abgeschafft hat.
    /// </summary>
    [Fact]
    public async Task Es_gibt_keine_Geraeteliste_hinter_dem_Weck_Endpunkt()
    {
        foreach (var pfad in new[] { "/api/wol", "/api/wol/pc", "/api/devices" })
        {
            var antwort = await _client.GetAsync(pfad);

            Assert.True(
                antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"GET {pfad} ergab {(int)antwort.StatusCode}.");
        }
    }
}
