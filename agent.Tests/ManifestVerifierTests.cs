using System.Security.Cryptography;
using System.Text;
using RemoteDesktopAgent.Services;
using Xunit;

namespace RemoteDesktopAgent.Tests;

/// <summary>
/// Die Signatur unter dem Release-Manifest ist das, was ein übernommenes
/// GitHub-Konto von einem übernommenen PC trennt. Wer die Datei austauschen
/// kann, kann auch die Prüfsumme daneben austauschen — die Unterschrift nicht,
/// solange der private Schlüssel woanders liegt.
/// </summary>
public class ManifestVerifierTests : IDisposable
{
    private const string Manifest =
        """
        {"version":"1.2.0","protocol":1,"file":"RemoteDesktopAgent.exe","size":81920,
         "sha256":"AB12CD34"}
        """;

    private readonly ECDsa _releaseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private ManifestVerifier Verifier =>
        new(Convert.ToBase64String(_releaseKey.ExportSubjectPublicKeyInfo()));

    private string Sign(byte[] data) => Convert.ToBase64String(_releaseKey.SignData(
        data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public void Dispose() => _releaseKey.Dispose();

    [Fact]
    public void Ein_richtig_unterschriebenes_Manifest_wird_gelesen()
    {
        var manifest = Verifier.Verify(Bytes(Manifest), Sign(Bytes(Manifest)));

        Assert.NotNull(manifest);
        Assert.Equal("1.2.0", manifest!.Version);
        Assert.Equal(1, manifest.Protocol);
        Assert.Equal("RemoteDesktopAgent.exe", manifest.File);
        Assert.Equal(81920, manifest.Size);
    }

    /// <summary>Die Prüfsumme wird kleingeschrieben — verglichen wird gegen Hex aus .NET.</summary>
    [Fact]
    public void Die_Pruefsumme_kommt_in_Kleinschreibung_heraus()
    {
        Assert.Equal("ab12cd34", Verifier.Verify(Bytes(Manifest), Sign(Bytes(Manifest)))!.Sha256);
    }

    /// <summary>
    /// Der Kernfall dieser Phase: ein Byte am Manifest geändert, die
    /// Unterschrift von vorher — das darf nicht durchgehen.
    /// </summary>
    [Fact]
    public void Ein_manipuliertes_Manifest_faellt_durch()
    {
        var signature = Sign(Bytes(Manifest));
        var manipuliert = Manifest.Replace("\"sha256\":\"AB12CD34\"", "\"sha256\":\"00000000\"");

        Assert.NotEqual(Manifest, manipuliert);
        Assert.Null(Verifier.Verify(Bytes(manipuliert), signature));
    }

    [Fact]
    public void Eine_Unterschrift_von_einem_fremden_Schluessel_faellt_durch()
    {
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var signature = Convert.ToBase64String(fremd.SignData(
            Bytes(Manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        Assert.Null(Verifier.Verify(Bytes(Manifest), signature));
    }

    [Fact]
    public void Ohne_Unterschrift_gibt_es_kein_Manifest()
    {
        Assert.Null(Verifier.Verify(Bytes(Manifest), null));
        Assert.Null(Verifier.Verify(Bytes(Manifest), "   "));
        Assert.Null(Verifier.Verify(Bytes(Manifest), "kein-base64!"));
    }

    /// <summary>
    /// Ohne einkompilierten Schlüssel wird nichts geprüft und damit auch nichts
    /// installiert. Das ist der Auslieferungszustand des Repos.
    /// </summary>
    [Fact]
    public void Ohne_einkompilierten_Schluessel_geht_gar_nichts()
    {
        var ohne = new ManifestVerifier(ReleaseKeys.PublicKey);

        Assert.False(ohne.IsConfigured);
        Assert.Null(ohne.Verify(Bytes(Manifest), Sign(Bytes(Manifest))));
    }

    /// <summary>
    /// Ein Manifest, das tatsächlich von <c>scripts/sign-manifest.mjs</c>
    /// stammt — dieselben Bytes, dieselbe Unterschrift.
    ///
    /// Das ist der Punkt, an dem sich zwei Laufzeiten treffen: Node
    /// unterschreibt, .NET prüft. Beide müssen dasselbe Signaturformat
    /// benutzen (r und s hintereinander, nicht DER). Ohne diesen Test fiele
    /// eine Abweichung erst beim ersten echten Release auf — und dort sähe sie
    /// aus wie ein Angriff.
    /// </summary>
    [Fact]
    public void Ein_Manifest_aus_dem_Signierskript_wird_angenommen()
    {
        const string vomSkript =
            "{\n  \"version\": \"1.2.0\",\n  \"protocol\": 1,\n" +
            "  \"file\": \"RemoteDesktopAgent.exe\",\n  \"size\": 200,\n" +
            "  \"sha256\": \"9e24f6a7327f5ac5ca173b6b8a087724a8fcd3909d6990a195f5c1a9ee65b4b4\"\n}";

        var verifier = new ManifestVerifier(
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgBOkQCMT+BnKWxCHAfFyQ37RWJmsSSdcha4a6N173" +
            "3vtJjzog3Gg7+EhKJxFz1iIJMq4d6eNPsUGEjBZTyFZRw==");

        var manifest = verifier.Verify(
            Bytes(vomSkript),
            "3bEhtPC/oXkjxc3DO9NQji/c7RzkMJ8DNTN9Y46nkpOTKBfWGQrJwWq1bJ98/+VkXmNwWet54Md3khBOowhzHg==");

        Assert.NotNull(manifest);
        Assert.Equal("1.2.0", manifest!.Version);
        Assert.Equal(200, manifest.Size);
    }

    /// <summary>
    /// Richtig unterschrieben, aber inhaltlich unbrauchbar: ohne Prüfsumme oder
    /// Dateinamen ließe sich nichts installieren, was man danach noch prüfen
    /// könnte.
    /// </summary>
    [Theory]
    [InlineData("""{"version":"1.0.0","protocol":1,"file":"a.exe","size":10}""")]
    [InlineData("""{"version":"1.0.0","protocol":1,"size":10,"sha256":"ab"}""")]
    [InlineData("""{"version":"1.0.0","protocol":1,"file":"a.exe","size":0,"sha256":"ab"}""")]
    [InlineData("kein JSON")]
    public void Ein_unvollstaendiges_Manifest_wird_verworfen(string json)
    {
        Assert.Null(Verifier.Verify(Bytes(json), Sign(Bytes(json))));
    }
}
