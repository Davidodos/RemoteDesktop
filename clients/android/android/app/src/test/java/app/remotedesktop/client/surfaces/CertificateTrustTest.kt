package app.remotedesktop.client.surfaces

import java.util.Base64
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Die zweite Prüfung des Zertifikats — die erste steht in der Weboberfläche
 * (`app/src/lib/certificateTrust.ts`).
 *
 * Zwei Prüfungen derselben Sache sind hier kein Versehen: die Weboberfläche ist
 * austauschbar, und was über das Vertrauen des ganzen Geräts entscheidet, soll
 * nicht allein an ihr hängen.
 */
class CertificateTrustTest {

    private val ca: ByteArray = Base64.getMimeDecoder().decode(SelfSignedCa)

    @Test
    fun `ein passender Fingerabdruck macht es bereit`() {
        val abdruck = CertificateTrust.fingerprint(ca)

        val outcome = CertificateTrust.inspect(ca, abdruck)

        assertTrue(outcome.toString(), outcome is CertificateTrust.Outcome.Ready)
        assertEquals(abdruck, (outcome as CertificateTrust.Outcome.Ready).fingerprint)
    }

    @Test
    fun `ein fremder Fingerabdruck wird abgelehnt`() {
        // Der eine Fall, der wirklich zählt: jemand im Netz schiebt sein eigenes
        // Zertifikat unter.
        assertTrue(CertificateTrust.inspect(ca, "a".repeat(64)) is CertificateTrust.Outcome.Rejected)
    }

    @Test
    fun `ohne Fingerabdruck wird nichts bestaetigt`() {
        // Ein Zertifikat ohne Vergleichswert anzunehmen wäre dasselbe wie nicht
        // zu prüfen.
        assertTrue(CertificateTrust.inspect(ca, "") is CertificateTrust.Outcome.Rejected)
        assertTrue(CertificateTrust.inspect(ca, "kein-hex") is CertificateTrust.Outcome.Rejected)
        assertTrue(CertificateTrust.inspect(ca, "ab") is CertificateTrust.Outcome.Rejected)
    }

    @Test
    fun `Grossschreibung des Fingerabdrucks ist egal`() {
        val abdruck = CertificateTrust.fingerprint(ca).uppercase()

        assertTrue(CertificateTrust.inspect(ca, abdruck) is CertificateTrust.Outcome.Ready)
    }

    @Test
    fun `was kein Zertifikat ist wird abgelehnt`() {
        // Der Fingerabdruck stimmt hier sogar — nur ist die Datei keins. Ohne
        // diese Prüfung ginge ein Systemdialog auf, der wortlos nichts tut.
        val unsinn = byteArrayOf(1, 2, 3, 4)

        assertTrue(
            CertificateTrust.inspect(unsinn, CertificateTrust.fingerprint(unsinn))
                is CertificateTrust.Outcome.Rejected
        )
    }

    @Test
    fun `eine leere Datei ist kein Zertifikat`() {
        assertTrue(
            CertificateTrust.inspect(ByteArray(0), "a".repeat(64))
                is CertificateTrust.Outcome.Rejected
        )
    }

    @Test
    fun `der Fingerabdruck ist kleingeschrieben und 64 Zeichen lang`() {
        val abdruck = CertificateTrust.fingerprint(ca)

        assertEquals(64, abdruck.length)
        assertEquals(abdruck.lowercase(), abdruck)

        // Derselbe Wert, den der Agent meldet und die App vergleicht.
        assertEquals("977bd0f26b0ce1de96321a31010b10a35ce9e77327914f32fe8860d9f4a3e469", abdruck)
    }

    private companion object {
        /**
         * Eine echte, selbst ausgestellte Zertifizierungsstelle (RSA-2048,
         * `CA:true`, `pathlen:0`) — dieselbe Bauart, die
         * `SelfSignedCertificate.CreateAuthority` im Agent erzeugt. Sie steht
         * fest im Test, damit er ohne Zufall und ohne Schlüsselerzeugung
         * auskommt: geprüft wird das Lesen, nicht das Erzeugen.
         */
        const val SelfSignedCa =
            "MIIDNDCCAhygAwIBAgIUNpIPJ1yf2Fv/lRoEQ274BGQcmOUwDQYJKoZIhvcNAQEL" +
            "BQAwIDEeMBwGA1UEAwwVUmVtb3RlRGVza3RvcCBUZXN0IENBMB4XDTI2MDgwNTEw" +
            "NTA0NloXDTM2MDgwMjEwNTA0NlowIDEeMBwGA1UEAwwVUmVtb3RlRGVza3RvcCBU" +
            "ZXN0IENBMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAnYltOmaFKSJi" +
            "/3ycUp/ehx6mGrj6a2pbDW8/CECBxBFtAcXolhpFQgHyhMs+nB5fxZjVP5n+n6mz" +
            "u4JWVa7uV6Y/17BQ+MrxnFTb3Bz9ZhaUMJ/POmWy5eg3ZhbCiC85Jh4lYncafEWT" +
            "75e8is1gi9NimT+Ss/SUdXFOrFl04mWyLFOWGLrlkai8/xkei5qh8wP9lNsJGPTy" +
            "G1iXVdNJ+XV7ntaQyvtxGrc+VdErquNr6141Qs84OzXCCJL4m/QIPgXhVhBHKhqi" +
            "D7hNBosef6qyleHR9Pgr+sD8hwjXAHA+h4LKCU1OpFAiHe966HseOqpHzBaWuAc1" +
            "Pp8F74Vu+wIDAQABo2YwZDAdBgNVHQ4EFgQUr789nEWIf3cmzUme6NjTs1zruMQw" +
            "HwYDVR0jBBgwFoAUr789nEWIf3cmzUme6NjTs1zruMQwEgYDVR0TAQH/BAgwBgEB" +
            "/wIBADAOBgNVHQ8BAf8EBAMCAQYwDQYJKoZIhvcNAQELBQADggEBAGS1los3WoTT" +
            "g2roVGT2xibBdL+tCzxC/u9iDZ21Unp5ZLCZ6QfTNPRpJTYyfXImIzwluhLI/E9y" +
            "LyZu975NuJBi0vefcWP5Pe4NimprTlhQESeXkWgy+57KgU4VwPZ+oUFo7jjoFnW1" +
            "yVubyGRUbEgHBMgH6ZLlFeG7zgC8FMKMovEinjaorbDd9IDvf3G4BGoc0/y/RpWP" +
            "QuW1dbMfBaDMfumQT/sj13EHHPk/jgNN6elOgNhDDlu9VQ9Zd774JZEyjz7oaIyC" +
            "sGVxsy0cmXUeTsecP0rHGGMJnfzj4m5p4nMtnH1fa6OTnbVPxlKdsHvLZpqAmPtk" +
            "wr/oLgOGBtQ="
    }
}
