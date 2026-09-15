package com.rextechnologies.flint.receiver.browser

import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.UUID

/** A bookmark or history visit. URLs are canonical HTTPS addresses. */
data class BrowserLibraryEntry(
    val url: String,
    val title: String,
    val faviconId: Long = 0,
    val lastVisitedMs: Long = 0,
)

data class BrowserLibraryState(
    val bookmarks: List<BrowserLibraryEntry> = emptyList(),
    val history: List<BrowserLibraryEntry> = emptyList(),
)

/** Stable, display-safe identity for a browser profile that lives on this TV. */
data class BrowserLibraryProfile(
    val id: String,
    val name: String,
)

/** A defensive snapshot of the TV profile picker. */
data class BrowserLibraryProfilesState(
    val activeProfileId: String,
    val profiles: List<BrowserLibraryProfile>,
)

data class BrowserSavedTab(val url: String, val title: String = "")

data class BrowserTvBrowsingSession(
    val tabs: List<BrowserSavedTab> = emptyList(),
    val activeTab: Int = 0,
    val workspaceMode: Boolean = false,
)

private data class StoredBrowserLibraryProfile(
    val profile: BrowserLibraryProfile,
    val library: BrowserLibraryState,
    val session: BrowserTvBrowsingSession = BrowserTvBrowsingSession(),
)

private data class BrowserLibraryCatalog(
    val activeProfileId: String,
    val profiles: List<StoredBrowserLibraryProfile>,
)

/**
 * Small persistent TV-side browser profile repository.
 *
 * Every TV profile owns an isolated bookmark/history library. Profile IDs and the active selection
 * survive restart. Both collections are newest-first, canonical-URL deduplicated and strictly
 * bounded before they touch disk. A malformed or future-incompatible file recovers to one usable
 * default profile. Persistence uses a temporary file plus backup rename so a power loss cannot
 * leave the only copy half-written on API 25 devices.
 *
 * Version-one single-library files remain readable and are written as a version-two profile catalog
 * by the next mutation. Existing callers keep operating on [snapshot] and the mutation methods;
 * those methods always target the currently selected TV profile.
 */
