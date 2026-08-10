package app.remotedesktop.client.host

import java.io.File
import java.math.BigInteger
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.PublicKey
import java.security.SecureRandom
import java.security.Signature
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.util.Date
import java.util.Locale

/**
 * Das Zertifikat, mit dem sich dieses Handy ausweist — eine eigene kleine CA
 * und darunter das Serverzertifikat.
 *
 * Der Aufbau ist derselbe wie beim Windows-Agent
 * (`agent/Services/SelfSignedCertificate.cs`), und das ist der Punkt: das
 * Handy spricht dasselbe Protokoll, also muss es sich auch auf dieselbe Weise
 * ausweisen. Der Client holt die CA über den unverschlüsselten Port, vergleicht
 * ihren Fingerabdruck mit dem aus dem QR-Code und bestätigt sie einmal. Danach
 * darf das Handy sein Serverzertifikat jederzeit erneuern — etwa wenn es im
 * WLAN eine andere Adresse bekommt —, ohne dass jemand wieder durch die
 * Systemeinstellungen muss.
 *
 * Anders als auf Windows sind beide Schlüssel EC P-256 statt RSA-2048: kleiner,
 * schneller, und auf jedem Android seit Jahren vorhanden.
 */
object HostCertificate {

    /** Zehn Jahre für die CA — sie wird von Hand bestätigt und soll bleiben. */
    private const val AUTHORITY_DAYS = 3650L

    /** Gut zwei Jahre für das Serverzertifikat; es wird still erneuert. */
    private const val SERVER_DAYS = 825L

    /** Ab hier wird vorsorglich erneuert, statt auf den Ablauf zu warten. */
    private const val RENEW_BEFORE_DAYS = 30L

    private const val ALIAS = "remotedesktop"

    /** Der Speicher liegt in den privaten Dateien der App — dort kommt nur sie hin. */
    private const val FILE_NAME = "host-keystore.p12"

    /**
     * Das Kennwort des Speichers. Es schützt nichts vor jemandem, der die Datei
     * lesen kann — es steht ja hier. PKCS12 verlangt eins, und es muss überall
     * dasselbe sein, sonst ließe sich ein geschriebener Speicher nie wieder
     * öffnen. Der Schutz ist, dass nur diese App in ihr eigenes Verzeichnis
     * kommt.
     */
    private const val PASSWORD = "remotedesktop"

    private const val OID_ECDSA_SHA256 = "1.2.840.10045.4.3.2"
    private const val OID_COMMON_NAME = "2.5.4.3"
    private const val OID_BASIC_CONSTRAINTS = "2.5.29.19"
    private const val OID_KEY_USAGE = "2.5.29.15"
    private const val OID_SUBJECT_ALT_NAME = "2.5.29.17"
    private const val OID_EXT_KEY_USAGE = "2.5.29.37"
    private const val OID_SERVER_AUTH = "1.3.6.1.5.5.7.3.1"

    private val DAY_MILLIS = 24L * 60 * 60 * 1000

    /**
     * Was der Server zum Lauschen braucht und der Client zum Prüfen.
     *
     * @param authorityDer Die CA im DER-Format — genau das, was unter
     *   `/ca.crt` ausgeliefert wird.
     * @param fingerprint `sha256` über die CA, kleingeschrieben und ohne
     *   Trennzeichen. Derselbe Wert wie auf Windows, denn die Gegenseite
     *   vergleicht ihn ohne zu wissen, wer ihn erzeugt hat.
     */
    data class Material(
        val keyStore: KeyStore,
        val password: CharArray,
        val alias: String,
        val authorityDer: ByteArray,
        val fingerprint: String,
        val names: List<String>,
    )

