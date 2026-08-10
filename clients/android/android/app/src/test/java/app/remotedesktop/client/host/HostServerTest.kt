package app.remotedesktop.client.host

import java.io.File
import java.math.BigInteger
import java.net.HttpURLConnection
import java.net.URL
import java.nio.file.Files
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.SecureRandom
import java.security.Signature
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.util.Base64
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory
import org.json.JSONObject
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * Der Host von außen — über eine echte TLS-Verbindung.
 *
 * Das ist der Test, auf den es ankommt. Er prüft nicht einzelne Bausteine,
 * sondern die Kette, an der am Gerät alles hängt: selbst kodiertes Zertifikat →
 * TLS-Handschlag → eigener HTTP-Parser → Zugangsprüfung → Kopplung →
 * Anmeldung → berechtigter Aufruf. Geht irgendwo davon etwas schief, sieht das
 * am Handy aus wie „antwortet nicht" — und genau das wäre hier nicht mehr zu
 * finden.
 */
class HostServerTest {

    private lateinit var folder: File
    private lateinit var material: HostCertificate.Material
    private lateinit var server: HostServer
    private lateinit var trust: SSLContext

    private val client = KeyPairGenerator.getInstance("EC").apply {
        initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
    }.generateKeyPair()

    @Before
    fun start() {
        folder = Files.createTempDirectory("host-server").toFile()

        material = HostCertificate.loadOrCreate(folder, "Pixel", listOf("localhost", "127.0.0.1"))

        val clients = ClientStore(File(folder, "clients.json"))
        val codes = PairingCodes()
        val sessions = SessionStore()

        server = HostServer(
            identity = HostIdentity.loadOrCreate(File(folder, "hostkey.txt")),
            pairing = PairingService(clients, codes, ChallengeStore(), sessions),
            codes = codes,
            material = material,
            deviceName = "Pixel",
            version = "1.9.0",
            port = 0,
            trustPort = 0,
            sessions = sessions,
            screen = { HostServer.Screen(1080, 2400) },
            address = { "127.0.0.1" },
        )

        server.start()
        trust = trustingContext(material.authorityDer)
    }

    @After
    fun stop() {
        server.stop()
    }

    @Test
    fun `health steht ohne Ausweis offen`() {
        val (status, body) = get("/health")

        assertEquals(200, status)
        assertEquals("ok", JSONObject(body).getString("status"))
    }

    @Test
    fun `die Selbstauskunft verlangt einen Ausweis`() {
        assertEquals(401, get("/api/info").first)
    }

    @Test
    fun `ein unbekannter Endpunkt kommt nicht durch, auch nicht angemeldet`() {
        val token = pairAndOpenSession()

        // Der Windows-Agent kann das; dieses Gerät nicht. Abgelehnt wird mit
        // 403 und einem Satz, der das sagt — 404 sähe nach einem Tippfehler aus.
        assertEquals(403, get("/api/power", token).first)
    }

    @Test
    fun `koppeln, anmelden, Auskunft holen`() {
        val token = pairAndOpenSession()
        val (status, body) = get("/api/info", token)

        assertEquals(200, status)

        val info = JSONObject(body)

        assertEquals("Pixel", info.getString("hostname"))
        assertEquals(1, info.getInt("protocol"))
        assertEquals(false, info.getBoolean("canWake"))
        assertEquals(material.fingerprint, info.getString("caFingerprint"))

        val capabilities = info.getJSONArray("capabilities")
        val list = (0 until capabilities.length()).map { capabilities.getString(it) }

        assertEquals(listOf("screen", "input", "files"), list)

        // Ein Bildschirm, und zwar der des Handys. Die App baut daraus dieselben
        // Tabs wie beim PC und blendet sie bei einem einzigen Eintrag aus.
        val monitors = info.getJSONArray("monitors")

        assertEquals(1, monitors.length())
        assertEquals(1080, monitors.getJSONObject(0).getInt("width"))
        assertEquals(2400, monitors.getJSONObject(0).getInt("height"))
    }

    @Test
    fun `der Kopplungscode kommt mit einem QR-Ziel`() {
        val (status, body) = post("/api/pair/code", "{}")

        assertEquals(200, status)

        val json = JSONObject(body)

        assertTrue(Regex("^\\d{6}$").matches(json.getString("code")))
        assertTrue(
            json.getString("pairingUri")
                .startsWith("remotedesktop://pair?host=127.0.0.1&port="),
        )
        assertTrue(json.getString("pairingUri").contains("&ca=${material.fingerprint}"))
    }

    @Test
    fun `ein falscher Code wird abgewiesen`() {
        post("/api/pair/code", "{}")

        val (status, body) = post(
            "/api/pair",
            JSONObject()
                .put("code", "000000")
                .put("label", "Laptop")
                .put("publicKey", publicKey())
                .toString(),
        )

        assertEquals(400, status)
        assertEquals("Code falsch oder abgelaufen.", JSONObject(body).getString("error"))
    }

