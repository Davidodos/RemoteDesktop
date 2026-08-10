package app.remotedesktop.client.host

import java.io.File
import java.math.BigInteger
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.SecureRandom
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.security.spec.PKCS8EncodedKeySpec
import java.security.spec.X509EncodedKeySpec
import java.util.Base64

/**
 * Das Schlüsselpaar dieses Handys, beim ersten Start erzeugt.
 *
 * Es beantwortet die Frage, die sich der Client stellt: „Ist das noch dasselbe
 * Handy, mit dem ich mich damals gekoppelt habe?" Der Fingerabdruck bleibt
 * gleich, auch wenn das Gerät umbenannt wird oder eine andere Adresse bekommt —
 * deshalb merkt sich der Client ihn statt des Namens.
 *
 * Gegenstück zu `agent/Auth/AgentIdentity.cs`.
 */
class HostIdentity private constructor(private val pair: KeyPair) {

    /** Öffentlicher Schlüssel als Base64 im SPKI-Format. */
    val publicKey: String = Base64.getEncoder().encodeToString(pair.public.encoded)

    /**
     * Die ersten 16 Hex-Stellen des SHA-256 über den öffentlichen Schlüssel.
     * 64 Bit reichen: der Wert wird nicht geraten, sondern bei der Kopplung
     * übernommen und danach nur noch verglichen.
     */
    val fingerprint: String = shortFingerprint(pair.public.encoded)

    companion object {

        /**
         * Lädt das Schlüsselpaar oder erzeugt es. Die Datei enthält den
         * privaten Schlüssel im Klartext und liegt in den privaten Dateien der
         * App — genau wie der TLS-Schlüssel daneben.
         */
        fun loadOrCreate(file: File): HostIdentity {
            existing(file)?.let { return it }

            val pair = KeyPairGenerator.getInstance("EC").apply {
                initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
            }.generateKeyPair()

            file.parentFile?.mkdirs()

            // Beide Hälften, eine je Zeile. Java gibt aus einem privaten
            // EC-Schlüssel den öffentlichen nicht wieder heraus — man müsste
            // ihn über die Kurve nachrechnen. Zwei Zeilen sind billiger als
            // eine eigene Punktmultiplikation, und es gibt nichts daran falsch
            // zu machen.
            file.writeText(
                Base64.getEncoder().encodeToString(pair.private.encoded) +
                    "\n" +
                    Base64.getEncoder().encodeToString(pair.public.encoded),
            )

            return HostIdentity(pair)
        }

        /**
         * Das abgelegte Paar, oder `null`. Eine unlesbare Datei zählt als
         * „keine": dann wird ein neues Paar erzeugt, und alle gekoppelten
         * Clients müssen erneut koppeln. Das ist unangenehm, aber sichtbar —
         * ein Host, der wegen einer halben Zeile gar nicht erst startet, wäre
         * es nicht.
         */
        private fun existing(file: File): HostIdentity? {
            if (!file.exists()) {
                return null
            }

            return runCatching {
                val lines = file.readText().trim().lines()

                if (lines.size != 2) {
                    return null
                }

                val factory = KeyFactory.getInstance("EC")

                HostIdentity(
                    KeyPair(
                        factory.generatePublic(
                            X509EncodedKeySpec(Base64.getDecoder().decode(lines[1].trim())),
                        ),
                        factory.generatePrivate(
                            PKCS8EncodedKeySpec(Base64.getDecoder().decode(lines[0].trim())),
                        ),
                    ),
                )
            }.getOrNull()
        }

        /** Nur für Tests: eine Identität, die nirgends landet. */
        fun transient(): HostIdentity = HostIdentity(
            KeyPairGenerator.getInstance("EC").apply {
                initialize(ECGenParameterSpec("secp256r1"), SecureRandom())
            }.generateKeyPair(),
        )

        /**
         * Prüft die Unterschrift eines Clients über eine Challenge.
         *
         * Erwartet wird das Format, das die WebCrypto-API des Browsers liefert:
         * r und s hintereinander mit fester Länge, **nicht** DER. Java kennt
         * nur DER, also wird umgerechnet. Wer hier das falsche Format annimmt,
         * bekommt eine Prüfung, die immer fehlschlägt — oder, schlimmer, eine,
         * die zu viel durchlässt.
         */
        fun verifyClientSignature(
            publicKeyBase64: String,
            data: ByteArray,
            signatureBase64: String,
        ): Boolean = runCatching {
            val key = KeyFactory.getInstance("EC").generatePublic(
                X509EncodedKeySpec(Base64.getDecoder().decode(publicKeyBase64)),
            )

            val raw = Base64.getDecoder().decode(signatureBase64)
            val der = concatToDer(raw) ?: return false

            Signature.getInstance("SHA256withECDSA").run {
                initVerify(key)
                update(data)
                verify(der)
            }
            // Ein unbrauchbarer Schlüssel oder eine unbrauchbare Unterschrift
            // sind kein Sonderfall, sondern einfach „nicht bestanden".
        }.getOrDefault(false)

        /**
         * Aus r‖s wird `SEQUENCE { INTEGER r, INTEGER s }`.
         *
         * Beide Hälften sind bei P-256 genau 32 Byte lang und vorzeichenlos.
         * `BigInteger(1, …)` hält sie positiv — ohne die 1 würde aus einem
         * ersten Byte über 0x7F eine negative Zahl und die Prüfung schlüge fehl,
         * und zwar nur bei etwa jeder zweiten Unterschrift.
         */
        private fun concatToDer(raw: ByteArray): ByteArray? {
            if (raw.size % 2 != 0 || raw.isEmpty()) {
                return null
            }

            val half = raw.size / 2

            return Der.sequence(
                Der.integer(BigInteger(1, raw.copyOfRange(0, half))),
                Der.integer(BigInteger(1, raw.copyOfRange(half, raw.size))),
            )
        }
    }
}
