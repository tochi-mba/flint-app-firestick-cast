package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserLibraryAction
import com.rextechnologies.flint.protocol.wire.BrowserLibraryCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntryKind
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import java.nio.charset.StandardCharsets

/** The authority whose bookmark/history library is currently visible on the TV. */
enum class BrowserProfileSource {
    TV,
    CONNECTED_DEVICE,
}

/** Profile-picker state containing no connected-device library contents. */
data class BrowserProfilesUiState(
    val tvProfiles: List<BrowserLibraryProfile> = emptyList(),
    val activeTvProfileId: String = BrowserLibraryStore.DEFAULT_PROFILE_ID,
    val activeSource: BrowserProfileSource = BrowserProfileSource.TV,
    val deviceAvailable: Boolean = false,
    val deviceName: String? = null,
    val switchingToDevice: Boolean = false,
) {
    val activeName: String
        get() = if (activeSource == BrowserProfileSource.CONNECTED_DEVICE) {
            deviceName ?: "Connected device"
        } else {
            tvProfiles.firstOrNull { it.id == activeTvProfileId }?.name
                ?: BrowserLibraryStore.DEFAULT_PROFILE_NAME
        }

    val storageDescription: String
        get() = if (activeSource == BrowserProfileSource.CONNECTED_DEVICE) {
            "Bookmarks and history stay on $activeName"
        } else {
            "Bookmarks and history are stored on this TV"
        }
}

/** One atomic view of the picker and whichever bounded library it currently owns. */
data class BrowserProfileSessionSnapshot(
    val profiles: BrowserProfilesUiState,
    val library: BrowserLibraryState,
)

/**
 * Coordinates N durable TV libraries with one authenticated, memory-only device projection.
 *
 * A direction is an ownership claim: TV-selected mutations touch [store], while device-selected
 * mutations are requests to the authenticated host and become visible only after its next full
 * replacement snapshot. Device rows are never passed to [BrowserLibraryStore].
 */
