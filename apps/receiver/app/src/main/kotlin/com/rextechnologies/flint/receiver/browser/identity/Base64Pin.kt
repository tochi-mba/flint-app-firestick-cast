package com.rextechnologies.flint.receiver.browser.identity

/**
 * Standard base64 encoding for the certificate pin.
 *
 * # Why this is not one of the platform's encoders
 *
 * The pin is a protocol value: the Windows host parses `sha256/<base64>` and compares it byte for
 * byte, so this has to produce exactly what `Convert.ToBase64String` does on the other side, on
 * every device Flint supports, and in a plain JVM unit test.
 *
 * Neither platform encoder manages all three.
 *
 * * `java.util.Base64` arrived in **API 26**. This receiver supports Fire OS devices reporting API
 *   25, where merely referencing it throws `ClassNotFoundException` at runtime. That is not a
 *   hypothetical: it shipped, and because the caller swallowed the failure it disabled the entire
 *   browser feature while looking exactly like a device that had no browser to begin with.
 * * `android.util.Base64` works on the device and is a stub in plain JVM unit tests, which return
 *   null and make every fingerprint assertion meaningless.
 *
 * So the encoding is written out. It is twenty lines of a fully specified format, and
 * [Base64PinTest] holds it to the RFC 4648 test vectors so "hand-rolled" cannot quietly become
 * "wrong".
 */
internal object Base64Pin {
    private const val ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
    private const val PAD = '='

    /**
     * Encodes [bytes] as standard base64 with padding and no line breaks.
     *
     * Padded because the host's decoder expects a canonical value, and unwrapped because the pin
     * occupies one protocol field rather than a MIME body.
     */
    fun encode(bytes: ByteArray): String {
        if (bytes.isEmpty()) {
            return ""
        }

        val out = StringBuilder((bytes.size + 2) / 3 * 4)
        var index = 0
        while (index + 2 < bytes.size) {
            val chunk = (bytes[index].toInt() and 0xFF shl 16) or
                (bytes[index + 1].toInt() and 0xFF shl 8) or
                (bytes[index + 2].toInt() and 0xFF)
            out.append(ALPHABET[chunk ushr 18 and 0x3F])
            out.append(ALPHABET[chunk ushr 12 and 0x3F])
            out.append(ALPHABET[chunk ushr 6 and 0x3F])
            out.append(ALPHABET[chunk and 0x3F])
            index += 3
        }

        // The tail: one or two bytes short of a group, padded to four characters. Getting this wrong
        // is the classic base64 defect, and it only shows on inputs whose length is not a multiple
        // of three — which a SHA-256 hash never is, so it would have failed every single time.
        when (bytes.size - index) {
            1 -> {
                val chunk = bytes[index].toInt() and 0xFF shl 16
                out.append(ALPHABET[chunk ushr 18 and 0x3F])
                out.append(ALPHABET[chunk ushr 12 and 0x3F])
                out.append(PAD)
                out.append(PAD)
            }
            2 -> {
                val chunk = (bytes[index].toInt() and 0xFF shl 16) or
                    (bytes[index + 1].toInt() and 0xFF shl 8)
                out.append(ALPHABET[chunk ushr 18 and 0x3F])
                out.append(ALPHABET[chunk ushr 12 and 0x3F])
                out.append(ALPHABET[chunk ushr 6 and 0x3F])
                out.append(PAD)
            }
        }
        return out.toString()
    }
}