class BrowserLibraryStore(
    private val file: File,
    private val urlPolicy: BrowserUrlPolicy = BrowserUrlPolicy(),
    private val profileIdFactory: () -> String = { UUID.randomUUID().toString() },
) {
    companion object {
        const val MAX_BOOKMARKS = 200

        // The phase-two browser protocol encodes each collection count as an unsigned byte.
        const val MAX_HISTORY = 0xff
        const val MAX_TITLE_UTF8_BYTES = 512
        const val MAX_PROFILES = 8
        const val MAX_PROFILE_NAME_UTF8_BYTES = 64
        const val MAX_PROFILE_ID_CHARS = 64
        const val MAX_FILE_BYTES = 16 * 1024 * 1024
        const val DEFAULT_PROFILE_ID = "default"
        const val DEFAULT_PROFILE_NAME = "Default"
        private const val FORMAT_VERSION = 2L
        private const val LEGACY_FORMAT_VERSION = 1L
        private const val MAX_PROFILE_ID_ATTEMPTS = 32
    }

    private var current: BrowserLibraryCatalog = load()

    @Synchronized
    fun profilesSnapshot(): BrowserLibraryProfilesState = BrowserLibraryProfilesState(
        activeProfileId = current.activeProfileId,
        profiles = current.profiles.map { it.profile.copy() },
    )

    @Synchronized
    fun snapshot(): BrowserLibraryState = copy(activeStoredProfile().library)

    @Synchronized
    fun browsingSession(): BrowserTvBrowsingSession = copySession(activeStoredProfile().session)

    @Synchronized
    fun saveBrowsingSession(session: BrowserTvBrowsingSession): Boolean {
        val sanitized = sanitizeSession(session) ?: return false
        val activeIndex = current.profiles.indexOfFirst { it.profile.id == current.activeProfileId }
        if (activeIndex < 0) return false
        if (current.profiles[activeIndex].session == sanitized) return true
        val profiles = current.profiles.toMutableList()
        profiles[activeIndex] = profiles[activeIndex].copy(session = sanitized)
        persist(current.copy(profiles = profiles))
        return true
    }

    @Synchronized
    fun profileSnapshot(profileId: String): BrowserLibraryState? =
        current.profiles.firstOrNull { it.profile.id == profileId }?.library?.let(::copy)

    /** Creates and selects a profile. Invalid, duplicate or over-limit requests return null. */
    @Synchronized
    fun createProfile(rawName: String): BrowserLibraryProfile? {
        if (current.profiles.size >= MAX_PROFILES) return null
        val name = sanitizeProfileName(rawName).takeIf(String::isNotEmpty) ?: return null
        if (current.profiles.any { it.profile.name.equals(name, ignoreCase = true) }) return null
        val profile = BrowserLibraryProfile(nextProfileId(), name)
        persist(
            current.copy(
                activeProfileId = profile.id,
                profiles = current.profiles + StoredBrowserLibraryProfile(profile, BrowserLibraryState()),
            ),
        )
        return profile
    }

    @Synchronized
    fun renameProfile(profileId: String, rawName: String): Boolean {
        val index = current.profiles.indexOfFirst { it.profile.id == profileId }
        if (index < 0) return false
        val name = sanitizeProfileName(rawName).takeIf(String::isNotEmpty) ?: return false
        if (current.profiles.any {
                it.profile.id != profileId && it.profile.name.equals(name, ignoreCase = true)
            }
        ) {
            return false
        }
        if (current.profiles[index].profile.name == name) return true
        val profiles = current.profiles.toMutableList()
        profiles[index] = profiles[index].copy(profile = profiles[index].profile.copy(name = name))
        persist(current.copy(profiles = profiles))
        return true
    }

    @Synchronized
    fun switchProfile(profileId: String): Boolean {
        if (current.profiles.none { it.profile.id == profileId }) return false
        if (current.activeProfileId == profileId) return true
        persist(current.copy(activeProfileId = profileId))
        return true
    }

    @Synchronized
    fun deleteProfile(profileId: String): Boolean {
        if (current.profiles.size == 1) return false
        if (current.profiles.none { it.profile.id == profileId }) return false
        val profiles = current.profiles.filterNot { it.profile.id == profileId }
        val activeId = if (current.activeProfileId == profileId) {
            profiles.first().profile.id
        } else {
            current.activeProfileId
        }
        persist(BrowserLibraryCatalog(activeId, profiles))
        return true
    }

    @Synchronized
    fun addBookmark(
        rawUrl: String,
        title: String,
        faviconId: Long = 0,
        nowMs: Long = System.currentTimeMillis(),
    ): Boolean {
        val entry = entry(rawUrl, title, faviconId, nowMs) ?: return false
        val library = activeStoredProfile().library
        val bookmarks = (listOf(entry) + library.bookmarks.filterNot { it.url == entry.url })
            .take(MAX_BOOKMARKS)
        persistActive(library.copy(bookmarks = bookmarks))
        return true
    }

    @Synchronized
    fun removeBookmark(rawUrl: String): Boolean {
        val canonical = canonical(rawUrl) ?: return false
        val library = activeStoredProfile().library
        val next = library.bookmarks.filterNot { it.url == canonical }
        if (next.size == library.bookmarks.size) return false
        persistActive(library.copy(bookmarks = next))
        return true
    }

    @Synchronized
    fun recordVisit(
        rawUrl: String,
        title: String,
        faviconId: Long = 0,
        nowMs: Long = System.currentTimeMillis(),
    ): Boolean {
        val entry = entry(rawUrl, title, faviconId, nowMs) ?: return false
        val library = activeStoredProfile().library
        val history = (listOf(entry) + library.history.filterNot { it.url == entry.url })
            .take(MAX_HISTORY)
        persistActive(library.copy(history = history))
        return true
    }

    @Synchronized
    fun clearBookmarks() {
        val library = activeStoredProfile().library
        if (library.bookmarks.isNotEmpty()) persistActive(library.copy(bookmarks = emptyList()))
    }

    @Synchronized
    fun clearHistory() {
        val library = activeStoredProfile().library
        if (library.history.isNotEmpty()) persistActive(library.copy(history = emptyList()))
    }

    @Synchronized
    fun clearAll() {
        persistActive(BrowserLibraryState())
    }

    private fun entry(rawUrl: String, title: String, faviconId: Long, nowMs: Long): BrowserLibraryEntry? {
        val canonical = canonical(rawUrl) ?: return null
        return BrowserLibraryEntry(
            url = canonical,
            title = sanitizeTitle(title).ifEmpty { truncateUtf8(canonical, MAX_TITLE_UTF8_BYTES) },
            faviconId = faviconId.coerceAtLeast(0),
            lastVisitedMs = nowMs.coerceAtLeast(0),
        )
    }

    private fun canonical(rawUrl: String): String? =
        (urlPolicy.evaluate(rawUrl) as? BrowserUrlResult.Accepted)?.url?.canonicalUrl

    private fun load(): BrowserLibraryCatalog {
        restoreBackupIfNeeded()
        if (!file.isFile || file.length() !in 1..MAX_FILE_BYTES.toLong()) return defaultCatalog()
        return try {
            val root = BrowserLibraryJson.parse(file.readText(StandardCharsets.UTF_8)) as? Map<*, *>
                ?: return defaultCatalog()
            when (root["version"] as? Long) {
                LEGACY_FORMAT_VERSION -> defaultCatalog(decodeLibrary(root))
                FORMAT_VERSION -> decodeCatalog(root)
                else -> defaultCatalog()
            }
        } catch (_: IOException) {
            defaultCatalog()
        } catch (_: IllegalArgumentException) {
            defaultCatalog()
        }
    }

    private fun decodeCatalog(root: Map<*, *>): BrowserLibraryCatalog {
        val seenIds = HashSet<String>()
        val seenNames = HashSet<String>()
        val profiles = (root["profiles"] as? List<*>)
            .orEmpty()
            .mapNotNull { raw ->
                val value = raw as? Map<*, *> ?: return@mapNotNull null
                val id = (value["id"] as? String)?.takeIf(::isValidProfileId)
                    ?: return@mapNotNull null
                val name = (value["name"] as? String)?.let(::sanitizeProfileName)
                    ?.takeIf(String::isNotEmpty)
                    ?: return@mapNotNull null
                if (!seenIds.add(id) || !seenNames.add(name.lowercase(Locale.ROOT))) {
                    return@mapNotNull null
                }
                StoredBrowserLibraryProfile(BrowserLibraryProfile(id, name), decodeLibrary(value), decodeSession(value))
            }
            .take(MAX_PROFILES)
        if (profiles.isEmpty()) return defaultCatalog()
        val requestedActive = root["activeProfileId"] as? String
        val activeId = requestedActive?.takeIf { id -> profiles.any { it.profile.id == id } }
            ?: profiles.first().profile.id
        return BrowserLibraryCatalog(activeId, profiles)
    }

    private fun decodeLibrary(root: Map<*, *>): BrowserLibraryState = BrowserLibraryState(
        bookmarks = decodeEntries(root["bookmarks"], MAX_BOOKMARKS),
        history = decodeEntries(root["history"], MAX_HISTORY),
    )

    private fun decodeSession(root: Map<*, *>): BrowserTvBrowsingSession {
        val tabs = (root["tabs"] as? List<*>).orEmpty().mapNotNull { item ->
            val value = item as? Map<*, *> ?: return@mapNotNull null
            val url = canonical(value["url"] as? String ?: return@mapNotNull null) ?: return@mapNotNull null
            BrowserSavedTab(url, sanitizeTitle(value["title"] as? String ?: ""))
        }.distinctBy { it.url }.take(8)
        val requested = (root["activeTab"] as? Long)?.toInt() ?: 0
        return BrowserTvBrowsingSession(
            tabs = tabs,
            activeTab = requested.coerceIn(0, (tabs.size - 1).coerceAtLeast(0)),
            workspaceMode = root["workspaceMode"] as? Boolean == true,
        )
    }

    private fun sanitizeSession(session: BrowserTvBrowsingSession): BrowserTvBrowsingSession? {
        val tabs = session.tabs.mapNotNull { tab ->
            val url = canonical(tab.url) ?: return@mapNotNull null
            BrowserSavedTab(url, sanitizeTitle(tab.title))
        }.distinctBy { it.url }.take(8)
        return BrowserTvBrowsingSession(
            tabs = tabs,
            activeTab = session.activeTab.coerceIn(0, (tabs.size - 1).coerceAtLeast(0)),
            workspaceMode = session.workspaceMode,
        )
    }

    private fun decodeEntries(raw: Any?, maximum: Int): List<BrowserLibraryEntry> {
        val decoded = (raw as? List<*>)
            .orEmpty()
            .mapNotNull { item ->
                val value = item as? Map<*, *> ?: return@mapNotNull null
                entry(
                    rawUrl = value["url"] as? String ?: return@mapNotNull null,
                    title = value["title"] as? String ?: "",
                    faviconId = value["faviconId"] as? Long ?: 0,
                    nowMs = value["lastVisitedMs"] as? Long ?: 0,
                )
            }
            .sortedByDescending(BrowserLibraryEntry::lastVisitedMs)

        // A malicious file can repeat one valid URL thousands of times. Deduplicate before taking
        // the public bound, retaining the newest occurrence after the sort above.
        val seen = HashSet<String>(decoded.size)
        return decoded.filter { seen.add(it.url) }.take(maximum)
    }

    private fun persistActive(library: BrowserLibraryState) {
        val activeIndex = current.profiles.indexOfFirst { it.profile.id == current.activeProfileId }
        check(activeIndex >= 0) { "Active browser profile is missing" }
        val profiles = current.profiles.toMutableList()
        profiles[activeIndex] = profiles[activeIndex].copy(library = library)
        persist(current.copy(profiles = profiles))
    }

    private fun persist(next: BrowserLibraryCatalog) {
        val json = BrowserLibraryJson.encode(next)
        check(json.toByteArray(StandardCharsets.UTF_8).size <= MAX_FILE_BYTES) {
            "browser library exceeded its file bound"
        }

        val parent = requireNotNull(file.absoluteFile.parentFile) {
            "Browser library path must have a parent directory"
        }
        if (parent.exists() && !parent.isDirectory) {
            throw IOException("Browser library parent is not a directory")
        }
        if (!parent.exists() && !parent.mkdirs() && !parent.isDirectory) {
            throw IOException("Could not create browser library directory")
        }
        val temporary = File(parent, "${file.name}.tmp")
        val backup = File(parent, "${file.name}.bak")
        FileOutputStream(temporary, false).use { stream ->
            stream.write(json.toByteArray(StandardCharsets.UTF_8))
            stream.fd.sync()
        }

        if (backup.exists() && !backup.delete()) {
            temporary.delete()
            throw IOException("Could not replace browser library backup")
        }
        if (file.exists() && !file.renameTo(backup)) {
            temporary.delete()
            throw IOException("Could not back up browser library")
        }
        if (!temporary.renameTo(file)) {
            backup.renameTo(file)
            temporary.delete()
            throw IOException("Could not replace browser library")
        }
        backup.delete()
        current = next
    }

    private fun activeStoredProfile(): StoredBrowserLibraryProfile =
        current.profiles.first { it.profile.id == current.activeProfileId }

    private fun nextProfileId(): String {
        repeat(MAX_PROFILE_ID_ATTEMPTS) {
            val candidate = profileIdFactory()
            if (isValidProfileId(candidate) && current.profiles.none { it.profile.id == candidate }) {
                return candidate
            }
        }
        throw IllegalStateException("Could not generate a unique browser profile ID")
    }

    private fun defaultCatalog(library: BrowserLibraryState = BrowserLibraryState()): BrowserLibraryCatalog =
        BrowserLibraryCatalog(
            activeProfileId = DEFAULT_PROFILE_ID,
            profiles = listOf(
                StoredBrowserLibraryProfile(
                    BrowserLibraryProfile(DEFAULT_PROFILE_ID, DEFAULT_PROFILE_NAME),
                    library,
                ),
            ),
        )

    private fun restoreBackupIfNeeded() {
        if (file.exists()) return
        val parent = file.absoluteFile.parentFile ?: return
        val backup = File(parent, "${file.name}.bak")
        if (backup.isFile) backup.renameTo(file)
    }
}

