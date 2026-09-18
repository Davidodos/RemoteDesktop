package app.remotedesktop.client.host

import java.util.Locale

/**
 * Was im QR-Code der Kopplung steht. Leseseite ist `app/src/lib/pairingUri.ts`,
 * Schwesterfassung `agent/Auth/PairingUri.cs` — dasselbe Format, damit derselbe
 * Scanner beides versteht.
 */
object PairingUri {

    fun build(host: String, port: Int, code: String, caFingerprint: String?): String {
        val trimmed = host.trim()

        require(trimmed.isNotEmpty()) { "Ohne Adresse ergibt der QR-Code keinen Sinn." }
        require(port in 1..65535) { "Der Port liegt außerhalb des möglichen Bereichs." }
        require(Regex("^\\d{6}$").matches(code)) { "Der Kopplungscode besteht aus sechs Ziffern." }

        val name = java.net.URLEncoder.encode(trimmed.lowercase(Locale.ROOT), "UTF-8")
        val uri = "remotedesktop://pair?host=$name&port=$port&code=$code"

        return if (caFingerprint.isNullOrBlank()) {
            uri
        } else {
            val fingerprint = java.net.URLEncoder.encode(
                caFingerprint.trim().lowercase(Locale.ROOT), "UTF-8",
            )

            "$uri&ca=$fingerprint"
        }
    }
}
