package com.rextechnologies.flint.receiver.browser

import java.net.IDN
import java.net.URI
import java.net.URISyntaxException
import java.nio.charset.StandardCharsets
import java.util.Locale

/**
 * The only address policy the receiver browser uses for an initial navigation and redirects.
 *
 * It deliberately performs no DNS lookup. A policy decision must not cause a network request,
 * and local-looking names are denied before WebView has an opportunity to reinterpret them.
 */
class BrowserUrlPolicy(
    private val allowDebugAppAssets: Boolean = false,
) {
    companion object {
        const val MAX_INPUT_BYTES: Int = 4 * 1024
        private const val HTTPS_PORT = 443
        private const val DEBUG_FIXTURE =
            "https://appassets.androidplatform.net/assets/flint-browser-fixture.html"
    }

    fun evaluate(rawInput: String): BrowserUrlResult {
        if (hasUnpairedSurrogate(rawInput)) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.MALFORMED_UNICODE)
        }
        if (rawInput.toByteArray(StandardCharsets.UTF_8).size > MAX_INPUT_BYTES) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.INPUT_TOO_LONG)
        }
        if (rawInput.any(::isControlCharacter)) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.INVALID_ADDRESS)
        }

        val input = rawInput.trim()
        if (input.isEmpty()) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.EMPTY_ADDRESS)
        }
        if (input.any(Char::isWhitespace)) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.INVALID_ADDRESS)
        }

        val uri = try {
            URI(input)
        } catch (_: URISyntaxException) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.INVALID_ADDRESS)
        }

        if (!uri.isAbsolute || !uri.scheme.equals("https", ignoreCase = true)) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.BLOCKED_SCHEME)
        }
        if (uri.rawUserInfo != null || uri.rawAuthority?.contains('@') == true) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.USER_INFO_NOT_ALLOWED)
        }

        val authority = parseAuthority(uri.rawAuthority)
            ?: return BrowserUrlResult.Rejected(BrowserUrlRejection.INVALID_ADDRESS)
        if (authority.invalidPort) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.INVALID_PORT)
        }
        if (authority.isIpLiteral || isLocalName(authority.host)) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.BLOCKED_LOCAL_RESOURCE)
        }

        val normalizedPath = uri.normalize().rawPath.orEmpty().ifEmpty { "/" }
        if (!normalizedPath.startsWith('/')) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.INVALID_ADDRESS)
        }

        val authorityText = buildString {
            append(authority.host)
            if (authority.port != null && authority.port != HTTPS_PORT) {
                append(':')
                append(authority.port)
            }
        }
        val canonical = buildString {
            append("https://")
            append(authorityText)
            append(normalizedPath)
            uri.rawQuery?.let {
                append('?')
                append(it)
            }
            uri.rawFragment?.let {
                append('#')
                append(it)
            }
        }
        val display = "https://$authorityText$normalizedPath"
        val isFixture = canonical == DEBUG_FIXTURE
        if (authority.host == "appassets.androidplatform.net" &&
            (!allowDebugAppAssets || !isFixture)
        ) {
            return BrowserUrlResult.Rejected(BrowserUrlRejection.BLOCKED_LOCAL_RESOURCE)
        }

        return BrowserUrlResult.Accepted(
            BrowserAddress(
                canonicalUrl = canonical,
                displayUrl = display,
                isDebugFixture = isFixture,
            ),
        )
    }

    private fun parseAuthority(rawAuthority: String?): ParsedAuthority? {
        if (rawAuthority.isNullOrEmpty() || rawAuthority.contains('@')) {
            return null
        }

        if (rawAuthority.startsWith('[')) {
            val closingBracket = rawAuthority.indexOf(']')
            if (closingBracket <= 1) {
                return null
            }
            val suffix = rawAuthority.substring(closingBracket + 1)
            val port = if (suffix.isEmpty()) {
                null
            } else {
                parsePortSuffix(suffix) ?: return ParsedAuthority.invalidPort()
            }
            return ParsedAuthority(host = rawAuthority.substring(1, closingBracket), port = port, isIpLiteral = true)
        }

        if (rawAuthority.count { it == ':' } > 1) {
            return null
        }
        val separator = rawAuthority.lastIndexOf(':')
        val hostPart = if (separator < 0) rawAuthority else rawAuthority.substring(0, separator)
        val port = if (separator < 0) {
            null
        } else {
            parsePortSuffix(rawAuthority.substring(separator)) ?: return ParsedAuthority.invalidPort()
        }
        if (hostPart.isBlank() || hostPart.contains('%') || hostPart.startsWith('.') || hostPart.endsWith('.')) {
            return null
        }

        val asciiHost = try {
            IDN.toASCII(hostPart, IDN.USE_STD3_ASCII_RULES).lowercase(Locale.ROOT)
        } catch (_: IllegalArgumentException) {
            return null
        }
        if (asciiHost.isBlank()) {
            return null
        }
        return ParsedAuthority(
            host = asciiHost,
            port = port,
            isIpLiteral = looksLikeIpLiteral(asciiHost),
        )
    }

    private fun parsePortSuffix(suffix: String): Int? {
        if (!suffix.startsWith(':')) {
            return null
        }
        val text = suffix.drop(1)
        if (text.isEmpty() || text.any { it !in '0'..'9' }) {
            return null
        }
        val port = text.toLongOrNull() ?: return null
        return if (port in 1..65_535) port.toInt() else null
    }

    private fun isLocalName(host: String): Boolean {
        val localSuffixes = listOf(".localhost", ".local", ".lan", ".internal", ".home", ".localdomain")
        return host == "localhost" ||
            !host.contains('.') ||
            localSuffixes.any { suffix -> host.endsWith(suffix) }
    }

    private fun looksLikeIpLiteral(host: String): Boolean =
        host.startsWith("0x", ignoreCase = true) ||
            host.all { it in '0'..'9' || it == '.' }

    private fun hasUnpairedSurrogate(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            when {
                Character.isHighSurrogate(value[index]) -> {
                    if (index + 1 == value.length || !Character.isLowSurrogate(value[index + 1])) {
                        return true
                    }
                    index += 2
                }
                Character.isLowSurrogate(value[index]) -> return true
                else -> index += 1
            }
        }
        return false
    }

    private fun isControlCharacter(character: Char): Boolean = Character.isISOControl(character)

    private data class ParsedAuthority(
        val host: String,
        val port: Int?,
        val isIpLiteral: Boolean,
        val invalidPort: Boolean = false,
    ) {
        companion object {
            fun invalidPort(): ParsedAuthority = ParsedAuthority("", null, false, invalidPort = true)
        }
    }
}

/** A URL that is safe to display and safe to pass to the receiver-owned WebView. */
data class BrowserAddress(
    val canonicalUrl: String,
    val displayUrl: String,
    val isDebugFixture: Boolean = false,
) {
    override fun toString(): String = "BrowserAddress(displayUrl=$displayUrl, isDebugFixture=$isDebugFixture)"
}

sealed interface BrowserUrlResult {
    data class Accepted(val url: BrowserAddress) : BrowserUrlResult

    data class Rejected(val reason: BrowserUrlRejection) : BrowserUrlResult {
        override fun toString(): String = "BrowserUrlResult.Rejected(reason=$reason)"
    }
}

/** Stable, non-echoing rejection codes suitable for state/UI presentation. */
enum class BrowserUrlRejection {
    EMPTY_ADDRESS,
    INPUT_TOO_LONG,
    MALFORMED_UNICODE,
    INVALID_ADDRESS,
    BLOCKED_SCHEME,
    BLOCKED_LOCAL_RESOURCE,
    USER_INFO_NOT_ALLOWED,
    INVALID_PORT,
}