private fun sanitizeTitle(raw: String): String {
    val printable = buildString(raw.length) {
        var index = 0
        while (index < raw.length) {
            val character = raw[index]
            when {
                Character.isHighSurrogate(character) &&
                    index + 1 < raw.length &&
                    Character.isLowSurrogate(raw[index + 1]) -> {
                    append(character)
                    append(raw[index + 1])
                    index += 2
                }
                Character.isSurrogate(character) -> {
                    append('\uFFFD')
                    index += 1
                }
                character.isISOControl() -> {
                    append(' ')
                    index += 1
                }
                else -> {
                    append(character)
                    index += 1
                }
            }
        }
    }.replace(Regex("\\s+"), " ").trim()
    return truncateUtf8(printable, BrowserLibraryStore.MAX_TITLE_UTF8_BYTES)
}

private fun sanitizeProfileName(raw: String): String {
    val printable = buildString(raw.length) {
        var index = 0
        while (index < raw.length) {
            val codePoint = raw.codePointAt(index)
            when {
                codePoint == 0xfffd -> appendCodePoint(codePoint)
                Character.isSurrogate(raw[index]) && Character.charCount(codePoint) == 1 -> append('\uFFFD')
                Character.isISOControl(
                    codePoint,
                ) || Character.getType(codePoint) == Character.FORMAT.toInt() -> append(' ')
                else -> appendCodePoint(codePoint)
            }
            index += Character.charCount(codePoint)
        }
    }.replace(Regex("\\s+"), " ").trim()
    return truncateUtf8(printable, BrowserLibraryStore.MAX_PROFILE_NAME_UTF8_BYTES)
}

