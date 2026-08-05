using Xunit;

namespace RemoteDesktopSetup.Tests;

public class SelectionTests
{
    [Fact]
    public void Nur_Tailscale_ist_keine_Installation()
    {
        var selection = new Selection(SetupComponent.Tailscale, AutostartMode.None);

        Assert.NotNull(selection.Rejection);
    }

    [Theory]
    [InlineData(SetupComponent.Agent)]
    [InlineData(SetupComponent.Client)]
    [InlineData(SetupComponent.Agent | SetupComponent.Client)]
    [InlineData(SetupComponent.Client | SetupComponent.Tailscale)]
    public void Agent_oder_Client_genuegt(SetupComponent components)
    {
        Assert.Null(new Selection(components, AutostartMode.None).Rejection);
    }

    [Fact]
    public void Was_nicht_installiert_wird_startet_auch_nicht_mit()
    {
        // Der Fehler, den das verhindert: ein Autostart-Eintrag, der auf eine
        // Datei zeigt, die es auf diesem Rechner nie gab. Er fiele erst beim
        // nächsten Anmelden auf, weit weg von der Stelle, an der er entstand.
        var nurClient = new Selection(SetupComponent.Client, AutostartMode.Both).Normalized();

        Assert.Equal(AutostartMode.Client, nurClient.Autostart);
    }

    [Fact]
    public void Ohne_Client_bleibt_der_Agent_im_Autostart()
    {
        var nurAgent = new Selection(SetupComponent.Agent, AutostartMode.Both).Normalized();

        Assert.Equal(AutostartMode.Agent, nurAgent.Autostart);
    }

    [Fact]
    public void Die_Vorgabe_ist_in_sich_stimmig()
    {
        Assert.Null(Selection.Default.Rejection);
        Assert.Equal(Selection.Default.Autostart, Selection.Default.Normalized().Autostart);
    }
}

public class AutostartTests
{
    /// <summary>Ein Rechner, auf dem nichts eingestellt ist.</summary>
    private sealed class Host : IAutostartHost
    {
        public ServiceStart Service { get; private set; } = ServiceStart.Manual;

        public bool Entry { get; private set; }

        public void SetServiceStart(ServiceStart start) => Service = start;

        public void SetClientEntry(bool enabled) => Entry = enabled;

        public AutostartPlan Current() => new(Service, Entry);
    }

    [Fact]
    public void Beides_setzt_Dienst_und_Eintrag()
    {
        var host = new Host();

        Autostart.Apply(host, AutostartMode.Both);

        Assert.Equal(ServiceStart.Automatic, host.Service);
        Assert.True(host.Entry);
    }

    [Fact]
    public void Nur_der_Client_laesst_den_Dienst_auf_manuell()
    {
        var host = new Host();

        Autostart.Apply(host, AutostartMode.Client);

        Assert.Equal(ServiceStart.Manual, host.Service);
        Assert.True(host.Entry);
    }

    [Fact]
    public void Kein_Autostart_deinstalliert_den_Dienst_nicht()
    {
        // „Nicht automatisch starten" heißt nicht „weg damit". Wer den Autostart
        // abschaltet, soll den Agent später noch von Hand starten können.
        var host = new Host();
        Autostart.Apply(host, AutostartMode.Both);

        Autostart.Apply(host, AutostartMode.None);

        Assert.Equal(ServiceStart.Manual, host.Service);
        Assert.False(host.Entry);
    }

    [Theory]
    [InlineData(AutostartMode.None)]
    [InlineData(AutostartMode.Agent)]
    [InlineData(AutostartMode.Client)]
    [InlineData(AutostartMode.Both)]
    public void Was_gesetzt_wurde_wird_auch_wieder_gelesen(AutostartMode mode)
    {
        var host = new Host();

        Autostart.Apply(host, mode);

        Assert.Equal(mode, Autostart.Read(host));
    }

    [Fact]
    public void Jeder_Modus_hat_einen_Satz_statt_eines_Namens()
    {
        foreach (var mode in new[]
                 {
                     AutostartMode.None, AutostartMode.Agent,
                     AutostartMode.Client, AutostartMode.Both
                 })
        {
            Assert.NotEmpty(mode.Describe());
            Assert.DoesNotContain("Mode", mode.Describe());
        }
    }
}

public class CoordinatorTests
{
    [Fact]
    public void Ohne_Eintrag_gilt_Tailscale()
    {
        Assert.True(Coordinator.From(null).IsTailscale);
        Assert.True(Coordinator.From("   ").IsTailscale);
    }

