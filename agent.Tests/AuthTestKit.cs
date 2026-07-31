using System.Security.Cryptography;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Eine Uhr, die sich stellen lässt. Ohne sie ließen sich Ablaufzeiten nur
/// prüfen, indem der Test fünf Minuten wartet.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}

/// <summary>
/// Ein Client mit eigenem Schlüsselpaar — im Test das, was im Betrieb das Handy
/// ist. Unterschrieben wird im Format der WebCrypto-API des Browsers (r und s
/// hintereinander), damit der Test dieselbe Prüfung durchläuft wie die App.
/// </summary>
public sealed class TestClient : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public string Sign(string nonceBase64) => Convert.ToBase64String(_key.SignData(
        Convert.FromBase64String(nonceBase64),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    public void Dispose() => _key.Dispose();
}