    /**
     * Holt das vorhandene Material oder stellt neues aus.
     *
     * Neu ausgestellt wird, wenn es noch keins gibt, wenn das Serverzertifikat
     * bald abläuft — oder wenn die Adressliste sich geändert hat. Der letzte
     * Fall ist der häufige: ein Handy bekommt seine Adresse per DHCP, und ein
     * Zertifikat auf die Adresse von gestern lehnt jeder Client ab, ohne zu
     * sagen, warum.
     *
     * Die CA überlebt das alles. Sie neu auszustellen hieße, dass jeder
     * gekoppelte Client sie erneut bestätigen muss.
     */
    fun loadOrCreate(directory: File, subject: String, names: List<String>): Material {
        val file = File(directory, FILE_NAME)
        val password = PASSWORD.toCharArray()

        val existing = read(file, password)

        if (existing != null && existing.covers(names) && !existing.expiresSoon()) {
            return existing.toMaterial(names)
        }

        val authority = existing?.authorityPair ?: newKeyPair()
        val authorityCertificate = existing?.authorityCertificate
            ?: selfSign(authority, "RemoteDesktop $subject CA", AUTHORITY_DAYS)

        val server = newKeyPair()
        val serverCertificate = sign(
            issuer = authorityCertificate,
            issuerKey = authority.private,
            subjectKey = server.public,
            commonName = subject,
            days = SERVER_DAYS,
            names = names,
        )

        val store = KeyStore.getInstance("PKCS12").apply {
            load(null, password)
            setKeyEntry(
                ALIAS,
                server.private,
                password,
                arrayOf(serverCertificate, authorityCertificate),
            )
            // Die CA liegt ein zweites Mal als reiner Vertrauensanker daneben:
            // nur so lässt sie sich nach einem Neustart wieder herausholen,
            // ohne den privaten Schlüssel des Servers anzufassen.
            setCertificateEntry("$ALIAS-ca", authorityCertificate)
            setKeyEntry("$ALIAS-ca-key", authority.private, password, arrayOf(authorityCertificate))
        }

        directory.mkdirs()
        file.outputStream().use { store.store(it, password) }

        return material(store, password, authorityCertificate, names)
    }

    /** Der Fingerabdruck, an dem der Client die richtige Stelle erkennt. */
    fun fingerprintOf(certificate: ByteArray): String =
        MessageDigest.getInstance("SHA-256")
            .digest(certificate)
            .joinToString("") { String.format(Locale.ROOT, "%02x", it) }

    // ---- Innenleben -------------------------------------------------------

    private class Existing(
        val store: KeyStore,
        val password: CharArray,
        val authorityPair: KeyPair,
        val authorityCertificate: X509Certificate,
        val serverCertificate: X509Certificate,
    ) {
        fun covers(wanted: List<String>): Boolean =
            namesOf(serverCertificate).containsAll(wanted.map { it.lowercase(Locale.ROOT) })

        fun expiresSoon(): Boolean =
            serverCertificate.notAfter.time - System.currentTimeMillis() <
                RENEW_BEFORE_DAYS * DAY_MILLIS
    }

    private fun read(file: File, password: CharArray): Existing? {
        if (!file.exists()) {
            return null
        }

        return try {
            val store = KeyStore.getInstance("PKCS12")
            file.inputStream().use { store.load(it, password) }

            val server = store.getCertificate(ALIAS) as? X509Certificate ?: return null
            val authority = store.getCertificate("$ALIAS-ca") as? X509Certificate ?: return null
            val authorityKey = store.getKey("$ALIAS-ca-key", password) as? PrivateKey ?: return null

            Existing(
                store,
                password,
                KeyPair(authority.publicKey, authorityKey),
                authority,
                server,
            )
        } catch (broken: Exception) {
            // Ein unlesbarer Speicher ist dasselbe wie keiner: neu ausstellen.
            // Alles andere hieße, dass das Handy nach einem halb geschriebenen
            // Speicher nie wieder startet.
            null
        }
    }

    private fun Existing.toMaterial(names: List<String>): Material =
        material(store, password, authorityCertificate, names)

    private fun material(
        store: KeyStore,
        password: CharArray,
        authority: X509Certificate,
        names: List<String>,
    ): Material {
        val der = authority.encoded

        return Material(store, password, ALIAS, der, fingerprintOf(der), names)
    }

    private fun newKeyPair(): KeyPair =
        KeyPairGenerator.getInstance("EC").apply {
            initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
        }.generateKeyPair()

    private fun selfSign(pair: KeyPair, commonName: String, days: Long): X509Certificate =
        build(
            issuerName = commonName,
            issuerKey = pair.private,
            subjectKey = pair.public,
            commonName = commonName,
            days = days,
            names = emptyList(),
            authority = true,
        )

    private fun sign(
        issuer: X509Certificate,
        issuerKey: PrivateKey,
        subjectKey: PublicKey,
        commonName: String,
        days: Long,
        names: List<String>,
    ): X509Certificate =
        build(
            issuerName = commonNameOf(issuer),
            issuerKey = issuerKey,
            subjectKey = subjectKey,
            commonName = commonName,
            days = days,
            names = names,
            authority = false,
        )