    [Fact]
    public void Bei_Tailscale_bleibt_das_Argument_weg()
    {
        // Tailscale kennt seinen eigenen Dienst. Ein überflüssiges Argument wäre
        // eine überflüssige Fehlerquelle.
        Assert.Equal(new[] { "up" }, Coordinator.Default.UpArguments());
    }

    [Fact]
    public void Ein_eigener_Koordinator_wird_uebergeben()
    {
        var eigener = Coordinator.From("https://headscale.example.org/");

        Assert.Equal(
            new[] { "up", "--login-server=https://headscale.example.org" },
            eigener.UpArguments());
    }

    [Fact]
    public void Klartext_wird_abgelehnt()
    {
        // Über den Koordinator läuft der Schlüsselaustausch des ganzen Netzes.
        Assert.NotNull(Coordinator.From("http://headscale.example.org").Rejection);
    }

    [Fact]
    public void Etwas_das_keine_Adresse_ist_wird_abgelehnt()
    {
        Assert.NotNull(Coordinator.From("headscale.example.org").Rejection);
        Assert.Null(Coordinator.From("https://headscale.example.org").Rejection);
    }

    [Fact]
    public void Die_Argumente_gehen_einzeln_hinaus()
    {
        // Dieselbe Regel wie bei den Aktionen aus Phase 13: keine Stelle, an der
        // Windows entscheidet, was eine Zeichenkette bedeutet.
        var arguments = Coordinator.From("https://headscale.example.org").UpArguments();

        Assert.All(arguments, argument => Assert.DoesNotContain(" ", argument));
    }
}

public class SetupStepsTests
{
    private sealed record Probe(
        bool HasTailscale = false,
        bool IsConnected = false,
        string TailnetName = "",
        bool HasCertificate = false,
        bool HasService = false) : ISetupProbe;

    [Fact]
    public void Ohne_Agent_entfallen_Zertifikat_und_Dienst()
    {
        var steps = SetupSteps.For(
            new Selection(SetupComponent.Client, AutostartMode.Client),
            new Probe());

        Assert.DoesNotContain(steps, step => step.Title.Contains("Zertifikat"));
        Assert.DoesNotContain(steps, step => step.Title.Contains("Agent"));
    }

    [Fact]
    public void Wer_Tailscale_schon_hat_faengt_weiter_hinten_an()
    {
        var steps = SetupSteps.For(
            Selection.Default,
            new Probe(HasTailscale: true, IsConnected: true, TailnetName: "pc.example.ts.net"));

        Assert.Equal("Zertifikat holen", SetupSteps.Next(steps)?.Title);
    }

    [Fact]
    public void Der_erste_offene_Schritt_ist_der_erste_offene()
    {
        Assert.Equal("Tailscale installieren", SetupSteps.Next(SetupSteps.For(
            Selection.Default, new Probe()))?.Title);
    }

    [Fact]
    public void Koppeln_haelt_die_Einrichtung_nicht_auf()
    {
        // Es ist der einzige Schritt, der nie abgehakt wird — ein weiteres Handy
        // hinzuzunehmen bleibt immer möglich.
        var steps = SetupSteps.For(
            Selection.Default,
            new Probe(HasTailscale: true, IsConnected: true, HasCertificate: true,
                HasService: true));

        Assert.True(SetupSteps.Ready(steps));
        Assert.Equal("Handy koppeln", SetupSteps.Next(steps)?.Title);
    }

    [Fact]
    public void Solange_etwas_fehlt_ist_es_nicht_bereit()
    {
        var steps = SetupSteps.For(
            Selection.Default,
            new Probe(HasTailscale: true, IsConnected: true, HasCertificate: true));

        Assert.False(SetupSteps.Ready(steps));
    }

    [Fact]
    public void Jeder_Schritt_erklaert_sich_ohne_Fachwort()
    {
        // Die Zusage dieser Phase: Meldungen für Menschen ohne Vorwissen. Wer
        // hier ein Fachwort einbaut, merkt es beim Testlauf.
        var verboten = new[] { "Tailnet", "MagicDNS", "Zertifikatskette", "Endpoint", "Scope" };

        foreach (var step in SetupSteps.For(Selection.Default, new Probe()))
        {
            Assert.NotEmpty(step.Explanation);
            Assert.All(verboten, wort => Assert.DoesNotContain(wort, step.Explanation));
        }
    }
}

public class CoordinatorConfigTests
{
    [Fact]
    public void Was_geschrieben_wurde_wird_wieder_gelesen()
    {
        var eigener = Coordinator.From("https://headscale.example.org");

        Assert.Equal(eigener, CoordinatorConfig.Read(CoordinatorConfig.Write(eigener)));
    }

