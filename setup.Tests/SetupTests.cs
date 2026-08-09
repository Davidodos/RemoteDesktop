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

        // Die Adresse kommt vor dem Zertifikat: sie ist der Name, auf den das
        // Zertifikat lauten muss. Andersherum holte man eins auf gut Glück.
        Assert.Equal(SetupSteps.AddressStep, SetupSteps.Next(steps)?.Title);

        var mitAdresse = SetupSteps.For(
            Selection.Default,
            new Probe(HasTailscale: true, IsConnected: true, TailnetName: "pc.example.ts.net"),
            new NetworkProfile(NetworkKind.Tailscale, "pc.example.ts.net", Coordinator.Default));

        Assert.Equal("Zertifikat holen", SetupSteps.Next(mitAdresse)?.Title);
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
                HasService: true),
            new NetworkProfile(NetworkKind.Tailscale, "pc.example.ts.net", Coordinator.Default));

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
    public void Die_Commit_Kennung_im_Dateikopf_ist_keine_neue_Fassung()
    {
        // Der Befund aus Release v1.1.0: seit .NET 8 steht in der installierten
        // .exe nicht "1.2.0", sondern "1.2.0+<commit>". Verglichen wurde das
        // gegen den nackten Git-Tag — die App bot deshalb bei jeder Suche ein
        // Update auf genau die Fassung an, die schon lief.
        Assert.False(ReleaseCheck.IsWorthInstalling(
            ReleaseCheck.Find(Release), "1.2.0+435992d47c60e8d9890ee4e4a00aa1025499ffad"));

        Assert.False(ReleaseCheck.IsWorthInstalling(
            ReleaseCheck.Find(Release), "v1.2.0+435992d47c60e8d9890ee4e4a00aa1025499ffad"));
    }

    [Fact]
    public void Aufgeraeumt_wird_das_v_und_alles_hinter_dem_Plus()
    {
        Assert.Equal("1.2.0", ReleaseCheck.Normalize("v1.2.0"));
        Assert.Equal("1.2.0", ReleaseCheck.Normalize("1.2.0"));
        Assert.Equal("1.2.0", ReleaseCheck.Normalize("  v1.2.0+abcdef1  "));

        // Eine Vorabfassung *ist* ein Unterschied und bleibt deshalb stehen.
        Assert.Equal("1.2.0-rc.1", ReleaseCheck.Normalize("v1.2.0-rc.1"));

        Assert.Equal(string.Empty, ReleaseCheck.Normalize(null));
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
    public void Auch_bei_Tailscale_wird_die_Adresse_verlangt()
    {
        // Sie durfte einmal fehlen — dann kam der Name aus dem Zertifikat. Wer
        // aber keins geholt hatte, bekam ein selbst ausgestelltes mit dem
        // Windows-Rechnernamen darin, und genau der landete im QR-Code. Eine
        // Adresse, die man weglassen darf, ist eine, die irgendwann fehlt.
        Assert.NotNull(NetworkProfile.Default.Rejection);
        Assert.Null(NetworkProfile.Default.AdvertisedAddress);

        foreach (var kind in Enum.GetValues<NetworkKind>())
        {
            Assert.NotNull(new NetworkProfile(kind, "", Coordinator.Default).Rejection);
        }
    }

    [Fact]
    public void Headscale_ist_ein_eigener_Modus_und_verlangt_seinen_Server()
    {
        var ohne = new NetworkProfile(NetworkKind.Headscale, "pc", Coordinator.Default);
        var mit = new NetworkProfile(
            NetworkKind.Headscale, "pc", Coordinator.From("https://headscale.example.org"));

        Assert.NotNull(ohne.Rejection);
        Assert.Null(mit.Rejection);

        // Derselbe Client, nur ein anderer Koordinator — und kein Zertifikat:
        // die Stelle, die eins ausstellt, gehört zum Dienst von Tailscale.
        Assert.True(mit.NeedsTailscale);
        Assert.False(mit.CanFetchCertificate);
        Assert.True(NetworkProfile.Default.CanFetchCertificate);
    }

    [Fact]
    public void Ein_Koordinator_ausserhalb_von_Headscale_faellt_weg()
    {
        // Sonst schickte ein stehengebliebener Eintrag aus einem verlassenen
        // Modus das nächste `tailscale up` an den falschen Server.
        var profil = new NetworkProfile(
            NetworkKind.Tailscale, "pc.tailnet-1234.ts.net",
            Coordinator.From("https://headscale.example.org")).Normalized();

        Assert.Equal(Coordinator.Default, profil.Coordinator);
    }

    [Fact]
    public void Eine_alte_Tailscale_Datei_mit_eigenem_Koordinator_ist_Headscale()
    {
        // Vor v1.3.0 gab es Headscale nur als Zusatzfeld unter „Tailscale". Ein
        // Update darf die Unterscheidung nicht dadurch verlieren, dass es sie
        // einführt.
        var gelesen = NetworkConfig.Read("""
            { "network": "tailscale", "address": "pc", "coordinator": "https://hs.example.org" }
            """);

        Assert.Equal(NetworkKind.Headscale, gelesen.Kind);
        Assert.Equal("https://hs.example.org", gelesen.Coordinator.Address);
    }

    [Fact]
    public void Bei_Tailscale_gilt_eine_eingetragene_Adresse_trotzdem()
    {
        // Der Befund: ohne Zertifikat von Tailscale stellt der Agent sich selbst
        // eins aus, und darin steht der Windows-Rechnername. Genau der landete
        // im QR-Code — und unter dem findet das Handy im Tailnet nichts. Wer den
        // Namen im Tailnet einträgt, muss ihn also wiederbekommen.
        var profile = new NetworkProfile(
            NetworkKind.Tailscale, "pc.tailnet-1234.ts.net", Coordinator.Default);

        Assert.Equal("pc.tailnet-1234.ts.net", profile.AdvertisedAddress);
        Assert.Null(profile.Rejection);
    }

    [Fact]
    public void Eine_unbrauchbare_Tailnet_Adresse_wird_auch_bei_Tailscale_abgelehnt()
    {
        // Freiwillig heißt nicht beliebig: was hier steht, geht unverändert ins
        // Zertifikat und in den QR-Code.
        var profile = new NetworkProfile(
            NetworkKind.Tailscale, "https://pc.tailnet-1234.ts.net", Coordinator.Default);

        Assert.Contains("https://", profile.Rejection);
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
        // Sie führt keinen Modus. Ein Update darf ihn nicht stumm wechseln — der
        // Rechner wäre danach über Tailscale nicht mehr erreichbar und niemand
        // wüsste warum.
        var alt = NetworkConfig.Read($$"""{ "coordinator": "{{Coordinator.TailscaleAddress}}" }""");

        Assert.Equal(NetworkKind.Tailscale, alt.Kind);

        // Steht dort ein eigener Koordinator, war es von jeher Headscale — nur
        // hieß es damals nicht so.
        var eigener = NetworkConfig.Read("""{ "coordinator": "https://headscale.example.org" }""");

        Assert.Equal(NetworkKind.Headscale, eigener.Kind);
        Assert.Equal("https://headscale.example.org", eigener.Coordinator.Address);
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

/// <summary>
/// Dieselbe Schrittliste, aber je nach Netzmodus eine andere.
/// </summary>
public class SetupStepsProfileTests
{
    private sealed record Probe(
        bool HasTailscale = false,
        bool IsConnected = false,
        string TailnetName = "",
        bool HasCertificate = false,
        bool HasService = false) : ISetupProbe;

    private static NetworkProfile Heimnetz(string address = "192.168.178.20") =>
        new(NetworkKind.Lan, address, Coordinator.Default);

    [Fact]
    public void Im_Heimnetz_kommt_Tailscale_nicht_vor()
    {
        // Der Kern des Befunds: wer den Rechner nur aus dem eigenen WLAN
        // steuert, lief bisher durch zwei Schritte für ein Programm, das er nie
        // braucht — und blieb am dritten hängen.
        var steps = SetupSteps.For(Selection.Default, new Probe(), Heimnetz());

        Assert.All(steps, step =>
        {
            Assert.DoesNotContain("Tailscale", step.Title);
            Assert.DoesNotContain("Tailscale", step.Explanation);
        });
    }

    [Fact]
    public void Ohne_Tailscale_gibt_es_auch_nichts_abzuholen()
    {
        // Das Zertifikat stellt sich der Agent selbst aus. Ein Schritt „holen"
        // zeigte auf ein Programm, das gar nicht installiert ist.
        var steps = SetupSteps.For(Selection.Default, new Probe(), Heimnetz());

        Assert.DoesNotContain(steps, step => step.Title == SetupSteps.CertificateStep);
        Assert.Contains(steps, step => step.Title == "Agent einrichten");
    }

    [Fact]
    public void Eine_eingetragene_Adresse_hakt_den_ersten_Schritt_ab()
    {
        var offen = SetupSteps.For(Selection.Default, new Probe(), Heimnetz(""));
        var fertig = SetupSteps.For(Selection.Default, new Probe(), Heimnetz());

        Assert.Equal(SetupSteps.AddressStep, SetupSteps.Next(offen)?.Title);
        Assert.Equal("Agent einrichten", SetupSteps.Next(fertig)?.Title);
    }

    [Fact]
    public void Im_Heimnetz_ist_es_bereit_sobald_der_Dienst_steht()
    {
        var steps = SetupSteps.For(
            Selection.Default, new Probe(HasService: true), Heimnetz());

        Assert.True(SetupSteps.Ready(steps));
        Assert.Equal("Handy koppeln", SetupSteps.Next(steps)?.Title);
    }

    [Fact]
    public void Beim_eigenen_VPN_wird_auf_die_Anleitung_verwiesen()
    {
        // RemoteDesktop richtet fremde VPN nicht ein. Dann muss wenigstens
        // dastehen, wo es erklärt ist.
        var steps = SetupSteps.For(
            Selection.Default,
            new Probe(),
            new NetworkProfile(NetworkKind.Vpn, "", Coordinator.Default));

        Assert.Contains("Anleitung", steps[0].Explanation);
    }

    [Fact]
    public void Ohne_Profil_bleibt_alles_wie_bisher()
    {
        // Bestehende Installationen laufen über Tailscale. Ein Update darf ihre
        // Einrichtung nicht umschreiben.
        Assert.Equal(
            SetupSteps.For(Selection.Default, new Probe()).Select(step => step.Title),
            SetupSteps.For(Selection.Default, new Probe(), NetworkProfile.Default)
                .Select(step => step.Title));
    }

    [Fact]
    public void Auch_die_neuen_Schritte_kommen_ohne_Fachwort_aus()
    {
        var verboten = new[] { "Tailnet", "MagicDNS", "Zertifikatskette", "Endpoint", "Scope", "SAN" };

        foreach (var profil in new[]
                 {
                     Heimnetz(""), new NetworkProfile(NetworkKind.Vpn, "", Coordinator.Default)
                 })
        {
            foreach (var step in SetupSteps.For(Selection.Default, new Probe(), profil))
            {
                Assert.NotEmpty(step.Explanation);
                Assert.All(verboten, wort => Assert.DoesNotContain(wort, step.Explanation));
            }
        }
    }
}

/// <summary>
/// Alle Teile auf einen Blick — auch die, die es hier nicht gibt.
///
/// Der Befund dahinter: bis Release v1.0.0 wurde ausgeblendet, was fehlte.
/// Genau das nahm dem Nutzer den Weg, es nachzuholen.
/// </summary>
public class InventoryTests
{
    private static readonly Machine Vollstaendig = new(
        AgentBinary: true, AgentService: true, AgentRunning: true,
        ClientFiles: true, WebView2: true,
        Tailscale: true, TailscaleConnected: true, Certificate: true);

    [Fact]
    public void Ein_Zertifikat_nach_dem_Start_des_Agents_ist_noch_keins()
    {
        // Der Agent liest es genau einmal, beim Start. Ohne diesen Hinweis sieht
        // im Fenster alles fertig aus, und auf dem Handy kommt weiter die
        // Rückfrage nach einem selbst ausgestellten Zertifikat.
        var teil = Teil(
            new Machine(
                Tailscale: true, TailscaleConnected: true,
                Certificate: true, CertificateOutdated: true),
            Inventory.NetworkTitle);

        Assert.False(teil.Ok);
        Assert.Contains("noch mit dem alten", teil.State);
        Assert.Contains("beenden und starten", teil.Purpose);
    }

    [Fact]
    public void Ein_Zertifikat_von_vor_dem_Start_ist_in_Ordnung()
    {
        var teil = Teil(
            new Machine(Tailscale: true, TailscaleConnected: true, Certificate: true),
            Inventory.NetworkTitle);

        Assert.True(teil.Ok);
        Assert.Equal("verbunden", teil.State);
    }

    private static Part Teil(Machine machine, string titel, NetworkProfile? profil = null) =>
        Inventory.For(machine, profil ?? NetworkProfile.Default)
            .Single(part => part.Title == titel);

    [Fact]
    public void Auch_ein_nicht_eingerichteter_Agent_steht_da()
    {
        var agent = Teil(Vollstaendig with { AgentService = false, AgentRunning = false },
            Inventory.AgentTitle);

        Assert.True(agent.Missing);
        Assert.Contains("nicht eingerichtet", agent.State);

        // Und zwar mit dem Knopf, der ihn einrichtet. Ohne den wäre die Anzeige
        // nur eine Auskunft über etwas, das man anderswo erledigen muss.
        Assert.Contains(PartAction.Install, agent.Actions);
    }

    [Fact]
    public void Ohne_Programmdatei_gibt_es_nichts_einzurichten()
    {
        // Ein Knopf „Einrichten" liefe hier ins Leere: `sc create` zeigte auf
        // eine Datei, die es nicht gibt, und der Dienst startete nie.
        var agent = Teil(new Machine(), Inventory.AgentTitle);

        Assert.Empty(agent.Actions);
        Assert.Contains("fehlt", agent.State);
    }

    [Fact]
    public void Ein_laufender_Agent_laesst_sich_beenden_und_ein_gestoppter_starten()
    {
        Assert.Contains(PartAction.Stop, Teil(Vollstaendig, Inventory.AgentTitle).Actions);
        Assert.DoesNotContain(PartAction.Start, Teil(Vollstaendig, Inventory.AgentTitle).Actions);

        var gestoppt = Teil(Vollstaendig with { AgentRunning = false }, Inventory.AgentTitle);

        Assert.Contains(PartAction.Start, gestoppt.Actions);
        Assert.DoesNotContain(PartAction.Stop, gestoppt.Actions);
        Assert.False(gestoppt.Ok);
    }

    [Fact]
    public void Entfernen_gibt_es_nur_wo_etwas_eingetragen_ist()
    {
        Assert.Contains(PartAction.Remove, Teil(Vollstaendig, Inventory.AgentTitle).Actions);
        Assert.DoesNotContain(
            PartAction.Remove,
            Teil(Vollstaendig with { AgentService = false }, Inventory.AgentTitle).Actions);
    }

    [Fact]
    public void Ohne_WebView2_wird_das_Fenster_nicht_angeboten()
    {
        // Es ginge auf und bliebe leer — das sähe aus wie ein Absturz.
        var client = Teil(Vollstaendig with { WebView2 = false }, Inventory.ClientTitle);

        Assert.Empty(client.Actions);
        Assert.Contains("WebView2", client.State);
    }

    [Fact]
    public void Die_Fernsteuerung_kennt_kein_Starten_und_kein_Beenden()
    {
        // Sie ist kein Dienst. Knöpfe dafür wären eine Verwechslung mit dem
        // Agent, und die kostete beim ersten Fehlgriff die eigene Sitzung.
        var client = Teil(Vollstaendig, Inventory.ClientTitle);

        Assert.Equal([PartAction.Open], client.Actions);
    }

    [Fact]
    public void Im_Heimnetz_gibt_es_am_Netz_nichts_zu_installieren()
    {
        var netz = Teil(
            new Machine(AgentBinary: true, AgentService: true, ClientFiles: true, WebView2: true),
            Inventory.NetworkTitle,
            new NetworkProfile(NetworkKind.Lan, "192.168.178.20", Coordinator.Default));

        Assert.Empty(netz.Actions);
        Assert.True(netz.Ok);
        Assert.Contains("192.168.178.20", netz.State);
    }

    [Fact]
    public void Im_Heimnetz_ohne_Adresse_fehlt_noch_etwas()
    {
        var netz = Teil(
            Vollstaendig,
            Inventory.NetworkTitle,
            new NetworkProfile(NetworkKind.Lan, "", Coordinator.Default));

        Assert.False(netz.Ok);
    }

    [Fact]
    public void Ein_fehlendes_Tailscale_Zertifikat_ist_kein_Fehler_mehr()
    {
        // Der Agent stellt sich sonst selbst eins aus. Angeboten wird es
        // trotzdem: ein Zertifikat von Tailscale kennt jeder Browser bereits.
        var netz = Teil(Vollstaendig with { Certificate = false }, Inventory.NetworkTitle);

        Assert.True(netz.Ok);
        Assert.Contains(PartAction.Certificate, netz.Actions);
    }

    [Fact]
    public void Ohne_Tailscale_fuehrt_der_Weg_zum_Herunterladen()
    {
        var netz = Teil(new Machine(), Inventory.NetworkTitle);

        Assert.Equal([PartAction.Download], netz.Actions);
        Assert.True(netz.Missing);
    }

    [Fact]
    public void Es_sind_immer_drei_Teile_egal_was_fehlt()
    {
        Assert.Equal(3, Inventory.For(new Machine(), NetworkProfile.Default).Count);
        Assert.Equal(3, Inventory.For(Vollstaendig, NetworkProfile.Default).Count);
    }

    [Fact]
    public void Jeder_Handgriff_hat_einen_Satz_statt_eines_Namens()
    {
        foreach (var action in Enum.GetValues<PartAction>())
        {
            Assert.NotEmpty(Inventory.Describe(action));
            Assert.DoesNotContain("Action", Inventory.Describe(action));
        }
    }

    [Fact]
    public void Jedes_Teil_sagt_wofuer_es_da_ist()
    {
        foreach (var part in Inventory.For(new Machine(), NetworkProfile.Default))
        {
            Assert.NotEmpty(part.Purpose);
            Assert.NotEmpty(part.State);
        }
    }
}

/// <summary>
/// Wo die Daten liegen. Ein Ordner statt zweier — und ein Weg von dort, wo sie
/// bis v1.2.0 lagen, hierher.
/// </summary>
public class AgentPathsTests
{
    [Fact]
    public void Der_Datenordner_liegt_neben_dem_Programm()
    {
        Assert.Equal(
            Path.Combine("C:", "Program Files", "RemoteDesktop", "data"),
            AgentPaths.For(Path.Combine("C:", "Program Files", "RemoteDesktop")));
    }

    [Fact]
    public void Beide_alten_Orte_werden_beruecksichtigt()
    {
        var moves = AgentPaths.Moves("/app", "/legacy");

        // Der Schlüssel lag neben der .exe, das Zertifikat in ProgramData.
        Assert.Contains(moves, move => move.From == Path.Combine("/app", "agentkey.txt"));
        Assert.Contains(moves, move => move.From == Path.Combine("/legacy", "cert.crt"));
        Assert.All(moves, move => Assert.StartsWith(Path.Combine("/app", "data"), move.To));
    }

    [Fact]
    public void Umgezogen_wird_nur_was_hier_noch_fehlt()
    {
        // Arrange — eine alte Installation mit Schlüssel und Kopplungen.
        var root = Directory.CreateTempSubdirectory().FullName;
        var app = Path.Combine(root, "app");
        var legacy = Path.Combine(root, "legacy");

        Directory.CreateDirectory(app);
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(AgentPaths.For(app));

        File.WriteAllText(Path.Combine(app, "agentkey.txt"), "alter Schlüssel");
        File.WriteAllText(Path.Combine(legacy, "cert.crt"), "altes Zertifikat");
        File.WriteAllText(Path.Combine(AgentPaths.For(app), "clients.json"), "die neuen");
        File.WriteAllText(Path.Combine(legacy, "clients.json"), "die alten");

        // Act
        var adopted = AgentPaths.Adopt(app, legacy);

        // Assert
        Assert.Equal("alter Schlüssel", File.ReadAllText(Path.Combine(AgentPaths.For(app), "agentkey.txt")));
        Assert.Equal("altes Zertifikat", File.ReadAllText(Path.Combine(AgentPaths.For(app), "cert.crt")));

        // Was hier schon steht, gehört zur laufenden Installation und bleibt.
        Assert.Equal("die neuen", File.ReadAllText(Path.Combine(AgentPaths.For(app), "clients.json")));

        // Verschoben, nicht kopiert: sonst gäbe es weiter zwei Orte.
        Assert.False(File.Exists(Path.Combine(app, "agentkey.txt")));
        Assert.Contains("agentkey.txt", adopted);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Ein_Pfad_aus_einer_alten_Konfiguration_wird_umgebogen()
    {
        // Die appsettings.json überlebt ein Update — der Pfad darin nicht.
        Assert.Equal(
            Path.Combine("/app/data", "cert.crt"),
            AgentPaths.Redirect(Path.Combine("/legacy", "cert.crt"), "/app/data", "/legacy"));
    }

    [Fact]
    public void Ein_selbst_eingetragener_Pfad_bleibt_stehen()
    {
        // Wer etwas Eigenes einträgt, meint es auch so.
        Assert.Equal(
            "/eigenes/cert.crt",
            AgentPaths.Redirect("/eigenes/cert.crt", "/app/data", "/legacy"));

        Assert.Null(AgentPaths.Redirect(null, "/app/data", "/legacy"));
    }
}

/// <summary>
/// Der Abschluss der Einrichtung — ein Auftrag, eine Rückfrage von Windows.
/// </summary>
public class SetupRequestTests
{
    [Fact]
    public void Ein_Auftrag_uebersteht_den_Weg_ueber_die_Datei()
    {
        // Arrange
        var request = new SetupRequest(
            new NetworkProfile(NetworkKind.Lan, "192.168.178.33", Coordinator.Default),
            AgentSetup.Automatic);

        // Act
        var read = SetupRequest.Read(request.Write());

        // Assert
        Assert.NotNull(read);
        Assert.Equal(AgentSetup.Automatic, read.Agent);
        Assert.Equal(NetworkKind.Lan, read.Profile.Kind);
        Assert.Equal("192.168.178.33", read.Profile.Address);
    }

    [Fact]
    public void Unlesbares_richtet_nichts_ein()
    {
        // Eine halb verstandene Einrichtung wäre schlimmer als keine — sie
        // richtete etwas ein, das niemand so gewählt hat.
        Assert.Null(SetupRequest.Read("{"));
        Assert.Null(SetupRequest.Read(""));
        Assert.Null(SetupRequest.Read(null));
        Assert.Null(SetupRequest.Read("{\"agent\":\"Automatic\"}"));
    }

    [Fact]
    public void Ein_unbekannter_Agent_Wunsch_richtet_keinen_ein()
    {
        // Im Zweifel nichts eintragen: ein Dienst, den niemand bestellt hat,
        // ist schlimmer als ein fehlender.
        var read = SetupRequest.Read("{\"network\":\"{}\",\"agent\":\"vielleicht\"}");

        Assert.NotNull(read);
        Assert.Equal(AgentSetup.None, read.Agent);
    }
}

/// <summary>
/// Der Agent als geplante Aufgabe. Sie ist der Grund, warum Bild und Eingabe
/// überhaupt funktionieren — ein Dienst sieht in Sitzung 0 keinen Bildschirm.
/// </summary>
public class AgentTaskTests
{
    private const string Exe = @"C:\Program Files\RemoteDesktop\RemoteDesktopAgent.exe";

    [Fact]
    public void Die_Aufgabe_laeuft_in_der_Sitzung_des_Benutzers()
    {
        var xml = AgentTask.Definition(Exe, @"PC\David", atLogon: true);

        // InteractiveToken heißt: in der Sitzung dessen, der angemeldet ist.
        // Genau das fehlt einem Dienst, und deshalb ist er weg.
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml);
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml);
        Assert.Contains(@"<UserId>PC\David</UserId>", xml);
        Assert.Contains($"<Command>{Exe}</Command>", xml);
    }

    [Fact]
    public void Ohne_Autostart_gibt_es_keinen_Ausloeser()
    {
        // „Nicht automatisch starten" heißt nicht „weg damit": die Aufgabe
        // bleibt bestehen und lässt sich von Hand starten.
        var xml = AgentTask.Definition(Exe, @"PC\David", atLogon: false);

        Assert.DoesNotContain("<LogonTrigger>", xml);
        Assert.Contains("<AllowStartOnDemand>true</AllowStartOnDemand>", xml);
    }

    [Fact]
    public void Kein_Zeitlimit_und_kein_Anhalten_auf_Akku()
    {
        // Ohne beides beendet Windows die Aufgabe irgendwann von selbst — und
        // der Rechner wäre unerreichbar, ohne dass jemand den Grund fände.
        var xml = AgentTask.Definition(Exe, @"PC\David", atLogon: true);

        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml);
        Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", xml);
    }

    [Fact]
    public void Sonderzeichen_im_Pfad_zerlegen_die_Beschreibung_nicht()
    {
        var xml = AgentTask.Definition(@"C:\Ordner & Co\agent.exe", "PC\\Ann&Bob", atLogon: true);

        Assert.Contains("Ordner &amp; Co", xml);
        Assert.Contains("Ann&amp;Bob", xml);
    }

    [Fact]
    public void Der_Ausloeser_wird_wiedererkannt()
    {
        Assert.True(AgentTask.StartsAtLogon(AgentTask.Definition(Exe, "PC\\D", atLogon: true)));
        Assert.False(AgentTask.StartsAtLogon(AgentTask.Definition(Exe, "PC\\D", atLogon: false)));
        Assert.False(AgentTask.StartsAtLogon(null));
    }

    [Theory]
    [InlineData(true, @"PC\David")]
    [InlineData(false, @"PC\David")]
    [InlineData(true, "")]
    public void Startart_und_Benutzer_ueberstehen_den_erhoehten_Aufruf(bool atLogon, string user)
    {
        var (readLogon, readUser) = AgentTask.ReadArgument(AgentTask.Argument(atLogon, user));

        Assert.Equal(atLogon, readLogon);
        Assert.Equal(user, readUser);
    }
}
