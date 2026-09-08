package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class TvSessionRestoreTest {
    @Test
    fun `a blank start reloads the saved strip and workspace mode`() {
        val plan = TvSessionRestore.plan(
            BrowserTvBrowsingSession(
                listOf(
                    BrowserSavedTab("https://example.com/home"),
                    BrowserSavedTab("https://example.com/news"),
                ),
                activeTab = 1,
                workspaceMode = true,
            ),
            currentUrl = null,
        )

        assertEquals("https://example.com/home", plan.navigateCurrentTo)
        assertEquals(listOf("https://example.com/news"), plan.extraUrls)
        assertEquals(1, plan.activeIndex)
        assertTrue(plan.workspaceMode)
    }

    @Test
    fun `an opened URL keeps the front page and appends the other saved tabs`() {
        val plan = TvSessionRestore.plan(
            BrowserTvBrowsingSession(
                listOf(
                    BrowserSavedTab("https://example.com/home"),
                    BrowserSavedTab("https://example.com/news"),
                ),
                activeTab = 1,
                workspaceMode = true,
            ),
            currentUrl = "https://typed.test/",
        )

        assertNull(plan.navigateCurrentTo)
        assertEquals(
            listOf("https://example.com/home", "https://example.com/news"),
            plan.extraUrls,
        )
        assertEquals(0, plan.activeIndex)
        assertFalse(plan.workspaceMode)
    }

    @Test
    fun `the opened URL is not duplicated from the saved strip`() {
        val plan = TvSessionRestore.plan(
            BrowserTvBrowsingSession(
                listOf(
                    BrowserSavedTab("https://example.com/home"),
                    BrowserSavedTab("https://example.com/news"),
                ),
            ),
            currentUrl = "https://example.com/home",
        )

        assertEquals(listOf("https://example.com/news"), plan.extraUrls)
        assertTrue(plan.navigateCurrentTo == null)
    }

    @Test
    fun `an empty saved session is a no-op`() {
        assertTrue(TvSessionRestore.plan(BrowserTvBrowsingSession(), currentUrl = null).isEmpty)
        assertTrue(TvSessionRestore.plan(null, currentUrl = "https://typed.test/").isEmpty)
    }
}