    [Fact]
    public void Eine_beschaedigte_Datei_kostet_nur_die_Abweichung()
    {
        // Sie darf die Einrichtung nicht verhindern — sonst steht jemand vor
        // einem Programm, das nicht startet, weil eine Textdatei kaputt ist.
        Assert.True(CoordinatorConfig.Read("{ kein JSON").IsTailscale);
        Assert.True(CoordinatorConfig.Read("{}").IsTailscale);
        Assert.True(CoordinatorConfig.Read(null).IsTailscale);
    }
}

public class ReleaseCheckTests
{
    private const string Release = """
        {
          "tag_name": "v1.2.0",
          "assets": [
            { "name": "remotedesktop.apk", "browser_download_url": "https://example.org/apk" },
            { "name": "RemoteDesktop-Setup-1.2.0.exe",
              "browser_download_url": "https://example.org/setup.exe" }
          ]
        }
        """;

    [Fact]
    public void Findet_den_Installer_zwischen_den_anderen_Anhaengen()
    {
        var offer = ReleaseCheck.Find(Release);

        Assert.Equal("1.2.0", offer?.Version);
        Assert.Equal("https://example.org/setup.exe", offer?.Url);
    }

    [Fact]
    public void Ohne_Installer_im_Release_gibt_es_kein_Angebot()
    {
        // Ein Release, das nur den Agent und die APK enthält — dann gibt es für
        // Windows nichts zu holen, und das ist kein Fehler.
        var ohne = """
            { "tag_name": "v1.2.0", "assets": [
              { "name": "remotedesktop.apk", "browser_download_url": "https://example.org/apk" } ] }
            """;

        Assert.Null(ReleaseCheck.Find(ohne));
    }

    [Fact]
    public void Was_kein_Release_ist_bleibt_folgenlos()
    {
        Assert.Null(ReleaseCheck.Find("kein JSON"));
        Assert.Null(ReleaseCheck.Find("{}"));
        Assert.Null(ReleaseCheck.Find(""));
        Assert.Null(ReleaseCheck.Find(null));
    }

    [Fact]
    public void Dieselbe_Fassung_wird_nicht_angeboten()
    {
        Assert.False(ReleaseCheck.IsWorthInstalling(ReleaseCheck.Find(Release), "1.2.0"));
        Assert.False(ReleaseCheck.IsWorthInstalling(ReleaseCheck.Find(Release), "v1.2.0"));
    }

    [Fact]
    public void Eine_andere_Fassung_wird_angeboten()
    {
        Assert.True(ReleaseCheck.IsWorthInstalling(ReleaseCheck.Find(Release), "1.1.0"));
    }

    [Fact]
    public void Ohne_bekannte_eigene_Fassung_wird_angeboten()
    {
        // Verschweigen wäre die schlechtere Antwort als einmal zu viel fragen.
        Assert.True(ReleaseCheck.IsWorthInstalling(ReleaseCheck.Find(Release), null));
        Assert.True(ReleaseCheck.IsWorthInstalling(ReleaseCheck.Find(Release), "  "));
    }

    [Fact]
    public void Der_Installer_laeuft_leise_aber_nicht_unsichtbar()
    {
        // Einen Fortschrittsbalken darf man sehen, wenn gerade der eigene
        // Rechner umgebaut wird.
        Assert.Contains("/SILENT", ReleaseCheck.InstallArguments());
        Assert.DoesNotContain("/VERYSILENT", ReleaseCheck.InstallArguments());
    }
}

/// <summary>
/// Die drei Netzmodi. Sie sind der Kern des Befunds „der Agent startet nicht
/// ohne Tailscale": bis Release v1.0.0 gab es genau einen Weg, und wer ihn nicht
/// gehen wollte, bekam einen Dienst, der sich sofort wieder beendete.
/// </summary>
public class NetworkProfileTests
{
    private static NetworkProfile Lan(string address) =>
        new(NetworkKind.Lan, address, Coordinator.Default);

    [Fact]
    public void Im_Heimnetz_braucht_es_kein_Tailscale()
    {
        Assert.False(Lan("192.168.178.20").NeedsTailscale);
        Assert.False(new NetworkProfile(NetworkKind.Vpn, "10.8.0.3", Coordinator.Default)
            .NeedsTailscale);
        Assert.True(NetworkProfile.Default.NeedsTailscale);
    }

    [Fact]
    public void Bei_Tailscale_wird_keine_Adresse_verlangt()
    {
        // Sie steht im Zertifikat. Eine zweite, von Hand gepflegte Quelle wäre
        // eine, die irgendwann abweicht.
        Assert.False(NetworkProfile.Default.NeedsOwnAddress);
        Assert.Null(NetworkProfile.Default.Rejection);
        Assert.Null(NetworkProfile.Default.AdvertisedAddress);
    }

