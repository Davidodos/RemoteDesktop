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
