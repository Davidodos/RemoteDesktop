using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RemoteDesktopAgent.Actions;
using RemoteDesktopAgent.Api;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die beiden Endpunkte über einen echten Testserver — ohne Zertifikat, ohne
/// Windows.
///
/// Zwei Dinge lassen sich nur hier belegen und nicht am Katalog: dass eine
/// unbekannte Kennung als 404 zurückkommt und nicht als 500, und dass die
/// Liste über das Netz wirklich keine Pfade preisgibt. Der zweite Punkt ist
/// keine Kleinigkeit — wer auslösen darf, muss nicht auch erfahren, welche
/// Software auf dem Rechner liegt und wo.
/// </summary>
public class ActionEndpointTests : IAsyncLifetime
{
    private const string Json =
        """
        [{ "id": "backup", "label": "Backup", "icon": "archive", "type": "script",
           "file": "C:\\Scripts\\backup.ps1", "confirm": true },
         { "id": "rechner", "label": "Rechner", "type": "process", "file": "C:\\calc.exe" }]
        """;

    private IHost _host = null!;
    private HttpClient _client = null!;
    private HostAufzeichnung _aufzeichnung = null!;

    public async Task InitializeAsync()
    {
        _aufzeichnung = new HostAufzeichnung();

        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(ActionCatalog.Parse(Json, _ => true));
        builder.Services.AddSingleton<IActionHost>(_aufzeichnung);
        builder.Services.AddSingleton(provider =>
            new ActionRunner(provider.GetRequiredService<IActionHost>()));

        var app = builder.Build();
        app.MapActionEndpoints();

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
    public async Task Eine_unbekannte_Kennung_ergibt_404_und_nicht_500()
    {
        // Eine Kennung, die es nicht gibt, ist eine veraltete App — kein Fehler
        // dieses Rechners. Ein 500 schickte den Nutzer auf die falsche Fährte.
        var antwort = await _client.PostAsync("/api/actions/gibt-es-nicht/invoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Eine_bekannte_Kennung_loest_aus()
    {
        // Act
        var antwort = await _client.PostAsync("/api/actions/rechner/invoke", content: null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("C:\\calc.exe", Assert.Single(_aufzeichnung.Gestartet).FileName);
    }

    [Fact]
    public async Task Die_Liste_nennt_Kennung_Beschriftung_und_den_Merker()
    {
        // Act
        var antwort = await _client.GetFromJsonAsync<JsonElement>("/api/actions");

        // Assert
        var aktionen = antwort.GetProperty("actions").EnumerateArray().ToList();

        Assert.Equal(2, aktionen.Count);
        Assert.Equal("backup", aktionen[0].GetProperty("id").GetString());
        Assert.Equal("Backup", aktionen[0].GetProperty("label").GetString());
        Assert.Equal("script", aktionen[0].GetProperty("type").GetString());
        Assert.True(aktionen[0].GetProperty("confirm").GetBoolean());
    }

    [Fact]
    public async Task Die_Liste_gibt_keine_Pfade_ueber_das_Netz_heraus()
    {
        // Act
        var roh = await _client.GetStringAsync("/api/actions");

        // Assert — das ist der Punkt, an dem sich entscheidet, ob die Auskunft
        // harmlos ist oder eine Landkarte des Rechners.
        Assert.DoesNotContain("backup.ps1", roh);
        Assert.DoesNotContain("Scripts", roh);
        Assert.DoesNotContain("calc.exe", roh);
    }

    [Fact]
    public async Task Es_gibt_keinen_Weg_die_Liste_ueber_das_Netz_zu_aendern()
    {
        // Ein Schreib-Endpunkt hieße „jeder gültige Ausweis darf beliebigen Code
        // auf diesem Rechner hinterlegen". Geprüft wird deshalb nicht nur, dass
        // keiner gebaut wurde, sondern dass auch keiner versehentlich entsteht.
        foreach (var pfad in new[] { "/api/actions", "/api/actions/backup" })
        {
            using var put = new HttpRequestMessage(HttpMethod.Put, pfad);
            using var post = new HttpRequestMessage(HttpMethod.Post, pfad);
            using var delete = new HttpRequestMessage(HttpMethod.Delete, pfad);

            foreach (var anfrage in new[] { put, post, delete })
            {
                var antwort = await _client.SendAsync(anfrage);

                Assert.True(
                    antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    $"{anfrage.Method} {pfad} ergab {(int)antwort.StatusCode}.");
            }
        }
    }
}
