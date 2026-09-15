package com.rextechnologies.flint.receiver.browser

import java.io.File
import java.io.IOException
import java.nio.charset.StandardCharsets
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserLibraryStoreTest {
    private val directory = File(
        System.getProperty("java.io.tmpdir"),
        "flint-browser-library-${System.nanoTime()}",
    )
    private val file = File(directory, "browser-library.json")

    @AfterTest
    fun cleanUp() {
        directory.deleteRecursively()
    }

    @Test
    fun `a fresh TV always has one usable persistent profile`() {
        val store = store()

        val profiles = store.profilesSnapshot()

        assertEquals(BrowserLibraryStore.DEFAULT_PROFILE_ID, profiles.activeProfileId)
        assertEquals(
            listOf(BrowserLibraryProfile(BrowserLibraryStore.DEFAULT_PROFILE_ID, "Default")),
            profiles.profiles,
        )
        assertEquals(BrowserLibraryState(), store.snapshot())
    }

    @Test
    fun `profiles keep isolated libraries and stable identities across a restart`() {
        val first = store(ids = ArrayDeque(listOf("kids-id")))
        assertTrue(first.addBookmark("https://example.com/adults", "Adults", nowMs = 10))
        val kids = assertNotNull(first.createProfile("Kids"))
        assertEquals("kids-id", kids.id)
        assertTrue(first.addBookmark("https://example.com/kids", "Kids", nowMs = 20))
        assertTrue(first.switchProfile(BrowserLibraryStore.DEFAULT_PROFILE_ID))

        val restarted = store()

        assertEquals(BrowserLibraryStore.DEFAULT_PROFILE_ID, restarted.profilesSnapshot().activeProfileId)
        assertEquals(
            listOf(BrowserLibraryStore.DEFAULT_PROFILE_ID, "kids-id"),
            restarted.profilesSnapshot().profiles.map(BrowserLibraryProfile::id),
        )
        assertEquals("https://example.com/adults", restarted.snapshot().bookmarks.single().url)
        assertEquals(
            "https://example.com/kids",
            restarted.profileSnapshot(kids.id)?.bookmarks?.single()?.url,
        )
    }

    @Test
    fun `creating a profile selects it and rename stores a clean distinct name`() {
        val store = store(ids = ArrayDeque(listOf("guest-id")))

        val guest = assertNotNull(store.createProfile("  Guest\n Room  "))

        assertEquals(guest.id, store.profilesSnapshot().activeProfileId)
        assertEquals("Guest Room", guest.name)
        assertTrue(store.renameProfile(guest.id, "  Family\tTV "))
        assertFalse(store.renameProfile(guest.id, " Default "))
        assertFalse(store.renameProfile("missing", "Someone"))
        assertEquals("Family TV", store().profilesSnapshot().profiles.last().name)
    }

    @Test
    fun `blank duplicate and excess profiles are refused without changing disk`() {
        val generated = ArrayDeque((1..BrowserLibraryStore.MAX_PROFILES).map { "profile-$it" })
        val store = store(ids = generated)

        assertNull(store.createProfile(" \n\t "))
        assertNull(store.createProfile("default"))
        repeat(BrowserLibraryStore.MAX_PROFILES - 1) { index ->
            assertNotNull(store.createProfile("Person ${index + 1}"))
        }
        val before = file.readBytes()

        assertNull(store.createProfile("One too many"))
        assertEquals(BrowserLibraryStore.MAX_PROFILES, store.profilesSnapshot().profiles.size)
        assertTrue(before.contentEquals(file.readBytes()))
    }

    @Test
    fun `deleting the active profile selects a survivor and the final profile cannot be deleted`() {
        val store = store(ids = ArrayDeque(listOf("one", "two")))
        val one = assertNotNull(store.createProfile("One"))
        val two = assertNotNull(store.createProfile("Two"))

        assertTrue(store.deleteProfile(two.id))
        assertEquals(BrowserLibraryStore.DEFAULT_PROFILE_ID, store.profilesSnapshot().activeProfileId)
        assertTrue(store.deleteProfile(BrowserLibraryStore.DEFAULT_PROFILE_ID))
        assertEquals(one.id, store.profilesSnapshot().activeProfileId)
        assertFalse(store.deleteProfile(one.id))
        assertFalse(store.deleteProfile("missing"))

        val restarted = store()
        assertEquals(listOf(one), restarted.profilesSnapshot().profiles)
        assertEquals(one.id, restarted.profilesSnapshot().activeProfileId)
    }

    @Test
    fun `entries are canonical deduplicated bounded and wire safe`() {
        val store = store()
        repeat(BrowserLibraryStore.MAX_HISTORY + 5) { index ->
            assertTrue(
                store.recordVisit(
                    "https://example.com/$index",
                    "\u0000  ${"😀".repeat(300)}  \n",
                    faviconId = -7,
                    nowMs = index.toLong() - 10,
                ),
            )
        }
        assertTrue(store.recordVisit("https://EXAMPLE.com/100", "Newest", nowMs = 9_999))

        val history = store.snapshot().history

        assertEquals(BrowserLibraryStore.MAX_HISTORY, history.size)
        assertEquals(1, history.count { it.url == "https://example.com/100" })
        assertEquals("Newest", history.first().title)
        assertTrue(
            history.all {
                it.title.toByteArray(StandardCharsets.UTF_8).size <=
                    BrowserLibraryStore.MAX_TITLE_UTF8_BYTES
            },
        )
        assertTrue(history.all { entry -> entry.title.none(Char::isISOControl) })
        assertTrue(history.all { it.faviconId >= 0 && it.lastVisitedMs >= 0 })
    }

    @Test
    fun `version one library is loaded as the default profile and upgraded on mutation`() {
        directory.mkdirs()
        file.writeText(
            """{"version":1,"bookmarks":[{"url":"https://example.com/old","title":"Old","faviconId":3,"lastVisitedMs":4}],"history":[]}""",
        )

        val migrated = store()

        assertEquals("https://example.com/old", migrated.snapshot().bookmarks.single().url)
        assertEquals(BrowserLibraryStore.DEFAULT_PROFILE_ID, migrated.profilesSnapshot().activeProfileId)
        assertTrue(migrated.addBookmark("https://example.com/new", "New"))
        assertTrue(file.readText().contains("\"version\":2"))
        assertEquals(2, store().snapshot().bookmarks.size)
    }

    @Test
    fun `invalid persisted profiles recover to one safe default`() {
        directory.mkdirs()
        file.writeText(
            """{"version":2,"activeProfileId":"../../bad","profiles":[{"id":"../../bad","name":"Bad","bookmarks":[],"history":[]}]}""",
        )

        val recovered = store().profilesSnapshot()

        assertEquals(BrowserLibraryStore.DEFAULT_PROFILE_ID, recovered.activeProfileId)
        assertEquals(
            listOf(BrowserLibraryProfile(BrowserLibraryStore.DEFAULT_PROFILE_ID, "Default")),
            recovered.profiles,
        )
    }

    @Test
    fun `a backup is restored if power loss removed the primary file`() {
        val first = store()
        assertTrue(first.addBookmark("https://example.com/safe", "Safe"))
        val backup = File(directory, "${file.name}.bak")
        assertTrue(file.renameTo(backup))

        val restored = store()

        assertEquals("Safe", restored.snapshot().bookmarks.single().title)
        assertTrue(file.isFile)
    }

    @Test
    fun `a failed atomic write leaves the in-memory profile unchanged`() {
        val invalidParent = File(directory, "not-a-directory").apply {
            checkNotNull(parentFile).mkdirs()
            writeText("occupied")
        }
        val store = BrowserLibraryStore(File(invalidParent, "browser-library.json"))
        val before = store.snapshot()

        assertFailsWith<IOException> {
            store.addBookmark("https://example.com/never-written", "Never")
        }

        assertEquals(before, store.snapshot())
    }

    @Test
    fun `generated ids must be safe unique and are never silently rewritten`() {
        val ids = ArrayDeque(listOf("bad/id", BrowserLibraryStore.DEFAULT_PROFILE_ID, "valid-id"))
        val store = store(ids)

        val profile = assertNotNull(store.createProfile("Valid"))

        assertEquals("valid-id", profile.id)
        assertNotEquals(BrowserLibraryStore.DEFAULT_PROFILE_ID, profile.id)
    }

    @Test
    fun `tabs and workspace mode survive restart without sharing across profiles`() {
        val first = store(ids = ArrayDeque(listOf("kids-id")))
        assertTrue(
            first.saveBrowsingSession(
                BrowserTvBrowsingSession(
                    listOf(
                        BrowserSavedTab("https://example.com/home", "Home"),
                        BrowserSavedTab("https://example.com/news", "News"),
                    ),
                    activeTab = 1,
                    workspaceMode = true,
                ),
            ),
        )
        val kids = assertNotNull(first.createProfile("Kids"))
        assertEquals(BrowserTvBrowsingSession(), first.browsingSession())
        assertTrue(first.switchProfile(BrowserLibraryStore.DEFAULT_PROFILE_ID))

        val restarted = store()
        val restored = restarted.browsingSession()
        assertEquals(listOf("https://example.com/home", "https://example.com/news"), restored.tabs.map { it.url })
        assertEquals(1, restored.activeTab)
        assertTrue(restored.workspaceMode)
        assertTrue(restarted.switchProfile(kids.id))
        assertEquals(emptyList(), restarted.browsingSession().tabs)
        assertFalse(restarted.browsingSession().workspaceMode)
    }

    private fun store(ids: ArrayDeque<String> = ArrayDeque()): BrowserLibraryStore =
        BrowserLibraryStore(file) {
            if (ids.isEmpty()) "generated-${System.nanoTime()}" else ids.removeFirst()
        }
}
