package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspacePage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSavedPane
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSnapshot
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSplit
import java.io.File
import kotlin.test.AfterTest
import kotlin.test.BeforeTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserWorkspaceStoreTest {
    private val directory = File(
        System.getProperty("java.io.tmpdir"),
        "flint-browser-workspace-${System.nanoTime()}",
    )
    private val file = File(directory, "browser-workspace.json")
    private val libraryFile = File(directory, "browser-library.json")
    private lateinit var library: BrowserLibraryStore

    @BeforeTest
    fun setUp() {
        library = BrowserLibraryStore(libraryFile)
    }

    @AfterTest
    fun cleanUp() {
        directory.deleteRecursively()
    }

    @Test
    fun `custom split survives disk restart and old files default to even`() {
        val split = BrowserWorkspaceSplit.of(6500, 2500)
        assertTrue(store().put(BrowserLibraryStore.DEFAULT_PROFILE_ID, sampleSnapshot().copy(split = split)))
        assertEquals(split, store().get(BrowserLibraryStore.DEFAULT_PROFILE_ID)!!.split)
        file.writeText(file.readText().replace(",\"columnSplit\":6500", "").replace(",\"rowSplit\":2500", ""))
        assertEquals(BrowserWorkspaceSplit.Even, store().get(BrowserLibraryStore.DEFAULT_PROFILE_ID)!!.split)
    }

    @Test
    fun `put get and remove survive restart without inventing panes`() {
        val first = store()
        val snapshot = sampleSnapshot()
        assertTrue(first.put(BrowserLibraryStore.DEFAULT_PROFILE_ID, snapshot))
        assertEquals(snapshot.panes.size, first.get(BrowserLibraryStore.DEFAULT_PROFILE_ID)!!.panes.size)

        val restarted = store()
        val restored = restarted.get(BrowserLibraryStore.DEFAULT_PROFILE_ID)!!
        assertEquals(snapshot.layout, restored.layout)
        assertEquals(snapshot.focusedPaneId, restored.focusedPaneId)
        assertEquals("https://example.com/a", restored.panes[0].page.url)
        assertTrue(restored.panes[0].desiredMuted)

        assertTrue(restarted.remove(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertNull(store().get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
    }

    @Test
    fun `malformed file recovers to empty catalog`() {
        directory.mkdirs()
        file.writeText("{not-json")
        assertNull(store().get(BrowserLibraryStore.DEFAULT_PROFILE_ID))
    }

    @Test
    fun `deleted tv profile cascade removes workspace`() {
        val kidsId = library.createProfile("Kids")!!.id
        val store = store()
        val snapshot = sampleSnapshot().copy(ownerProfileId = kidsId)
        assertTrue(store.put(kidsId, snapshot))
        assertTrue(library.deleteProfile(kidsId))
        assertTrue(store.removeForDeletedTvProfile(kidsId))
        assertNull(store.get(kidsId))
        assertFalse(file.readText().contains("example.com"))
    }

    private fun store() = BrowserWorkspaceStore(
        file = file,
        profileAuthority = BrowserLibraryNetworkProfileAuthority(library),
    )

    private fun sampleSnapshot() = BrowserWorkspaceSnapshot(
        ownerProfileId = BrowserLibraryStore.DEFAULT_PROFILE_ID,
        layout = BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
        focusedPaneId = 1,
        nextPaneId = 3,
        panes = listOf(
            BrowserWorkspaceSavedPane(
                id = 1,
                slot = 0,
                page = BrowserWorkspacePage(url = "https://example.com/a", title = "A"),
                desiredMuted = true,
            ),
            BrowserWorkspaceSavedPane(
                id = 2,
                slot = 1,
                page = BrowserWorkspacePage(url = "https://example.com/b", title = "B"),
                desiredMuted = false,
            ),
        ),
    )
}
