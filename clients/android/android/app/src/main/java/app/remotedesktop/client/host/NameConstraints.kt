package app.remotedesktop.client.host

import java.security.cert.X509Certificate
import java.util.Locale

/**
 * Wofür die eigene Stelle Zertifikate unterschreiben darf — X.509 Name
 * Constraints (RFC 5280, 4.2.1.10), kritisch.
 *
 * **Der Befund dahinter (Durchsicht B5):** die CA dieses Handys landet am
 * Rechner in der Liste vertrauter Stellen und am anderen Handy im
 * Zertifikatspeicher des Benutzers, dem auch Chrome vertraut. Ohne
 * Einschränkung könnte, wer den Schlüssel dieser Stelle hat, ein Zertifikat
 * für `google.de` unterschreiben. Mit den Einschränkungen hier gilt sie nur
 * für private Adressen, die üblichen Heimnetz-Endungen, Tailscale und die
 * eigenen Namen. Dasselbe tut der Rechner in `agent/Services/NameConstraints.cs`.
 */
object NameConstraints {

    const val OID = "2.5.29.30"

    /** Endungen, unter denen ein Gerät zu Hause heißt — ohne Punkt davor. */
    val DEFAULT_SUFFIXES = listOf(
        "localhost", "local", "lan", "home", "internal", "fritz.box", "home.arpa", "ts.net",
    )

    /** Private Bereiche, Loopback und Link-Local, als Adresse und Präfixlänge. */
    val DEFAULT_RANGES: List<Pair<ByteArray, Int>> = listOf(
        byteArrayOf(10, 0, 0, 0) to 8,
        byteArrayOf(172.toByte(), 16, 0, 0) to 12,
        byteArrayOf(192.toByte(), 168.toByte(), 0, 0) to 16,
        byteArrayOf(100, 64, 0, 0) to 10,
        byteArrayOf(169.toByte(), 254.toByte(), 0, 0) to 16,
        byteArrayOf(127, 0, 0, 0) to 8,
    )

    private const val TAG_DNS = 0x82
    private const val TAG_IP = 0x87

    /**
     * Der Wert der Erweiterung für eine neue Stelle: die Vorgaben plus alles,
     * was [names] darüber hinaus nennt.
     */
    fun build(names: List<String>): ByteArray {
        val dns = DEFAULT_SUFFIXES.toMutableList()
        val ranges = DEFAULT_RANGES.toMutableList()

        for (raw in names) {
            val name = normalize(raw)

            if (name.isEmpty()) {
                continue
            }

            val address = parseIpv4(name)

            if (address != null) {
                if (ranges.none { (network, prefix) -> covers(network, prefix, address) }) {
                    ranges += address to 32
                }
            } else if (dns.none { matchesSuffix(name, it) }) {
                dns += name
            }
        }

        val subtrees = dns.map { Der.sequence(Der.implicit(2, it.toByteArray(Charsets.US_ASCII))) } +
            ranges.map { (network, prefix) -> Der.sequence(Der.implicit(7, network + mask(prefix))) }

        // permittedSubtrees [0] IMPLICIT SEQUENCE OF GeneralSubtree
        return Der.sequence(Der.implicitSequence(0, subtrees.fold(ByteArray(0)) { acc, part -> acc + part }))
    }

    /**
     * Ob die Stelle für diese Namen unterschreiben darf. Eine Stelle ohne die
     * Erweiterung — von vor v1.4 — darf alles; sie bleibt stehen, damit
     * bestehende Kopplungen gültig bleiben.
     */
    fun permits(authority: X509Certificate, names: List<String>): Boolean {
        val raw = authority.getExtensionValue(OID) ?: return true
        val (dns, ranges) = read(raw) ?: return false

        return names.map(::normalize).filter { it.isNotEmpty() }.all { name ->
            val address = parseIpv4(name)

            if (address != null) {
                ranges.any { (network, prefix) -> covers(network, prefix, address) }
            } else {
                dns.any { matchesSuffix(name, it) }
            }
        }
    }

    /** Liest die erlaubten Namen und Bereiche aus dem Wert der Erweiterung. */
    private fun read(raw: ByteArray): Pair<List<String>, List<Pair<ByteArray, Int>>>? = runCatching {
        val dns = mutableListOf<String>()
        val ranges = mutableListOf<Pair<ByteArray, Int>>()

        // OCTET STRING → SEQUENCE → [0] → SEQUENCE OF GeneralSubtree
        val outer = element(raw, 0)
        val constraints = element(outer.content, 0)
        var offset = 0

        while (offset < constraints.content.size) {
            val part = element(constraints.content, offset)
            offset = part.next

            if (part.tag != 0xA0) {
                continue
            }

            var inner = 0

            while (inner < part.content.size) {
                val subtree = element(part.content, inner)
                inner = subtree.next

                val base = element(subtree.content, 0)

                when (base.tag) {
                    TAG_DNS -> dns += normalize(String(base.content, Charsets.US_ASCII))
                    TAG_IP -> if (base.content.size == 8) {
                        ranges += base.content.copyOfRange(0, 4) to prefixOf(base.content.copyOfRange(4, 8))
                    }
                }
            }
        }

        dns.toList() to ranges.toList()
    }.getOrNull()

    private class Element(val tag: Int, val content: ByteArray, val next: Int)

    /** Ein DER-Element ab [offset]: Kennung, Inhalt, und wo das nächste beginnt. */
    private fun element(bytes: ByteArray, offset: Int): Element {
        val tag = bytes[offset].toInt() and 0xFF
        var position = offset + 1
        var length = bytes[position].toInt() and 0xFF
        position++

        if (length and 0x80 != 0) {
            val count = length and 0x7F
            length = 0

            repeat(count) {
                length = (length shl 8) or (bytes[position].toInt() and 0xFF)
                position++
            }
        }

        return Element(tag, bytes.copyOfRange(position, position + length), position + length)
    }

    private fun matchesSuffix(name: String, suffix: String): Boolean =
        name == suffix || name.endsWith(".$suffix")

    private fun covers(network: ByteArray, prefix: Int, address: ByteArray): Boolean {
        if (network.size != address.size) {
            return false
        }

        for (bit in 0 until prefix) {
            val mask = 0x80 ushr (bit % 8)

            if ((network[bit / 8].toInt() and mask) != (address[bit / 8].toInt() and mask)) {
                return false
            }
        }

        return true
    }

    private fun mask(prefix: Int): ByteArray {
        val mask = ByteArray(4)

        for (bit in 0 until prefix) {
            mask[bit / 8] = (mask[bit / 8].toInt() or (0x80 ushr (bit % 8))).toByte()
        }

        return mask
    }

    private fun prefixOf(mask: ByteArray): Int {
        var prefix = 0

        for (value in mask) {
            for (bit in 7 downTo 0) {
                if ((value.toInt() and (1 shl bit)) == 0) {
                    return prefix
                }

                prefix++
            }
        }

        return prefix
    }

    internal fun parseIpv4(text: String): ByteArray? {
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

    private fun normalize(name: String): String = name.trim().trim('[', ']').lowercase(Locale.ROOT)
}
