package com.rextechnologies.flint.protocol.http

import java.security.SecureRandom
import java.util.concurrent.ConcurrentHashMap
import kotlin.io.encoding.Base64

/** Opaque item identifier: it is deliberately not a file name or filesystem path. */
class SharedItemId private constructor(private val value: String) {
    override fun equals(other: Any?): Boolean = other is SharedItemId && value == other.value
    override fun hashCode(): Int = value.hashCode()
    override fun toString(): String = value

    companion object {
        private val FORMAT = Regex("[A-Za-z0-9_-]{22}")

        fun generate(random: SecureRandom = SecureRandom()): SharedItemId {
            val bytes = ByteArray(16)
            random.nextBytes(bytes)
            val value = Base64.UrlSafe.withPadding(Base64.PaddingOption.ABSENT).encode(bytes)
            return SharedItemId(value)
        }

        fun parse(value: String): SharedItemId {
            require(FORMAT.matches(value)) { "Shared item id must be a canonical opaque identifier" }
            return SharedItemId(value)
        }

        fun parseOrNull(value: String): SharedItemId? =
            if (FORMAT.matches(value)) SharedItemId(value) else null
    }
}

/**
 * Parser/builder for `/media/v1/<session-token>/<opaque-item-id>` request paths.
 *
 * Percent escapes and backslashes are rejected rather than decoded. The server
 * never turns any client-provided segment into a local path.
 */
@ConsistentCopyVisibility
data class TokenPath private constructor(val itemId: SharedItemId) {
    companion object {
        fun build(token: SessionToken, itemId: SharedItemId): String =
            "/media/v1/$token/$itemId"

        fun parse(rawRequestTarget: String, expectedToken: SessionToken): TokenPath? {
            if (rawRequestTarget.isEmpty() || rawRequestTarget.length > 512) return null
            if (rawRequestTarget.any { it == '\\' || it == '%' || it == '\u0000' || it.isISOControl() }) {
                return null
            }
            if ('#' in rawRequestTarget) return null
            val path = rawRequestTarget.substringBefore('?')
            val segments = path.split('/')
            if (segments.size != 5 || segments[0].isNotEmpty()) return null
            if (segments[1] != "media" || segments[2] != "v1") return null
            val presentedToken = segments[3]
            if (!expectedToken.constantTimeMatches(presentedToken)) return null
            if (SessionToken.parseOrNull(presentedToken) == null) return null
            val itemId = SharedItemId.parseOrNull(segments[4]) ?: return null
            return TokenPath(itemId)
        }
    }
}

/**
 * A session-scoped explicit allowlist. Resolution can only return values that
 * the application deliberately registered; request paths cannot name files.
 */
class TokenPathRegistry<T>(
    val sessionToken: SessionToken = SessionToken.generate(),
    private val random: SecureRandom = SecureRandom(),
) {
    private val items = ConcurrentHashMap<SharedItemId, T>()

    fun share(value: T): String {
        var itemId: SharedItemId
        do {
            itemId = SharedItemId.generate(random)
        } while (items.putIfAbsent(itemId, value) != null)
        return TokenPath.build(sessionToken, itemId)
    }

    fun resolve(rawRequestTarget: String): T? {
        val parsed = TokenPath.parse(rawRequestTarget, sessionToken) ?: return null
        return items[parsed.itemId]
    }

    fun revoke(rawRequestTarget: String): Boolean {
        val parsed = TokenPath.parse(rawRequestTarget, sessionToken) ?: return false
        return items.remove(parsed.itemId) != null
    }

    fun clear() = items.clear()
}