    [Theory]
    [InlineData("192.168.178.20")]
    [InlineData("pc.fritz.box")]
    [InlineData("laptop")]
    [InlineData("10.8.0.3")]
    [InlineData("[fd7a::1]")]
    public void Ein_Name_oder_eine_IP_genuegt(string address)
    {
        Assert.Null(Lan(address).Rejection);
    }

    [Fact]
    public void Die_ganze_Adresse_mit_Schema_wird_abgelehnt()
    {
        // Genau das trägt man ein, wenn man es nicht besser weiß. Als
        // Zertifikatsname ergäbe es einen, den kein Client je vorzeigt — und der
        // Fehler fiele erst beim Verbinden auf.
        Assert.Contains("https://", Lan("https://192.168.178.20").Rejection);
        Assert.Contains("Port", Lan("192.168.178.20:8443").Rejection);
        Assert.NotNull(Lan("192.168.178.20/steuern").Rejection);
    }

    [Fact]
    public void Ohne_Adresse_steht_da_was_fehlt()
    {
        var rejection = Lan("   ").Rejection;

        Assert.NotNull(rejection);
        Assert.Contains("192.168", rejection);
    }

    [Fact]
    public void Die_Adresse_wird_kleingeschrieben_und_entklammert()
    {
        // Kleinbuchstaben, weil der Name so ins Zertifikat und in den QR-Code
        // geht. Wer „PC.Fritz.Box" einträgt, soll nicht daran scheitern.
        Assert.Equal("pc.fritz.box", Lan("  PC.Fritz.Box ").Normalized().Address);
        Assert.Equal("fd7a::1", Lan("[fd7a::1]").Normalized().Address);
    }

    [Fact]
    public void Jeder_Modus_erklaert_sich_ohne_Fachwort()
    {
        foreach (var kind in Enum.GetValues<NetworkKind>())
        {
            var satz = new NetworkProfile(kind, "pc", Coordinator.Default).Describe();

            Assert.NotEmpty(satz);
            Assert.All(
                new[] { "Tailnet", "MagicDNS", "SAN", "Endpoint" },
                wort => Assert.DoesNotContain(wort, satz));
        }
    }

    [Fact]
    public void Ein_kaputter_Koordinator_stoert_nur_bei_Tailscale()
    {
        // Im Heimnetz wird er nie benutzt. Eine Fehlermeldung über einen
        // Koordinator, den niemand fragt, hielte die Einrichtung grundlos auf.
        var kaputt = Coordinator.From("http://headscale.example.org");

        Assert.NotNull(new NetworkProfile(NetworkKind.Tailscale, "", kaputt).Rejection);
        Assert.Null(new NetworkProfile(NetworkKind.Lan, "pc", kaputt).Rejection);
    }
}

public class NetworkConfigTests
{
    [Fact]
    public void Was_geschrieben_wurde_wird_wieder_gelesen()
    {
        var profil = new NetworkProfile(
            NetworkKind.Vpn, "10.8.0.3", Coordinator.From("https://headscale.example.org"));

        Assert.Equal(profil, NetworkConfig.Read(NetworkConfig.Write(profil)));
    }

    [Fact]
    public void Eine_Datei_aus_der_Zeit_davor_meint_Tailscale()
    {
        // Sie führt nur den Koordinator. Ein Update darf den Modus nicht stumm
        // wechseln — der Rechner wäre danach über Tailscale nicht mehr
        // erreichbar und niemand wüsste warum.
        var alt = NetworkConfig.Read("""{ "coordinator": "https://headscale.example.org" }""");

        Assert.Equal(NetworkKind.Tailscale, alt.Kind);
        Assert.Equal("https://headscale.example.org", alt.Coordinator.Address);
    }

    [Fact]
    public void Eine_beschaedigte_Datei_kostet_nur_die_Abweichung()
    {
        Assert.Equal(NetworkProfile.Default, NetworkConfig.Read("{ kein JSON"));
        Assert.Equal(NetworkProfile.Default, NetworkConfig.Read("{}"));
        Assert.Equal(NetworkProfile.Default, NetworkConfig.Read(null));
    }

    [Fact]
    public void Ein_unbekannter_Modus_ist_kein_Fehler()
    {
        // Die Datei darf von Hand bearbeitet werden. Ein Tippfehler kostet die
        // Abweichung, nicht den Dienst.
        Assert.Equal(NetworkKind.Tailscale, NetworkConfig.Read("""{ "network": "quatsch" }""").Kind);
    }
}
