package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserLibraryAction
import com.rextechnologies.flint.protocol.wire.BrowserLibraryCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntry as WireLibraryEntry
import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntryKind
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import java.io.File
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

class BrowserProfileSessionTest {
    private val directory = File(
        System.getProperty("java.io.tmpdir"),
        "flint-profile-session-${System.nanoTime()}",
    )
    private val store = BrowserLibraryStore(File(directory, "library.json")) { "kids" }
    private val sent = mutableListOf<BrowserLibraryCommandMessage>()
    private val session = BrowserProfileSession(store, sent::add)

    @AfterTest
    fun cleanUp() {
        directory.deleteRecursively()
    }

    @Test
    fun `named TV profiles remain isolated and selected after restart`() {
        assertTrue(session.addBookmark("https://adult.test/", "Adults"))
        val kids = assertNotNull(session.createTvProfile("Kids"))
        assertTrue(session.addBookmark("https://kids.test/", "Kids"))

        assertTrue(session.selectTvProfile(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertEquals("Adults", session.snapshot().library.bookmarks.single().title)
        assertTrue(session.selectTvProfile(kids.id))
        assertEquals("Kids", session.snapshot().library.bookmarks.single().title)

        val reopened = BrowserLibraryStore(File(directory, "library.json"))
        assertEquals(kids.id, reopened.profilesSnapshot().activeProfileId)
        assertEquals("Kids", reopened.snapshot().bookmarks.single().title)
    }

    @Test
    fun `connected device projection is requested then forgotten without touching TV profiles`() {
        assertTrue(session.addBookmark("https://tv.test/", "TV"))
        session.connectDevice(sessionId = 41, name = "Tobi's laptop")

        assertTrue(session.requestConnectedDevice(epoch = 9))
        assertEquals(BrowserLibraryAction.REQUEST_SNAPSHOT, sent.single().action)
        assertTrue(session.snapshot().profiles.switchingToDevice)
        assertTrue(
            session.acceptDeviceSnapshot(
                BrowserLibraryStateMessage(
                    epoch = 9,
                    revision = 1,
                    bookmarks = listOf(wireEntry(BrowserLibraryEntryKind.BOOKMARK, "https://pc.test/", "PC")),
                    history = emptyList(),
                ),
                currentEpoch = 9,
            ),
        )

        assertEquals(BrowserProfileSource.CONNECTED_DEVICE, session.snapshot().profiles.activeSource)
        assertEquals("PC", session.snapshot().library.bookmarks.single().title)
        assertTrue(session.addBookmark("https://from-tv.test/", "Remote save"))
        assertEquals(BrowserLibraryAction.ADD_BOOKMARK, sent.last().action)
        assertEquals("TV", store.snapshot().bookmarks.single().title)

        assertTrue(session.disconnectDevice(41))
        assertEquals(BrowserProfileSource.TV, session.snapshot().profiles.activeSource)
        assertEquals("TV", session.snapshot().library.bookmarks.single().title)
        assertFalse(session.snapshot().profiles.deviceAvailable)
        assertEquals("TV", BrowserLibraryStore(File(directory, "library.json")).snapshot().bookmarks.single().title)
    }

    @Test
    fun `stale wrong epoch and invalid device snapshots never replace the visible projection`() {
        session.connectDevice(7, "Laptop")
        assertTrue(session.requestConnectedDevice(12))
        val valid = BrowserLibraryStateMessage(
            12,
            3,
            listOf(wireEntry(BrowserLibraryEntryKind.BOOKMARK, "https://valid.test/", "Valid")),
            emptyList(),
        )
        assertTrue(session.acceptDeviceSnapshot(valid, currentEpoch = 12))

        assertFalse(session.acceptDeviceSnapshot(valid, currentEpoch = 12))
        assertFalse(session.acceptDeviceSnapshot(valid.copy(epoch = 11, revision = 4), currentEpoch = 12))
        assertFalse(
            session.acceptDeviceSnapshot(
                valid.copy(
                    revision = 4,
                    bookmarks = listOf(
                        wireEntry(BrowserLibraryEntryKind.BOOKMARK, "https://VALID.test:443/", "Not canonical"),
                    ),
                ),
                currentEpoch = 12,
            ),
        )
        assertEquals("Valid", session.snapshot().library.bookmarks.single().title)
    }

    @Test
    fun `TV mutations are durable while device mutations wait for authoritative replacement`() {
        assertTrue(session.recordVisit("https://tv.test/", "TV visit"))
        session.connectDevice(8, "Desktop")
        assertTrue(session.requestConnectedDevice(14))
        assertTrue(
            session.acceptDeviceSnapshot(
                BrowserLibraryStateMessage(14, 1, emptyList(), emptyList()),
                currentEpoch = 14,
            ),
        )

        assertTrue(session.clearHistory(epoch = 14))
        assertEquals(BrowserLibraryAction.CLEAR_HISTORY, sent.last().action)
        assertEquals("TV visit", store.snapshot().history.single().title)
        // Visits are derived by Windows from ordered loaded-page state, never persisted by the TV.
        assertFalse(session.recordVisit("https://device.test/", "Device visit"))
        assertTrue(session.snapshot().library.history.isEmpty())
    }

    @Test
    fun `deleting a durable TV profile invokes its credential cascade hook once`() {
        val cascaded = mutableListOf<String>()
        val hooked = BrowserProfileSession(
            store = store,
            sendToDevice = sent::add,
            onTvProfileDeleted = cascaded::add,
        )
        val kids = assertNotNull(hooked.createTvProfile("Kids"))

        assertTrue(hooked.deleteTvProfile(kids.id))
        assertEquals(listOf(kids.id), cascaded)
        assertFalse(hooked.deleteTvProfile(kids.id))
        assertEquals(listOf(kids.id), cascaded)
    }

    private fun wireEntry(kind: BrowserLibraryEntryKind, url: String, title: String) = WireLibraryEntry(
        kind = kind,
        faviconId = 0,
        lastVisitedMs = 10,
        url = url,
        title = title,
    )
}