private fun truncateUtf8(value: String, maximumBytes: Int): String {
    var index = 0
    var bytes = 0
    while (index < value.length) {
        val codePoint = value.codePointAt(index)
        val encodedBytes = when {
            codePoint <= 0x7f -> 1
            codePoint <= 0x7ff -> 2
            codePoint <= 0xffff -> 3
            else -> 4
        }
        if (bytes + encodedBytes > maximumBytes) break
        bytes += encodedBytes
        index += Character.charCount(codePoint)
    }
    return if (index == value.length) value else value.substring(0, index)
}

private fun isValidProfileId(value: String): Boolean =
    value.length in 1..BrowserLibraryStore.MAX_PROFILE_ID_CHARS &&
        value.all { it in 'a'..'z' || it in 'A'..'Z' || it in '0'..'9' || it == '-' || it == '_' }

private fun copy(state: BrowserLibraryState): BrowserLibraryState = state.copy(
    bookmarks = state.bookmarks.toList(),
    history = state.history.toList(),
)

private fun copySession(session: BrowserTvBrowsingSession): BrowserTvBrowsingSession = session.copy(
    tabs = session.tabs.toList(),
)

private const val MAX_JSON_DEPTH = 32
private const val MAX_JSON_ARRAY_ITEMS = 1_024
private const val MAX_JSON_OBJECT_MEMBERS = 64
private const val MAX_JSON_STRING_CODE_UNITS = BrowserUrlPolicy.MAX_INPUT_BYTES