    private fun build(
        issuerName: String,
        issuerKey: PrivateKey,
        subjectKey: PublicKey,
        commonName: String,
        days: Long,
        names: List<String>,
        authority: Boolean,
    ): X509Certificate {
        // Fünf Minuten Vorlauf: die Uhren zweier Geräte gehen selten gleich,
        // und ein Zertifikat „aus der Zukunft" lehnt jeder Client ab.
        val from = Date(System.currentTimeMillis() - 5 * 60 * 1000)
        val until = Date(from.time + days * DAY_MILLIS)

        val algorithm = Der.sequence(Der.oid(OID_ECDSA_SHA256))

        val tbs = Der.sequence(
            Der.explicit(0, Der.integer(2)),
            Der.integer(BigInteger(159, SecureRandom())),
            algorithm,
            name(issuerName),
            Der.sequence(Der.utcTime(from), Der.utcTime(until)),
            name(commonName),
            subjectKey.encoded,
            Der.explicit(3, Der.sequence(*extensions(authority, names).toTypedArray())),
        )

        val signature = Signature.getInstance("SHA256withECDSA").run {
            initSign(issuerKey)
            update(tbs)
            sign()
        }

        val certificate = Der.sequence(tbs, algorithm, Der.bitString(signature))

        return CertificateFactory.getInstance("X.509")
            .generateCertificate(certificate.inputStream()) as X509Certificate
    }

    private fun extensions(authority: Boolean, names: List<String>): List<ByteArray> {
        val list = mutableListOf<ByteArray>()

        list += extension(
            OID_BASIC_CONSTRAINTS,
            critical = true,
            // pathLength 0: diese Stelle darf genau Endzertifikate ausstellen
            // und keine weiteren Stellen — selbst wenn jemand ihren Schlüssel
            // bekäme.
            value = if (authority) {
                Der.sequence(Der.boolean(true), Der.integer(0))
            } else {
                Der.sequence()
            },
        )

        list += extension(
            OID_KEY_USAGE,
            critical = true,
            value = if (authority) {
                // keyCertSign + cRLSign
                Der.bitString(byteArrayOf(0x06), unused = 1)
            } else {
                // digitalSignature + keyEncipherment
                Der.bitString(byteArrayOf(0xA0.toByte()), unused = 5)
            },
        )

        if (!authority) {
            list += extension(
                OID_EXT_KEY_USAGE,
                critical = false,
                value = Der.sequence(Der.oid(OID_SERVER_AUTH)),
            )

            if (names.isNotEmpty()) {
                list += extension(
                    OID_SUBJECT_ALT_NAME,
                    critical = false,
                    value = Der.sequence(*names.map(::generalName).toTypedArray()),
                )
            }
        }

        return list
    }

    /**
     * Ein Eintrag in der Namensliste.
     *
     * Was als IP-Adresse lesbar ist, kommt als IP-Eintrag hinein und nicht als
     * Name: ein Client, der `https://192.168.178.31:8443` aufruft, sieht sich
     * ausschließlich die IP-Einträge an, und ein Namenseintrag „192.168.178.31"
     * hilft ihm nicht.
     */
    private fun generalName(name: String): ByteArray {
        val trimmed = name.trim().trim('[', ']').lowercase(Locale.ROOT)
        val address = parseIpv4(trimmed)

        return if (address != null) {
            Der.implicit(7, address)
        } else {
            Der.implicit(2, trimmed.toByteArray(Charsets.US_ASCII))
        }
    }

    private fun parseIpv4(text: String): ByteArray? {
        val parts = text.split('.')

        if (parts.size != 4) {
            return null
        }

        val bytes = ByteArray(4)

        for (index in parts.indices) {
            val value = parts[index].toIntOrNull() ?: return null

            if (value !in 0..255 || (parts[index].length > 1 && parts[index].startsWith("0"))) {
                return null
            }

            bytes[index] = value.toByte()
        }

        return bytes
    }

    private fun extension(oid: String, critical: Boolean, value: ByteArray): ByteArray =
        if (critical) {
            Der.sequence(Der.oid(oid), Der.boolean(true), Der.octetString(value))
        } else {
            Der.sequence(Der.oid(oid), Der.octetString(value))
        }

    private fun name(commonName: String): ByteArray =
        Der.sequence(Der.set(Der.sequence(Der.oid(OID_COMMON_NAME), Der.utf8(commonName))))

    private fun commonNameOf(certificate: X509Certificate): String =
        certificate.subjectX500Principal.name
            .split(',')
            .map(String::trim)
            .firstOrNull { it.startsWith("CN=") }
            ?.removePrefix("CN=")
            ?: "RemoteDesktop"

    /** Die Namen, auf die ein Zertifikat lautet — aus `subjectAltName`. */
    private fun namesOf(certificate: X509Certificate): Set<String> =
        certificate.subjectAlternativeNames.orEmpty()
            .mapNotNull { it.getOrNull(1) as? String }
            .map { it.lowercase(Locale.ROOT) }
            .toSet()

}
