package com.rextechnologies.flint.receiver.browser

import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.util.Base64

/** Decides which IDs are durable TV profiles permitted to own a VPN credential. */
fun interface BrowserNetworkProfileAuthority {
    fun isPersistentTvProfile(profileId: String): Boolean
}

/** Keeps VPN ownership aligned with the authoritative TV profile catalog, never device projections. */
class BrowserLibraryNetworkProfileAuthority(
    private val libraryStore: BrowserLibraryStore,
) : BrowserNetworkProfileAuthority {
    override fun isPersistentTvProfile(profileId: String): Boolean =
        libraryStore.profilesSnapshot().profiles.any { it.id == profileId }
}

/**
 * Encrypted per-TV-profile VPN settings.
 *
 * Only the opaque WireGuard configuration is secret, but it is the part that matters: it is
 * AES-GCM encrypted before it reaches disk and bound to both the local profile ID and provider via
 * associated data. There is deliberately no plaintext fallback. A legacy version-one plaintext
 * file is discarded rather than migrated, so an old config can never silently auto-connect.
 */
class BrowserNetworkStore(
    private val file: File,
    private val crypto: BrowserNetworkCrypto,
    private val profileAuthority: BrowserNetworkProfileAuthority,
) {
    companion object {
        const val MAX_CONFIG_UTF8_BYTES = ProfileNetworkSettings.MAX_CONFIG_UTF8_BYTES
        const val MAX_PROFILE_ID_CHARS = BrowserLibraryStore.MAX_PROFILE_ID_CHARS
        const val MAX_PROFILES = BrowserLibraryStore.MAX_PROFILES
        const val MAX_FILE_BYTES = 2 * 1024 * 1024
        private const val FORMAT_VERSION = 2L
        private const val LEGACY_PLAINTEXT_FORMAT_VERSION = 1L
        private const val MAX_JSON_DEPTH = 8
        private const val MAX_JSON_OBJECT_MEMBERS = 128
        private const val GCM_TAG_BYTES = 16
        private const val GCM_IV_BYTES = 12
        private const val MAX_CIPHERTEXT_BYTES = MAX_CONFIG_UTF8_BYTES + GCM_TAG_BYTES
        private const val MAX_CIPHERTEXT_BASE64_CHARS = ((MAX_CIPHERTEXT_BYTES + 2) / 3) * 4
        private const val MAX_IV_BASE64_CHARS = ((GCM_IV_BYTES + 2) / 3) * 4
        private const val MAX_JSON_STRING_CODE_UNITS = MAX_CIPHERTEXT_BASE64_CHARS + 64
        private const val AAD_SCHEMA = "flint.browser-network.v2"
    }

    private data class StoredNetworkSettings(
        val settings: ProfileNetworkSettings,
        val ciphertext: BrowserNetworkCiphertext,
    )

    private data class LoadResult(
        val records: Map<String, StoredNetworkSettings>,
        val rewriteRequired: Boolean,
    )

    private var current: Map<String, StoredNetworkSettings> = emptyMap()

    init {
        val loaded = load()
        current = loaded.records
        if (loaded.rewriteRequired) rewriteSafely()
        // An interrupted legacy write can leave a plaintext `.bak` or `.tmp` beside a healthy
        // primary. Neither is a recovery source once the primary exists, so remove both.
        removeStaleArtifacts()
    }

    @Synchronized
    fun get(profileId: String): ProfileNetworkSettings? =
        profileId.takeIf(::isValidProfileId)
            ?.takeIf(profileAuthority::isPersistentTvProfile)
            ?.let(current::get)
            ?.settings

    /**
     * Persists settings only for an existing TV profile. A disabled update erases the credential
     * instead of retaining a dormant secret or an auto-connect preference.
     */
    @Synchronized
    fun put(profileId: String, settings: ProfileNetworkSettings): Boolean {
        if (!isPermittedProfile(profileId)) return false
        val validated = settings.validated() ?: return false
        if (!validated.vpnEnabled) {
            return removeStored(profileId, reportMissing = false)
        }
        if (!current.containsKey(profileId) && current.size >= MAX_PROFILES) return false

        val plaintext = validated.configText.toByteArray(StandardCharsets.UTF_8)
        val ciphertext = try {
            crypto.encrypt(plaintext, associatedData(profileId, validated.provider))
        } catch (_: BrowserNetworkCryptoException) {
            return false
        } finally {
            plaintext.fill(0)
        }
        if (!isBounded(ciphertext)) return false

        persist(current + (profileId to StoredNetworkSettings(validated, ciphertext)))
        return true
    }

    /** Removes settings for an existing local TV profile. */
    @Synchronized
    fun remove(profileId: String): Boolean {
        if (!isPermittedProfile(profileId)) return false
        return removeStored(profileId, reportMissing = true)
    }

    /**
     * Cascade hook for a profile already deleted from [BrowserLibraryStore]. It deliberately does
     * not accept a still-existing profile, which prevents callers from using a deletion callback as
     * a generic destructive API.
     */
    @Synchronized
    fun removeForDeletedTvProfile(profileId: String): Boolean {
        if (!isValidProfileId(profileId) || profileAuthority.isPersistentTvProfile(profileId)) return false
        return removeStored(profileId, reportMissing = true)
    }

    /** A defensive copy containing only current, local-TV owned settings. */
    @Synchronized
    fun snapshot(): Map<String, ProfileNetworkSettings> = current
        .asSequence()
        .filter { (id, _) -> profileAuthority.isPersistentTvProfile(id) }
        .associate { (id, stored) -> id to stored.settings.copy() }

    private fun removeStored(profileId: String, reportMissing: Boolean): Boolean {
        if (!current.containsKey(profileId)) return !reportMissing
        persist(current - profileId)
        return true
    }

    private fun isPermittedProfile(profileId: String): Boolean =
        isValidProfileId(profileId) && profileAuthority.isPersistentTvProfile(profileId)

    private fun load(): LoadResult {
        restoreBackupIfNeeded()
        if (!file.isFile) {
            return LoadResult(emptyMap(), rewriteRequired = backupFile().isFile || temporaryFile().isFile)
        }
        if (file.length() !in 1..MAX_FILE_BYTES.toLong()) return LoadResult(emptyMap(), rewriteRequired = true)
        return try {
            val root = NetworkStoreJson.parse(file.readText(StandardCharsets.UTF_8)) as? Map<*, *>
                ?: return LoadResult(emptyMap(), rewriteRequired = true)
            when (root["version"] as? Long) {
                FORMAT_VERSION -> decodeV2(root)
                // Never decrypt, import or preserve the older configText schema. It was plaintext.
                LEGACY_PLAINTEXT_FORMAT_VERSION -> LoadResult(emptyMap(), rewriteRequired = true)
                else -> LoadResult(emptyMap(), rewriteRequired = true)
            }
        } catch (_: Exception) {
            LoadResult(emptyMap(), rewriteRequired = true)
        }
    }

    private fun decodeV2(root: Map<*, *>): LoadResult {
        val profiles = root["profiles"] as? Map<*, *> ?: return LoadResult(emptyMap(), true)
        val loaded = LinkedHashMap<String, StoredNetworkSettings>()
        var rewriteRequired = false
        for ((rawId, rawValue) in profiles.entries) {
            if (loaded.size >= MAX_PROFILES) {
                rewriteRequired = true
                break
            }
            val id = rawId as? String
            val objectValue = rawValue as? Map<*, *>
            if (id == null || objectValue == null || !isPermittedProfile(id)) {
                rewriteRequired = true
                continue
            }
            val record = decodeRecord(id, objectValue)
            if (record == null) {
                rewriteRequired = true
                continue
            }
            loaded[id] = record
        }
        return LoadResult(loaded, rewriteRequired)
    }

    private fun decodeRecord(profileId: String, obj: Map<*, *>): StoredNetworkSettings? {
        val vpnEnabled = obj["vpnEnabled"] as? Boolean ?: return null
        // Disabled settings have no legitimate secret to restore. Dropping them also erases old
        // state where configText was retained while the toggle was off.
        if (!vpnEnabled) return null
        val providerName = obj["provider"] as? String ?: return null
        val provider = runCatching { VpnProvider.valueOf(providerName) }.getOrNull()
            ?: return null
        if (provider != VpnProvider.WIREGUARD) return null
        val autoConnect = obj["autoConnectOnBrowserStart"] as? Boolean ?: false
        val requireVpnBeforeBrowse = obj["requireVpnBeforeBrowse"] as? Boolean ?: false
        val secret = obj["secret"] as? Map<*, *> ?: return null
        val ciphertext = decodeCiphertext(secret) ?: return null
        val plaintext = try {
            crypto.decrypt(ciphertext, associatedData(profileId, provider))
        } catch (_: BrowserNetworkCryptoException) {
            return null
        }
        val configText = try {
            decodeUtf8Strict(plaintext)
        } catch (_: CharacterCodingException) {
            return null
        } finally {
            plaintext.fill(0)
        }
        if (configText.toByteArray(StandardCharsets.UTF_8).size > MAX_CONFIG_UTF8_BYTES) return null
        val settings = ProfileNetworkSettings(
            vpnEnabled = true,
            provider = provider,
            autoConnectOnBrowserStart = autoConnect,
            requireVpnBeforeBrowse = requireVpnBeforeBrowse,
            configText = configText,
        )
        return settings.validated()?.let { StoredNetworkSettings(it, ciphertext) }
    }

    private fun decodeCiphertext(secret: Map<*, *>): BrowserNetworkCiphertext? {
        val ivText = secret["iv"] as? String ?: return null
        val ciphertextText = secret["ciphertext"] as? String ?: return null
        if (ivText.length > MAX_IV_BASE64_CHARS || ciphertextText.length > MAX_CIPHERTEXT_BASE64_CHARS) {
            return null
        }
        val iv = decodeBase64(ivText) ?: return null
        val ciphertext = decodeBase64(ciphertextText) ?: return null
        if (iv.size != GCM_IV_BYTES || ciphertext.size !in GCM_TAG_BYTES..MAX_CIPHERTEXT_BYTES) return null
        return BrowserNetworkCiphertext(iv, ciphertext)
    }

    private fun decodeBase64(value: String): ByteArray? = try {
        Base64.getDecoder().decode(value)
    } catch (_: IllegalArgumentException) {
        null
    }

    private fun decodeUtf8Strict(value: ByteArray): String = StandardCharsets.UTF_8.newDecoder()
        .onMalformedInput(CodingErrorAction.REPORT)
        .onUnmappableCharacter(CodingErrorAction.REPORT)
        .decode(ByteBuffer.wrap(value))
        .toString()

    private fun associatedData(profileId: String, provider: VpnProvider): ByteArray =
        "$AAD_SCHEMA\u0000$profileId\u0000${provider.name}".toByteArray(StandardCharsets.UTF_8)

    private fun isBounded(ciphertext: BrowserNetworkCiphertext): Boolean =
        ciphertext.initializationVector.size == GCM_IV_BYTES &&
            ciphertext.encryptedBytes.size in GCM_TAG_BYTES..MAX_CIPHERTEXT_BYTES

    private fun persist(next: Map<String, StoredNetworkSettings>) {
        val json = NetworkStoreJson.encode(next)
        check(json.toByteArray(StandardCharsets.UTF_8).size <= MAX_FILE_BYTES) {
            "browser network store exceeded its file bound"
        }

        val parent = requireNotNull(file.absoluteFile.parentFile) {
            "Browser network store path must have a parent directory"
        }
        if (parent.exists() && !parent.isDirectory) {
            throw IOException("Browser network store parent is not a directory")
        }
        if (!parent.exists() && !parent.mkdirs() && !parent.isDirectory) {
            throw IOException("Could not create browser network store directory")
        }
        val temporary = temporaryFile()
        val backup = backupFile()
        FileOutputStream(temporary, false).use { stream ->
            stream.write(json.toByteArray(StandardCharsets.UTF_8))
            stream.fd.sync()
        }

        if (backup.exists() && !backup.delete()) {
            temporary.delete()
            throw IOException("Could not replace browser network store backup")
        }
        if (file.exists() && !file.renameTo(backup)) {
            temporary.delete()
            throw IOException("Could not back up browser network store")
        }
        if (!temporary.renameTo(file)) {
            backup.renameTo(file)
            temporary.delete()
            throw IOException("Could not replace browser network store")
        }
        current = next
        if (backup.exists() && !backup.delete()) {
            throw IOException("Could not remove browser network store backup")
        }
    }

    /** Replacing raw legacy data is best effort, but failure still fails closed and removes it. */
    private fun rewriteSafely() {
        try {
            persist(current)
        } catch (_: IOException) {
            current = emptyMap()
            deleteSensitiveArtifacts()
        }
    }

    private fun restoreBackupIfNeeded() {
        if (file.exists()) return
        val backup = backupFile()
        if (backup.isFile) backup.renameTo(file)
    }

    private fun removeStaleArtifacts() {
        temporaryFile().takeIf(File::exists)?.delete()
        backupFile().takeIf(File::exists)?.delete()
    }

    private fun deleteSensitiveArtifacts() {
        file.takeIf(File::exists)?.delete()
        temporaryFile().takeIf(File::exists)?.delete()
        backupFile().takeIf(File::exists)?.delete()
    }

    private fun temporaryFile(): File = File(requireNotNull(file.absoluteFile.parentFile), "${file.name}.tmp")

    private fun backupFile(): File = File(requireNotNull(file.absoluteFile.parentFile), "${file.name}.bak")

    private fun isValidProfileId(value: String): Boolean =
        value.length in 1..MAX_PROFILE_ID_CHARS &&
            value.all { it.isLetterOrDigit() || it == '-' || it == '_' }

    private object NetworkStoreJson {
        fun encode(profiles: Map<String, StoredNetworkSettings>): String = buildString {
            append("{\"version\":").append(FORMAT_VERSION)
            append(",\"profiles\":{")
            var first = true
            for ((id, stored) in profiles) {
                if (!first) append(',')
                first = false
                appendString(id)
                append(":{\"vpnEnabled\":true,\"provider\":")
                appendString(stored.settings.provider.name)
                append(",\"autoConnectOnBrowserStart\":")
                append(stored.settings.autoConnectOnBrowserStart)
                append(",\"requireVpnBeforeBrowse\":")
                append(stored.settings.requireVpnBeforeBrowse)
                append(",\"secret\":{\"iv\":")
                appendString(Base64.getEncoder().encodeToString(stored.ciphertext.initializationVector))
                append(",\"ciphertext\":")
                appendString(Base64.getEncoder().encodeToString(stored.ciphertext.encryptedBytes))
                append("}}")
            }
            append("}}")
        }

        fun parse(text: String): Any? = Parser(text).parseDocument()

        private fun StringBuilder.appendString(value: String) {
            append('"')
            value.forEach { character ->
                when (character) {
                    '"' -> append("\\\"")
                    '\\' -> append("\\\\")
                    '\b' -> append("\\b")
                    '\u000C' -> append("\\f")
                    '\n' -> append("\\n")
                    '\r' -> append("\\r")
                    '\t' -> append("\\t")
                    else -> if (character.code < 0x20) {
                        append("\\u").append(character.code.toString(16).padStart(4, '0'))
                    } else {
                        append(character)
                    }
                }
            }
            append('"')
        }

        private class Parser(private val source: String) {
            private var index = 0

            fun parseDocument(): Any? {
                skipWhitespace()
                val value = parseValue(depth = 0)
                skipWhitespace()
                require(index == source.length) { "Trailing JSON content" }
                return value
            }

            private fun parseValue(depth: Int): Any? {
                require(depth <= MAX_JSON_DEPTH) { "JSON nesting is too deep" }
                skipWhitespace()
                require(index < source.length) { "Missing JSON value" }
                return when (source[index]) {
                    '{' -> parseObject(depth)
                    '"' -> parseString()
                    't' -> parseLiteral("true", true)
                    'f' -> parseLiteral("false", false)
                    'n' -> parseLiteral("null", null)
                    '-', in '0'..'9' -> parseNumber()
                    else -> throw IllegalArgumentException("Invalid JSON value")
                }
            }

            private fun parseObject(depth: Int): Map<String, Any?> {
                expect('{')
                val result = LinkedHashMap<String, Any?>()
                skipWhitespace()
                if (peek('}')) {
                    expect('}')
                    return result
                }
                while (true) {
                    require(result.size < MAX_JSON_OBJECT_MEMBERS) { "Too many JSON object members" }
                    val key = parseString()
                    skipWhitespace()
                    expect(':')
                    result[key] = parseValue(depth + 1)
                    skipWhitespace()
                    when {
                        peek(',') -> {
                            expect(',')
                            skipWhitespace()
                        }
                        peek('}') -> {
                            expect('}')
                            return result
                        }
                        else -> throw IllegalArgumentException("Invalid JSON object")
                    }
                }
            }

            private fun parseString(): String {
                expect('"')
                val out = StringBuilder()
                while (index < source.length) {
                    require(out.length < MAX_JSON_STRING_CODE_UNITS) { "JSON string too long" }
                    when (val character = source[index++]) {
                        '"' -> return out.toString()
                        '\\' -> {
                            require(index < source.length) { "Truncated escape" }
                            when (val escaped = source[index++]) {
                                '"', '\\', '/' -> out.append(escaped)
                                'b' -> out.append('\b')
                                'f' -> out.append('\u000C')
                                'n' -> out.append('\n')
                                'r' -> out.append('\r')
                                't' -> out.append('\t')
                                'u' -> {
                                    require(index + 4 <= source.length) { "Truncated unicode escape" }
                                    val hex = source.substring(index, index + 4)
                                    out.append(hex.toInt(16).toChar())
                                    index += 4
                                }
                                else -> throw IllegalArgumentException("Invalid escape")
                            }
                        }
                        else -> {
                            require(character.code >= 0x20) { "Unescaped control character" }
                            out.append(character)
                        }
                    }
                }
                throw IllegalArgumentException("Unterminated string")
            }

            private fun parseNumber(): Long {
                val start = index
                if (peek('-')) index++
                require(index < source.length && source[index] in '0'..'9') { "Invalid number" }
                if (source[index] == '0') {
                    index++
                } else {
                    while (index < source.length && source[index] in '0'..'9') index++
                }
                require(index == source.length || source[index] !in setOf('.', 'e', 'E')) {
                    "Only integer JSON numbers are supported"
                }
                return source.substring(start, index).toLong()
            }

            private fun parseLiteral(literal: String, value: Any?): Any? {
                require(source.startsWith(literal, index)) { "Invalid JSON literal" }
                index += literal.length
                return value
            }

            private fun expect(character: Char) {
                require(index < source.length && source[index] == character) {
                    "Expected '$character'"
                }
                index++
            }

            private fun peek(character: Char): Boolean =
                index < source.length && source[index] == character

            private fun skipWhitespace() {
                while (index < source.length && source[index].isWhitespace()) index++
            }
        }
    }
}