    @Test
    fun `ein falsches Token kommt nicht herein`() {
        pairAndOpenSession()

        assertEquals(401, get("/api/info", "ausgedacht").first)
    }

    @Test
    fun `die CA liegt unverschluesselt zum Abholen bereit`() {
        val connection = URL("http://127.0.0.1:${server.boundTrustPort}/ca.crt")
            .openConnection() as HttpURLConnection

        assertEquals(200, connection.responseCode)
        assertEquals(
            material.fingerprint,
            connection.getHeaderField("X-Certificate-Fingerprint"),
        )

        val der = connection.inputStream.readBytes()

        // Was dort liegt, muss genau die Stelle sein, deren Fingerabdruck im
        // QR-Code steht — sonst bestätigt das Handy etwas anderes, als es
        // geprüft hat.
        assertEquals(material.fingerprint, HostCertificate.fingerprintOf(der))
        assertNotNull(
            CertificateFactory.getInstance("X.509")
                .generateCertificate(der.inputStream()) as X509Certificate,
        )
    }

    @Test
    fun `auf dem unverschluesselten Port gibt es sonst nichts`() {
        val connection = URL("http://127.0.0.1:${server.boundTrustPort}/api/info")
            .openConnection() as HttpURLConnection

        assertEquals(404, connection.responseCode)
    }

    @Test
    fun `der Widerruf nimmt dem Token sofort die Wirkung`() {
        val token = pairAndOpenSession()
        val clientId = JSONObject(get("/api/clients").second)
            .getJSONArray("clients")
            .getJSONObject(0)
            .getString("id")

        assertEquals(200, get("/api/info", token).first)
        assertEquals(200, delete("/api/clients/$clientId"))
        assertEquals(401, get("/api/info", token).first)
    }

    // ---- Hilfen -----------------------------------------------------------

    private fun publicKey(): String = Base64.getEncoder().encodeToString(client.public.encoded)

    private fun pairAndOpenSession(): String {
        val code = JSONObject(post("/api/pair/code", "{}").second).getString("code")

        val paired = JSONObject(
            post(
                "/api/pair",
                JSONObject()
                    .put("code", code)
                    .put("label", "Laptop")
                    .put("publicKey", publicKey())
                    .toString(),
            ).second,
        )

        val clientId = paired.getString("clientId")

        val nonce = JSONObject(
            post("/api/session/challenge", JSONObject().put("clientId", clientId).toString()).second,
        ).getString("nonce")

        return JSONObject(
            post(
                "/api/session",
                JSONObject()
                    .put("clientId", clientId)
                    .put("nonce", nonce)
                    .put("signature", sign(nonce))
                    .toString(),
            ).second,
        ).getString("token")
    }

    private fun get(path: String, token: String? = null): Pair<Int, String> =
        call("GET", path, null, token)

    private fun post(path: String, body: String, token: String? = null): Pair<Int, String> =
        call("POST", path, body, token)

    private fun delete(path: String): Int = call("DELETE", path, null, null).first

    private fun call(
        method: String,
        path: String,
        body: String?,
        token: String?,
    ): Pair<Int, String> {
        val connection = URL("https://127.0.0.1:${server.boundPort}$path")
            .openConnection() as HttpsURLConnection

        connection.sslSocketFactory = trust.socketFactory
        connection.requestMethod = method
        token?.let { connection.setRequestProperty("Authorization", "Bearer $it") }

        if (body != null) {
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json")
            connection.outputStream.use { it.write(body.toByteArray()) }
        }

        val status = connection.responseCode
        val stream = if (status < 400) connection.inputStream else connection.errorStream

        return status to (stream?.readBytes()?.toString(Charsets.UTF_8) ?: "")
    }

    /** Vertraut genau der einen Stelle, die dieser Host vorzeigt — sonst keiner. */
    private fun trustingContext(authorityDer: ByteArray): SSLContext {
        val authority = CertificateFactory.getInstance("X.509")
            .generateCertificate(authorityDer.inputStream()) as X509Certificate

        val store = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null, null)
            setCertificateEntry("host", authority)
        }

        val managers = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm())
            .apply { init(store) }

        return SSLContext.getInstance("TLS").apply { init(null, managers.trustManagers, null) }
    }

    private fun sign(nonce: String, pair: KeyPair = client): String {
        val der = Signature.getInstance("SHA256withECDSA").run {
            initSign(pair.private)
            update(Base64.getDecoder().decode(nonce))
            sign()
        }

        var index = 2

        if (der[1].toInt() and 0x80 != 0) {
            index += der[1].toInt() and 0x7F
        }

        fun readInteger(): BigInteger {
            index++
            val length = der[index++].toInt()
            val value = BigInteger(der.copyOfRange(index, index + length))
            index += length
            return value
        }

        val r = readInteger()
        val s = readInteger()

        fun pad(value: BigInteger): ByteArray {
            val bytes = value.toByteArray().dropWhile { it == 0.toByte() }.toByteArray()

            return ByteArray(32 - bytes.size) + bytes
        }

        return Base64.getEncoder().encodeToString(pad(r) + pad(s))
    }
}
