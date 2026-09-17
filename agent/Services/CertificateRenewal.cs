using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Hält das vorgezeigte Zertifikat gültig, ohne Neustart.
///
/// <para>
/// Zwei Anlässe, ein Handgriff: **einmal am Tag** wird nachgesehen, ob das
/// Zertifikat bald abläuft — bei Tailscale nach 90 Tagen, und das erneuert
/// sonst niemand (Durchsicht C1). Und **bei jedem Netzwechsel** wird das
/// selbst ausgestellte auf die neuen Adressen ausgestellt: ein Laptop, der das
/// WLAN wechselt, hätte sonst eine Adresse, die nicht im Zertifikat steht,
/// bis zum nächsten Start (C2). Die Stelle darüber bleibt, die Clients merken
/// nichts.
/// </para>
/// </summary>
public sealed class CertificateRenewal(
    ServingCertificate serving,
    CertificateVault vault,
    CertificateRenewal.Source source,
    ILogger<CertificateRenewal> logger) : BackgroundService
{
    /// <summary>Woher ein neues Zertifikat käme.</summary>
    /// <param name="TailscaleName">Der Name für <c>tailscale cert</c> — <c>null</c> ohne Tailscale.</param>
    /// <param name="CertificatePath">Wohin Tailscale das Zertifikat legt.</param>
    /// <param name="KeyPath">Und den Schlüssel.</param>
    /// <param name="MachineName">Für die eigene Stelle, falls sie neu entstehen muss.</param>
    /// <param name="Names">Die Namen, auf die das eigene lauten muss — jetzt, nicht beim Start.</param>
    public sealed record Source(
        string? TailscaleName,
        string CertificatePath,
        string KeyPath,
        string MachineName,
        Func<IReadOnlyList<string>> Names);

    public static readonly TimeSpan Daily = TimeSpan.FromHours(24);

    /// <summary>
    /// Nach einem Netzwechsel kurz warten: Windows meldet einen Wechsel in
    /// mehreren Schüben, und die neue Adresse steht erst am Ende fest.
    /// </summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkAddressChangedEventHandler onChange = (_, _) => Trigger(stoppingToken);

        // Nur das selbst ausgestellte hängt an den Adressen; ein Zertifikat
        // von Tailscale lautet auf einen Namen, der bleibt.
        if (serving.SelfIssued)
        {
            NetworkChange.NetworkAddressChanged += onChange;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(Daily, stoppingToken);
                await RenewAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Der Agent geht aus.
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= onChange;
        }
    }

    private void Trigger(CancellationToken stoppingToken)
    {
        _pending?.Cancel();

        var mine = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _pending = mine;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SettleDelay, mine.Token);
                await RenewAsync(mine.Token);
            }
            catch (OperationCanceledException)
            {
                // Ein neuerer Wechsel hat diesen abgelöst.
            }
        }, CancellationToken.None);
    }

    /// <summary>Sieht nach und tauscht, falls nötig. Öffentlich für den Test.</summary>
    public async Task RenewAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (serving.SelfIssued)
            {
                RenewSelfIssued();
            }
            else
            {
                await RenewTailscaleAsync(cancellationToken);
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Ein gescheiterter Tausch darf den laufenden Agent nicht
            // beeinträchtigen — im Zweifel bleibt eben das alte Zertifikat.
            logger.LogWarning(failure, "Das Zertifikat ließ sich nicht erneuern.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RenewSelfIssued()
    {
        var names = source.Names();
        var authority = vault.Authority(source.MachineName, names);
        var server = vault.Server(authority, names);

        if (server.Thumbprint == serving.Current.Thumbprint)
        {
            return;
        }

        serving.Swap(server);
        logger.LogInformation(
            "Zertifikat neu ausgestellt auf {Names}.", string.Join(", ", names));
    }

    private async Task RenewTailscaleAsync(CancellationToken cancellationToken)
    {
        var current = serving.Current;
        var now = DateTimeOffset.UtcNow;

        if (current.NotAfter - now > SelfSignedCertificate.RenewBefore)
        {
            return;
        }

        var name = source.TailscaleName ?? CertificateLoader.DnsName(current);

        if (name is null)
        {
            logger.LogWarning("Das Zertifikat läuft ab, aber es steht kein Name für tailscale cert fest.");
            return;
        }

        logger.LogInformation(
            "Das Zertifikat von Tailscale läuft am {NotAfter:d} ab — erneuere es.", current.NotAfter);

        var failure = await TailscaleCert.RenewAsync(
            name, source.CertificatePath, source.KeyPath, cancellationToken);

        if (failure is not null)
        {
            logger.LogWarning("tailscale cert ist gescheitert: {Reason}", failure);
            return;
        }

        var renewed = CertificateLoader.Load(source.CertificatePath, source.KeyPath);

        if (renewed.NotAfter <= current.NotAfter)
        {
            logger.LogWarning("tailscale cert hat kein neueres Zertifikat geliefert.");
            renewed.Dispose();
            return;
        }

        serving.Swap(renewed);
        logger.LogInformation("Zertifikat von Tailscale erneuert, gültig bis {NotAfter:d}.", renewed.NotAfter);
    }
}
