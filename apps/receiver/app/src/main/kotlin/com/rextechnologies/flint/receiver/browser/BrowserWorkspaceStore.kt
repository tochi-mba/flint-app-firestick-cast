package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspacePage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSavedPane
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSnapshot
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSplit
import com.rextechnologies.flint.receiver.browser.workspace.layoutSupportsPaneCount
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.nio.charset.StandardCharsets

/**
 * Durable TV-side browser workspace snapshots, keyed by local profile ID.
 *
 * No secrets. Device-profile workspaces must never be written here. A malformed file recovers to an
 * empty catalog rather than inventing panes.
 */
class BrowserWorkspaceStore(
    private val file: File,
    private val profileAuthority: BrowserNetworkProfileAuthority,
    private val urlPolicy: BrowserUrlPolicy = BrowserUrlPolicy(),
) {
    companion object {
        const val MAX_PROFILES = BrowserLibraryStore.MAX_PROFILES
        const val MAX_FILE_BYTES = 512 * 1024
        private const val FORMAT_VERSION = 1L
        private const val MAX_PANES = 4
        private const val MAX_URL_CHARS = BrowserUrlPolicy.MAX_INPUT_BYTES
        private const val MAX_TITLE_CHARS = BrowserLibraryStore.MAX_TITLE_CODE_POINTS
    }

    @Volatile
    private var current: Map<String, BrowserWorkspaceSnapshot> = emptyMap()

    init {
        current = load()
    }

    @Synchronized
    fun get(profileId: String): BrowserWorkspaceSnapshot? =
        profileId.takeIf(::isValidProfileId)
            ?.takeIf(profileAuthority::isPersistentTvProfile)
            ?.let(current::get)

    @Synchronized
    fun put(profileId: String, snapshot: BrowserWorkspaceSnapshot): Boolean {
        if (!isPermittedProfile(profileId)) return false
        if (snapshot.ownerProfileId != profileId) return false
        val sanitized = sanitize(snapshot) ?: return false
        if (!current.containsKey(profileId) && current.size >= MAX_PROFILES) return false
        persist(current + (profileId to sanitized))
        return true
    }

    @Synchronized
    fun remove(profileId: String): Boolean {
        if (!isPermittedProfile(profileId)) return false
        if (!current.containsKey(profileId)) return true
        persist(current - profileId)
        return true
    }

    @Synchronized
    fun removeForDeletedTvProfile(profileId: String): Boolean {
        if (!isValidProfileId(profileId) || profileAuthority.isPersistentTvProfile(profileId)) return false
        if (!current.containsKey(profileId)) return true
        persist(current - profileId)
        return true
    }

    private fun sanitize(snapshot: BrowserWorkspaceSnapshot): BrowserWorkspaceSnapshot? {
        if (snapshot.panes.size > MAX_PANES) return null
        if (snapshot.nextPaneId <= 0L) return null
        val layout = snapshot.layout
        if (!layoutSupportsPaneCount(layout, snapshot.panes.size)) return null
        if (snapshot.panes.map { it.id }.toSet().size != snapshot.panes.size) return null
        val panes = snapshot.panes.sortedBy { it.slot }.mapIndexed { index, pane ->
            if (pane.id <= 0L || pane.id == Long.MAX_VALUE || pane.slot != index) return null
            val url = pane.page.url.trim()
            val acceptedUrl = when {
                url.isEmpty() -> ""
                else -> when (val result = urlPolicy.evaluate(url)) {
                    is BrowserUrlResult.Accepted -> result.url.canonicalUrl
                    is BrowserUrlResult.Rejected -> return null
                }
            }
            if (acceptedUrl.length > MAX_URL_CHARS) return null
            val title = BrowserPageTitle.fromPage(pane.page.title)?.value ?: return null
            BrowserWorkspaceSavedPane(
                id = pane.id,
                slot = index,
                page = BrowserWorkspacePage(url = acceptedUrl, title = title),
                desiredMuted = pane.desiredMuted,
            )
        }
        val focused = when {
            panes.isEmpty() -> 0L
            panes.any { it.id == snapshot.focusedPaneId } -> snapshot.focusedPaneId
            else -> panes.first().id
        }
        return BrowserWorkspaceSnapshot(
            ownerProfileId = snapshot.ownerProfileId,
            split = snapshot.split,
            layout = layout,
            focusedPaneId = focused,
            nextPaneId = maxOf(snapshot.nextPaneId, (panes.maxOfOrNull { it.id } ?: 0L) + 1),
            panes = panes,
        )
    }

    private fun isPermittedProfile(profileId: String): Boolean =
        isValidProfileId(profileId) && profileAuthority.isPersistentTvProfile(profileId)

    private fun isValidProfileId(value: String): Boolean =
        value.length in 1..BrowserLibraryStore.MAX_PROFILE_ID_CHARS &&
            value.all { it in 'a'..'z' || it in 'A'..'Z' || it in '0'..'9' || it == '-' || it == '_' }

    private fun load(): Map<String, BrowserWorkspaceSnapshot> {
        // A failed replacement must leave either the current file or its complete predecessor.
        val backup = File(file.parentFile, "${file.name}.bak")
        if (!file.exists() && backup.isFile) backup.renameTo(file)
        if (!file.isFile) return emptyMap()
        if (file.length() !in 1..MAX_FILE_BYTES.toLong()) return emptyMap()
        return try {
            val root = WorkspaceStoreJson.parse(file.readText(StandardCharsets.UTF_8)) as? Map<*, *>
                ?: return emptyMap()
            if (root["version"] as? Long != FORMAT_VERSION) return emptyMap()
            val workspaces = root["workspaces"] as? Map<*, *> ?: return emptyMap()
            if (workspaces.size > MAX_PROFILES) return emptyMap()
            workspaces.entries.mapNotNull { (key, value) ->
                val profileId = key as? String ?: return@mapNotNull null
                if (!isPermittedProfile(profileId)) return@mapNotNull null
                val body = value as? Map<*, *> ?: return@mapNotNull null
                decodeSnapshot(profileId, body)?.let { profileId to it }
            }.toMap()
        } catch (_: Exception) {
            emptyMap()
        }
    }

    private fun decodeSnapshot(profileId: String, body: Map<*, *>): BrowserWorkspaceSnapshot? {
        val owner = body["ownerProfileId"] as? String ?: return null
        if (owner != profileId) return null
        val layoutName = body["layout"] as? String ?: return null
        val layout = runCatching { BrowserWorkspaceLayout.valueOf(layoutName) }.getOrNull() ?: return null
        val focused = body["focusedPaneId"] as? Long ?: return null
        val nextPaneId = body["nextPaneId"] as? Long ?: return null
        val panesRaw = body["panes"] as? List<*> ?: return null
        if (panesRaw.size > MAX_PANES) return null
        val panes = panesRaw.mapIndexed { index, item ->
            val pane = item as? Map<*, *> ?: return null
            val id = pane["id"] as? Long ?: return null
            val rawSlot = pane["slot"] as? Long ?: return null
            if (rawSlot != index.toLong()) return null
            val slot = rawSlot.toInt()
            val url = pane["url"] as? String ?: ""
            val title = pane["title"] as? String ?: ""
            val muted = pane["desiredMuted"] as? Boolean ?: false
            BrowserWorkspaceSavedPane(
                id = id,
                slot = slot,
                page = BrowserWorkspacePage(url = url, title = title),
                desiredMuted = muted,
            )
        }
        return sanitize(
            BrowserWorkspaceSnapshot(
                ownerProfileId = owner,
                split = BrowserWorkspaceSplit.of(
                    (body["columnSplit"] as? Long ?: 5000L).coerceIn(1500L, 8500L).toInt(),
                    (body["rowSplit"] as? Long ?: 5000L).coerceIn(1500L, 8500L).toInt(),
                ),
                layout = layout,
                focusedPaneId = focused,
                nextPaneId = nextPaneId,
                panes = panes,
            ),
        )
    }

    private fun persist(next: Map<String, BrowserWorkspaceSnapshot>) {
        val encoded = WorkspaceStoreJson.encode(next)
        val bytes = encoded.toByteArray(StandardCharsets.UTF_8)
        if (bytes.size > MAX_FILE_BYTES) throw IOException("workspace store exceeds size limit")
        val parent = file.parentFile ?: throw IOException("workspace store has no parent")
        if (!parent.exists() && !parent.mkdirs()) throw IOException("unable to create workspace store directory")
        val temporary = File(parent, "${file.name}.tmp")
        FileOutputStream(temporary).use { stream ->
            stream.write(bytes)
            stream.fd.sync()
        }
        if (!temporary.renameTo(file)) {
            val backup = File(parent, "${file.name}.bak")
            if (backup.exists() && !backup.delete()) throw IOException("unable to clear workspace backup")
            if (!file.renameTo(backup)) throw IOException("unable to back up workspace store")
            if (!temporary.renameTo(file)) {
                backup.renameTo(file)
                throw IOException("unable to publish workspace store")
            }
            backup.delete()
        }
        current = next
    }

    private object WorkspaceStoreJson {
        fun encode(workspaces: Map<String, BrowserWorkspaceSnapshot>): String = buildString {
            append("{\"version\":1,\"workspaces\":{")
            workspaces.entries.forEachIndexed { index, (profileId, snapshot) ->
                if (index != 0) append(',')
                appendString(profileId)
                append(':')
                append('{')
                append("\"ownerProfileId\":")
                appendString(snapshot.ownerProfileId)
                append(",\"layout\":")
                appendString(snapshot.layout.name)
                append(",\"columnSplit\":").append(snapshot.split.column.tenThousandths)
                append(",\"rowSplit\":").append(snapshot.split.row.tenThousandths)
                append(",\"focusedPaneId\":").append(snapshot.focusedPaneId)
                append(",\"nextPaneId\":").append(snapshot.nextPaneId)
                append(",\"panes\":[")
                snapshot.panes.forEachIndexed { paneIndex, pane ->
                    if (paneIndex != 0) append(',')
                    append("{\"id\":").append(pane.id)
                    append(",\"slot\":").append(pane.slot)
                    append(",\"url\":")
                    appendString(pane.page.url)
                    append(",\"title\":")
                    appendString(pane.page.title)
                    append(",\"desiredMuted\":").append(pane.desiredMuted)
                    append('}')
                }
                append("]}")
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

        private class Parser(private val text: String) {
            private var index = 0

            fun parseDocument(): Any? {
                skipWhitespace()
                val value = parseValue(0)
                skipWhitespace()
                return if (index == text.length) value else null
            }

            private fun parseValue(depth: Int): Any? {
                if (depth > 8) return null
                skipWhitespace()
                if (index >= text.length) return null
                return when (text[index]) {
                    '{' -> parseObject(depth + 1)
                    '[' -> parseArray(depth + 1)
                    '"' -> parseString()
                    't' -> parseLiteral("true", true)
                    'f' -> parseLiteral("false", false)
                    'n' -> parseLiteral("null", null)
                    '-', in '0'..'9' -> parseNumber()
                    else -> null
                }
            }

            private fun parseObject(depth: Int): Map<String, Any?>? {
                index++
                val out = linkedMapOf<String, Any?>()
                skipWhitespace()
                if (peek('}')) {
                    index++
                    return out
                }
                while (index < text.length) {
                    skipWhitespace()
                    val key = parseString() ?: return null
                    skipWhitespace()
                    if (!peek(':')) return null
                    index++
                    val value = parseValue(depth) ?: return null
                    if (out.containsKey(key)) return null
                    out[key] = value
                    skipWhitespace()
                    when {
                        peek(',') -> index++
                        peek('}') -> {
                            index++
                            return out
                        }
                        else -> return null
                    }
                }
                return null
            }

            private fun parseArray(depth: Int): List<Any?>? {
                index++
                val out = mutableListOf<Any?>()
                skipWhitespace()
                if (peek(']')) {
                    index++
                    return out
                }
                while (index < text.length) {
                    val value = parseValue(depth) ?: return null
                    out += value
                    skipWhitespace()
                    when {
                        peek(',') -> index++
                        peek(']') -> {
                            index++
                            return out
                        }
                        else -> return null
                    }
                }
                return null
            }

            private fun parseString(): String? {
                if (!peek('"')) return null
                index++
                val out = StringBuilder()
                while (index < text.length) {
                    when (val character = text[index++]) {
                        '"' -> return out.toString()
                        '\\' -> {
                            if (index >= text.length) return null
                            when (val escaped = text[index++]) {
                                '"', '\\', '/' -> out.append(escaped)
                                'b' -> out.append('\b')
                                'f' -> out.append('\u000C')
                                'n' -> out.append('\n')
                                'r' -> out.append('\r')
                                't' -> out.append('\t')
                                'u' -> {
                                    if (index + 4 > text.length) return null
                                    val hex = text.substring(index, index + 4)
                                    out.append(hex.toIntOrNull(16)?.toChar() ?: return null)
                                    index += 4
                                }
                                else -> return null
                            }
                        }
                        else -> {
                            if (character.code < 0x20) return null
                            out.append(character)
                        }
                    }
                }
                return null
            }

            private fun parseNumber(): Long? {
                val start = index
                if (peek('-')) index++
                if (index >= text.length || text[index] !in '0'..'9') return null
                while (index < text.length && text[index] in '0'..'9') index++
                return text.substring(start, index).toLongOrNull()
            }

            private fun parseLiteral(literal: String, value: Any?): Any? {
                if (!text.startsWith(literal, index)) return null
                index += literal.length
                return value
            }

            private fun skipWhitespace() {
                while (index < text.length && text[index].isWhitespace()) index++
            }

            private fun peek(expected: Char): Boolean = index < text.length && text[index] == expected
        }
    }
}
