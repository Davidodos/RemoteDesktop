using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktopAgent.Services;

/// <summary>
/// Das Zertifikat, das Kestrel gerade vorzeigt — austauschbar im Lauf.
///
/// <para>
/// Kestrel fragt bei jedem Handshake über <c>ServerCertificateSelector</c>
/// nach; was hier steht, gilt ab der nächsten Verbindung. Vorher war das
/// Zertifikat beim Start festgenagelt: ein Tailscale-Zertifikat lief nach 90
/// Tagen ab, ein selbst ausgestelltes lautete nach einem Netzwechsel auf die
/// falsche Adresse, und beides hielt bis zum nächsten Neustart (Durchsicht
/// C1, C2). Getauscht wird von <see cref="CertificateRenewal"/>.
/// </para>
/// </summary>
public sealed class ServingCertificate(X509Certificate2 initial, bool selfIssued)
{
    private X509Certificate2 _current = initial;

    /// <summary>Ob der Agent sich selbst beglaubigt — entschieden beim Start.</summary>
    public bool SelfIssued { get; } = selfIssued;

    public X509Certificate2 Current => Volatile.Read(ref _current);

    /// <summary>
    /// Tauscht aus. Das alte bleibt ungelöscht liegen: ein Handshake, der es
    /// gerade benutzt, soll nicht mitten im Aufbau ins Leere greifen — und
    /// ein Tausch kommt seltener als einmal im Monat.
    /// </summary>
    public void Swap(X509Certificate2 next) => Interlocked.Exchange(ref _current, next);
}