/** Minimal strict JSON for the fixed library schema; unknown values remain parseable and ignored. */
private object BrowserLibraryJson {
    fun encode(catalog: BrowserLibraryCatalog): String = buildString {
        append("{\"version\":2,\"activeProfileId\":")
        appendString(catalog.activeProfileId)
        append(",\"profiles\":[")
        catalog.profiles.forEachIndexed { index, stored ->
            if (index != 0) append(',')
            append("{\"id\":")
            appendString(stored.profile.id)
            append(",\"name\":")
            appendString(stored.profile.name)
            append(",\"bookmarks\":")
            appendEntries(stored.library.bookmarks)
            append(",\"history\":")
            appendEntries(stored.library.history)
            append(",\"tabs\":[")
            stored.session.tabs.forEachIndexed { tabIndex, tab ->
                if (tabIndex != 0) append(',')
                append("{\"url\":")
                appendString(tab.url)
                append(",\"title\":")
                appendString(tab.title)
                append('}')
            }
            append("],\"activeTab\":").append(stored.session.activeTab)
            append(",\"workspaceMode\":").append(stored.session.workspaceMode)
            append('}')
        }
        append("]}")
    }

    fun parse(text: String): Any? = Parser(text).parseDocument()

    private fun StringBuilder.appendEntries(entries: List<BrowserLibraryEntry>) {
        append('[')
        entries.forEachIndexed { index, entry ->
            if (index != 0) append(',')
            append("{\"url\":")
            appendString(entry.url)
            append(",\"title\":")
            appendString(entry.title)
            append(",\"faviconId\":").append(entry.faviconId)
            append(",\"lastVisitedMs\":").append(entry.lastVisitedMs)
            append('}')
        }
        append(']')
    }

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
                '[' -> parseArray(depth)
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
            if (consume('}')) return result
            while (true) {
                require(result.size < MAX_JSON_OBJECT_MEMBERS) { "Too many JSON object members" }
                skipWhitespace()
                val key = parseString()
                require(!result.containsKey(key)) { "Duplicate JSON object member" }
                skipWhitespace()
                expect(':')
                result[key] = parseValue(depth + 1)
                skipWhitespace()
                if (consume('}')) return result
                expect(',')
            }
        }

        private fun parseArray(depth: Int): List<Any?> {
            expect('[')
            val result = mutableListOf<Any?>()
            skipWhitespace()
            if (consume(']')) return result
            while (true) {
                require(result.size < MAX_JSON_ARRAY_ITEMS) { "Too many JSON array items" }
                result += parseValue(depth + 1)
                skipWhitespace()
                if (consume(']')) return result
                expect(',')
            }
        }

        private fun parseString(): String {
            expect('"')
            val result = StringBuilder()
            while (index < source.length) {
                val character = source[index++]
                when {
                    character == '"' -> {
                        val value = result.toString()
                        require(!value.hasUnpairedSurrogate()) { "Unpaired Unicode surrogate" }
                        return value
                    }
                    character == '\\' -> result.append(parseEscape())
                    character.code < 0x20 -> throw IllegalArgumentException("Control character in JSON string")
                    else -> result.append(character)
                }
                require(result.length <= MAX_JSON_STRING_CODE_UNITS) { "JSON string is too long" }
            }
            throw IllegalArgumentException("Unterminated JSON string")
        }

        private fun parseEscape(): Char {
            require(index < source.length) { "Missing JSON escape" }
            return when (val escaped = source[index++]) {
                '"', '\\', '/' -> escaped
                'b' -> '\b'
                'f' -> '\u000C'
                'n' -> '\n'
                'r' -> '\r'
                't' -> '\t'
                'u' -> {
                    require(index + 4 <= source.length) { "Short Unicode escape" }
                    val digits = source.substring(index, index + 4)
                    index += 4
                    digits.toIntOrNull(16)?.toChar()
                        ?: throw IllegalArgumentException("Invalid Unicode escape")
                }
                else -> throw IllegalArgumentException("Invalid JSON escape")
            }
        }

        private fun parseNumber(): Long {
            val start = index
            if (source[index] == '-') index += 1
            require(index < source.length && source[index].isDigit()) { "Invalid JSON number" }
            if (source[index] == '0') {
                index += 1
            } else {
                while (index < source.length && source[index].isDigit()) index += 1
            }
            // The library schema contains integers only. Rejecting decimal/exponent syntax keeps a
            // corrupt timestamp from being silently rounded to a different visit.
            return source.substring(start, index).toLongOrNull()
                ?: throw IllegalArgumentException("JSON integer out of range")
        }

        private fun <T> parseLiteral(token: String, value: T): T {
            require(source.regionMatches(index, token, 0, token.length)) { "Invalid JSON literal" }
            index += token.length
            return value
        }

        private fun expect(expected: Char) {
            require(consume(expected)) { "Expected $expected" }
        }

        private fun consume(expected: Char): Boolean {
            if (index >= source.length || source[index] != expected) return false
            index += 1
            return true
        }

        private fun skipWhitespace() {
            while (index < source.length && source[index] in " \t\r\n") index += 1
        }
    }
}

private fun String.hasUnpairedSurrogate(): Boolean {
    var index = 0
    while (index < length) {
        when {
            Character.isHighSurrogate(this[index]) -> {
                if (index + 1 == length || !Character.isLowSurrogate(this[index + 1])) return true
                index += 2
            }
            Character.isLowSurrogate(this[index]) -> return true
            else -> index += 1
        }
    }
    return false
}