class BrowserProfileSession(
    private val store: BrowserLibraryStore,
    private val sendToDevice: (BrowserLibraryCommandMessage) -> Boolean,
    /** Invoked only after a durable TV profile was actually removed from [store]. */
    private val onTvProfileDeleted: (String) -> Unit = {},
    private val urlPolicy: BrowserUrlPolicy = BrowserUrlPolicy(),
) {
    private data class Device(
        val sessionId: Long,
        val name: String,
    )

    private var device: Device? = null
    private var source = BrowserProfileSource.TV
    private var deviceProjection = BrowserLibraryState()
    private var deviceEpoch: Long? = null
    private var deviceRevision = 0L
    private var nextDeviceCommandId = 1L
    private var switchingToDevice = false

    @Synchronized
    fun snapshot(): BrowserProfileSessionSnapshot {
        val catalog = store.profilesSnapshot()
        val connected = device
        return BrowserProfileSessionSnapshot(
            profiles = BrowserProfilesUiState(
                tvProfiles = catalog.profiles,
                activeTvProfileId = catalog.activeProfileId,
                activeSource = source,
                deviceAvailable = connected != null,
                deviceName = connected?.name,
                switchingToDevice = switchingToDevice,
            ),
            library = if (source == BrowserProfileSource.TV) store.snapshot() else copy(deviceProjection),
        )
    }

    @Synchronized
    fun browsingSession(): BrowserTvBrowsingSession =
        if (source == BrowserProfileSource.TV) store.browsingSession() else BrowserTvBrowsingSession()

    @Synchronized
    fun saveBrowsingSession(session: BrowserTvBrowsingSession): Boolean =
        source == BrowserProfileSource.TV && store.saveBrowsingSession(session)

    /** Announces a paired controller without selecting or persisting anything from it. */
    @Synchronized
    fun connectDevice(sessionId: Long, name: String) {
        require(sessionId > 0) { "Browser device session id must be positive" }
        if (device?.sessionId == sessionId) return
        source = BrowserProfileSource.TV
        clearDeviceProjection()
        device = Device(sessionId, safeDeviceName(name))
        nextDeviceCommandId = 1
    }

    /** Wipes only the matching ephemeral projection and falls back to the last TV selection. */
    @Synchronized
    fun disconnectDevice(sessionId: Long): Boolean {
        if (device?.sessionId != sessionId) return false
        val changedOwner = source == BrowserProfileSource.CONNECTED_DEVICE || switchingToDevice
        source = BrowserProfileSource.TV
        clearDeviceProjection()
        device = null
        return changedOwner
    }

    /** Requests the connected device's authoritative library; adoption waits for its snapshot. */
    @Synchronized
    fun requestConnectedDevice(epoch: Long): Boolean {
        if (device == null || epoch <= 0) return false
        val request = BrowserLibraryCommandMessage(
            epoch = epoch,
            commandId = nextDeviceCommandId++,
            action = BrowserLibraryAction.REQUEST_SNAPSHOT,
        )
        if (!sendToDevice(request)) return false
        switchingToDevice = true
        deviceEpoch = epoch
        deviceRevision = 0
        deviceProjection = BrowserLibraryState()
        return true
    }

    /** Validates and atomically adopts a current host-owned full replacement. */
    @Synchronized
    fun acceptDeviceSnapshot(state: BrowserLibraryStateMessage, currentEpoch: Long): Boolean {
        if (device == null || (!switchingToDevice && source != BrowserProfileSource.CONNECTED_DEVICE)) return false
        if (state.epoch != currentEpoch || state.epoch != deviceEpoch || state.revision <= deviceRevision) return false
        val projection = validateProjection(state) ?: return false
        deviceProjection = projection
        deviceRevision = state.revision
        switchingToDevice = false
        source = BrowserProfileSource.CONNECTED_DEVICE
        return true
    }

    @Synchronized
    fun selectTvProfile(profileId: String): Boolean {
        if (!store.switchProfile(profileId)) return false
        source = BrowserProfileSource.TV
        clearDeviceProjection()
        return true
    }

    @Synchronized
    fun createTvProfile(name: String): BrowserLibraryProfile? {
        val created = store.createProfile(name) ?: return null
        source = BrowserProfileSource.TV
        clearDeviceProjection()
        return created
    }

    @Synchronized
    fun renameTvProfile(profileId: String, name: String): Boolean = store.renameProfile(profileId, name)

    @Synchronized
    fun deleteTvProfile(profileId: String): Boolean {
        val deleted = store.deleteProfile(profileId)
        if (deleted) {
            // Credentials have a narrower lifetime than the library catalog. The callback is
            // intentionally best effort because the profile has already been durably deleted;
            // its authority check still prevents a stale encrypted blob from ever being used.
            runCatching { onTvProfileDeleted(profileId) }
            if (source == BrowserProfileSource.TV) clearDeviceProjection()
        }
        return deleted
    }

    @Synchronized
    fun addBookmark(url: String, title: String, faviconId: Long = 0): Boolean =
        mutateOrSend(BrowserLibraryAction.ADD_BOOKMARK, url, title) {
            store.addBookmark(url, title, faviconId)
        }

    @Synchronized
    fun removeBookmark(url: String): Boolean =
        mutateOrSend(BrowserLibraryAction.REMOVE_BOOKMARK, url, "") {
            store.removeBookmark(url)
        }

    /** Device visits are derived by Windows from ordered loaded-page state/tab snapshots. */
    @Synchronized
    fun recordVisit(url: String, title: String, faviconId: Long = 0): Boolean =
        if (source == BrowserProfileSource.TV) store.recordVisit(url, title, faviconId) else false

    @Synchronized
    fun clearHistory(epoch: Long? = null): Boolean =
        mutateOrSend(BrowserLibraryAction.CLEAR_HISTORY, epoch = epoch) {
            val changed = store.snapshot().history.isNotEmpty()
            store.clearHistory()
            changed
        }

    @Synchronized
    fun clearBookmarks(epoch: Long? = null): Boolean =
        mutateOrSend(BrowserLibraryAction.CLEAR_BOOKMARKS, epoch = epoch) {
            val changed = store.snapshot().bookmarks.isNotEmpty()
            store.clearBookmarks()
            changed
        }

    /** Clears durable lists only for a TV-owned profile; never erases a Windows library. */
    @Synchronized
    fun clearTvLibrary(): Boolean {
        if (source != BrowserProfileSource.TV) return false
        store.clearAll()
        return true
    }

    private fun mutateOrSend(
        action: BrowserLibraryAction,
        url: String = "",
        title: String = "",
        epoch: Long? = null,
        local: () -> Boolean,
    ): Boolean {
        if (source == BrowserProfileSource.TV) return local()
        val activeEpoch = epoch ?: deviceEpoch ?: return false
        return sendToDevice(
            BrowserLibraryCommandMessage(
                epoch = activeEpoch,
                commandId = nextDeviceCommandId++,
                action = action,
                url = url,
                title = title,
            ),
        )
    }

    private fun validateProjection(state: BrowserLibraryStateMessage): BrowserLibraryState? {
        if (state.bookmarks.size > BrowserLibraryStore.MAX_BOOKMARKS ||
            state.history.size > BrowserLibraryStore.MAX_HISTORY
        ) {
            return null
        }
        val bookmarks = validateEntries(state.bookmarks, BrowserLibraryEntryKind.BOOKMARK) ?: return null
        val history = validateEntries(state.history, BrowserLibraryEntryKind.HISTORY) ?: return null
        return BrowserLibraryState(bookmarks, history)
    }

    private fun validateEntries(
        entries: List<com.rextechnologies.flint.protocol.wire.BrowserLibraryEntry>,
        expectedKind: BrowserLibraryEntryKind,
    ): List<BrowserLibraryEntry>? {
        val seen = HashSet<String>(entries.size)
        return entries.map { entry ->
            if (entry.kind != expectedKind || entry.faviconId < 0 || entry.lastVisitedMs < 0) return null
            val accepted = urlPolicy.evaluate(entry.url) as? BrowserUrlResult.Accepted ?: return null
            if (accepted.url.canonicalUrl != entry.url || !seen.add(entry.url)) return null
            val safeTitle = BrowserPageTitle.fromPage(entry.title)?.value ?: return null
            if (safeTitle != entry.title ||
                entry.title.toByteArray(StandardCharsets.UTF_8).size > BrowserLibraryStore.MAX_TITLE_UTF8_BYTES
            ) {
                return null
            }
            BrowserLibraryEntry(entry.url, entry.title, entry.faviconId, entry.lastVisitedMs)
        }
    }

    private fun clearDeviceProjection() {
        switchingToDevice = false
        deviceEpoch = null
        deviceRevision = 0
        deviceProjection = BrowserLibraryState()
    }

    private fun copy(state: BrowserLibraryState) = state.copy(
        bookmarks = state.bookmarks.toList(),
        history = state.history.toList(),
    )

    private fun safeDeviceName(raw: String): String = BrowserPageTitle.fromPage(raw)
        ?.value
        ?.takeIf(String::isNotBlank)
        ?.let { truncateUtf8(it, BrowserLibraryStore.MAX_PROFILE_NAME_UTF8_BYTES) }
        ?: "Connected device"

    private fun truncateUtf8(value: String, maximumBytes: Int): String {
        if (value.toByteArray(StandardCharsets.UTF_8).size <= maximumBytes) return value
        val result = StringBuilder()
        var bytes = 0
        var index = 0
        while (index < value.length) {
            val codePoint = value.codePointAt(index)
            val piece = String(Character.toChars(codePoint))
            val pieceBytes = piece.toByteArray(StandardCharsets.UTF_8).size
            if (bytes + pieceBytes > maximumBytes) break
            result.append(piece)
            bytes += pieceBytes
            index += Character.charCount(codePoint)
        }
        return result.toString()
    }
}
