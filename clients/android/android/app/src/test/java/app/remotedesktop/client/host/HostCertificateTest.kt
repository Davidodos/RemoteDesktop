package app.remotedesktop.client.host

import java.io.File
import java.nio.file.Files
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Das Zertifikat wird von Hand kodiert (siehe [Der]). Entweder es ist richtig,
 * oder gar nichts läuft — deshalb wird hier nicht der Kodierer geprüft, sondern
 * das Ergebnis: liest Java es, hängt die Kette zusammen, stehen die Namen drin?
 */
class HostCertificateTest {

    private fun directory(): File = Files.createTempDirectory("host-cert").toFile()

    @Test
    fun `stellt eine lesbare Kette aus`() {
        val material = HostCertificate.loadOrCreate(
            directory(), "Pixel", listOf("pixel.example.ts.net", "192.168.178.31"),
        )

        val authority = parse(material.authorityDer)
        val server = material.keyStore.getCertificate(material.alias) as X509Certificate

        // Das Serverzertifikat muss sich mit dem Schlüssel der CA prüfen
        // lassen. Wäre die Unterschrift falsch kodiert, scheiterte hier alles.
        server.verify(authority.publicKey)
        authority.verify(authority.publicKey)

        assertTrue("CA muss eine CA sein", authority.basicConstraints >= 0)
        assertEquals("Server darf keine CA sein", -1, server.basicConstraints)
    }

    @Test
    fun `traegt Name und Adresse als Alternativnamen`() {
        val material = HostCertificate.loadOrCreate(
            directory(), "Pixel", listOf("pixel.example.ts.net", "192.168.178.31"),
        )

        val server = material.keyStore.getCertificate(material.alias) as X509Certificate
        val names = server.subjectAlternativeNames.orEmpty().map { it[1] as String }

        assertTrue(names.contains("pixel.example.ts.net"))
        assertTrue(names.contains("192.168.178.31"))
    }

    @Test
    fun `der Fingerabdruck ist sha256 ueber die CA, klein und ohne Trenner`() {
        val material = HostCertificate.loadOrCreate(directory(), "Pixel", listOf("pixel"))

        assertEquals(64, material.fingerprint.length)
        assertEquals(material.fingerprint.lowercase(), material.fingerprint)
        assertEquals(
            HostCertificate.fingerprintOf(parse(material.authorityDer).encoded),
            material.fingerprint,
        )
    }

    @Test
    fun `beim zweiten Start bleibt dieselbe CA stehen`() {
        val folder = directory()

        val first = HostCertificate.loadOrCreate(folder, "Pixel", listOf("pixel"))
        val second = HostCertificate.loadOrCreate(folder, "Pixel", listOf("pixel"))

        // Eine neue CA hieße: jeder gekoppelte Client muss sie erneut
        // bestätigen. Genau das darf ein Neustart nicht auslösen.
        assertEquals(first.fingerprint, second.fingerprint)
    }

    @Test
    fun `eine neue Adresse ergibt ein neues Serverzertifikat, aber dieselbe CA`() {
        val folder = directory()

        val first = HostCertificate.loadOrCreate(folder, "Pixel", listOf("192.168.178.31"))
        val second = HostCertificate.loadOrCreate(folder, "Pixel", listOf("192.168.178.44"))

        assertEquals(first.fingerprint, second.fingerprint)

        val serverNames = (second.keyStore.getCertificate(second.alias) as X509Certificate)
            .subjectAlternativeNames.orEmpty().map { it[1] as String }

        assertTrue(serverNames.contains("192.168.178.44"))
        assertNotEquals(
            (first.keyStore.getCertificate(first.alias) as X509Certificate).serialNumber,
            (second.keyStore.getCertificate(second.alias) as X509Certificate).serialNumber,
        )
    }

    @Test
    fun `ein kaputter Speicher fuehrt zu einem neuen, nicht zu einem Absturz`() {
        val folder = directory()

        HostCertificate.loadOrCreate(folder, "Pixel", listOf("pixel"))
        File(folder, "host-keystore.p12").writeText("kaputt")

        val material = HostCertificate.loadOrCreate(folder, "Pixel", listOf("pixel"))

        assertEquals(64, material.fingerprint.length)
    }

    private fun parse(der: ByteArray): X509Certificate =
        CertificateFactory.getInstance("X.509")
            .generateCertificate(der.inputStream()) as X509Certificate
}
